using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AttackTargetVisualizer
{
    // 编辑器显式开启会话后才加载资源。这里的相机和粒子副本有自己的位置和生命周期。
    internal sealed class PrefabPreviewBridge
    {
        private const int Width = 480;
        private const int Height = 300;
        private const int FrameLimit = 15;
        private const float FrameInterval = 1f / 12f;
        private const string Mailbox = "VfxEditorBridge";
        private static readonly Vector3 Stage = new Vector3(2048f, 256f, 0f);

        private readonly string _directory;
        private readonly string _inbox;
        private float _nextPoll;
        private float _nextFrame;
        private string _session;
        private int _revision;
        private GameObject _stage;
        private GameObject _preview;
        private Camera _camera;
        private RenderTexture _target;
        private Texture2D _pixels;
        private ParticleSystem[] _systems = Array.Empty<ParticleSystem>();
        private Reply _reply;
        private bool? _runInBackgroundBefore;

        internal PrefabPreviewBridge(string pluginFile)
        {
            _directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pluginFile), "..", "temp", Mailbox));
            _inbox = Path.Combine(_directory, "request.json");
        }

        internal void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextPoll) return;
            _nextPoll = now + 0.2f;
            try
            {
                if (!File.Exists(_inbox) || DateTime.UtcNow - File.GetLastWriteTimeUtc(_inbox) > TimeSpan.FromSeconds(15))
                {
                    if (_session != null) Clear();
                    return;
                }
                if (!PrefabSingleton<UIRoot>.HasInstance) return;
                Request request = JsonConvert.DeserializeObject<Request>(ReadRequest(_inbox));
                if (request == null || !ValidSession(request.session) || request.revision < 1 ||
                    (request.session == _session && request.revision <= _revision)) return;
                StopPreview();
                _session = request.session;
                _revision = request.revision;
                _reply = new Reply { session = _session, revision = _revision, preset = request.preset };
                if (request.operation == "close")
                {
                    RestoreBackground();
                    _reply.phase = "closed";
                    WriteReply();
                    return;
                }
                if (request.operation != "inspect" && request.operation != "play") return;
                try
                {
                    if (!_runInBackgroundBefore.HasValue) _runInBackgroundBefore = Application.runInBackground;
                    Application.runInBackground = true;
                    Prepare(request);
                }
                catch (Exception error)
                {
                    Fail(error);
                }
            }
            catch (IOException)
            {
                // 编辑器可能正在原子替换文件，下一轮再读。
            }
            catch (Exception error)
            {
                AttackTargetPlugin.LogError("预制体预览桥接：" + error.Message);
            }
        }

        private static string ReadRequest(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                if (stream.Length > 32768) throw new IOException("预览请求超过 32 KB");
                return reader.ReadToEnd();
            }
        }

        private static bool ValidSession(string value)
        {
            return value != null && value.Length == 32 && value.All(Uri.IsHexDigit);
        }

        private void Prepare(Request request)
        {
            string path;
            switch (request.preset)
            {
                case "blade-hit": path = "Effect/Prefabs/FX_BladeHit"; break;
                case "charm": path = "Effect/Prefabs/FX_Skill_Charm"; break;
                case "adrenaline": path = "Effect/Prefabs/Buff/FX_Buff_130"; break;
                default: throw new ArgumentException("此版本只加载页面列出的原版预制体");
            }
            GameObject source = Resources.Load<GameObject>(path);
            if (source == null) throw new InvalidOperationException("找不到 " + path);
            _stage = new GameObject("VfxEditorPreviewStage");
            _stage.transform.position = Stage;
            _stage.SetActive(false);
            _preview = Object.Instantiate(source, _stage.transform, false);
            Placement pose = request.placement ?? new Placement();
            _preview.transform.localPosition += new Vector3(Clamp(pose.x, -1.5f, 1.5f, 0f),
                Clamp(pose.y, -1.2f, 1.2f, 0f), 0f);
            _preview.transform.localScale *= Clamp(pose.scale, 0.2f, 4f, 1f);
            _preview.transform.localRotation *= Quaternion.Euler(0f, 0f, Clamp(pose.angle, -180f, 180f, 0f));
            foreach (AudioSource audio in _preview.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;

            ParticleSystem[] systems = _preview.GetComponentsInChildren<ParticleSystem>(true);
            _systems = systems;
            _reply.layers = new Layer[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                Layer layer = new Layer { index = i, name = system.gameObject.name, before = Read(system) };
                Edit edit = request.edits?.FirstOrDefault(e => e != null && e.index == i);
                Apply(system, edit);
                layer.after = Read(system);
                _reply.layers[i] = layer;
                if (request.operation == "play")
                {
                    var particleMain = system.main;
                    particleMain.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    system.useAutoRandomSeed = false;
                    system.randomSeed = (uint)(7429 + i);
                }
            }
            _reply.phase = "ready";
            if (request.operation == "play")
            {
                BeginCapture();
            }
            else StopPreview();
            WriteReply();
            AttackTargetPlugin.LogInfo($"预制体试播 {request.operation} {request.preset}，{systems.Length} 层，序号 {_revision}");
        }

        private void BeginCapture()
        {
            _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "VfxEditorPreviewRT" };
            _target.Create();
            _pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            var cameraGo = new GameObject("VfxEditorPreviewCamera");
            cameraGo.transform.SetParent(_stage.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 0f, -10f);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color32(31, 39, 43, 255);
            _camera.orthographic = true;
            _camera.orthographicSize = 0.90f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 26f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.targetTexture = _target;
            _nextFrame = Time.unscaledTime;
            _reply.phase = "rendering";
            RenderPipelineManager.endCameraRendering += CaptureAfterSrp;
            Camera.onPostRender += CaptureFrame;
            _stage.SetActive(true);

            foreach (ParticleSystem system in _systems)
            {
                if (!system.gameObject.activeInHierarchy) continue;
                // 设为手动逐步模拟，避免切到网页后 Unity 在后台限帧，导致漏掉短促的命中效果。
                system.Play(false);
                system.Pause(false);
            }
        }

        private void CaptureAfterSrp(ScriptableRenderContext context, Camera camera) => CaptureFrame(camera);

        private void CaptureFrame(Camera camera)
        {
            if (camera != _camera || _reply?.phase != "rendering" || Time.unscaledTime < _nextFrame) return;
            try
            {
                int index = _reply.frameCount;
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = _target;
                    _pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                WriteAtomic("frame-" + index + ".jpg", ImageConversion.EncodeToJPG(_pixels, 88));
                _reply.frameCount++;
                if (_reply.frameCount == FrameLimit)
                {
                    _reply.phase = "done";
                    StopPreview();
                    WriteReply();
                    AttackTargetPlugin.LogInfo($"预制体试播完成 {_reply.preset}，采样 {_reply.frameCount} 帧");
                    return;
                }

                // 只在每张图读完后推进一步，浏览器回放的帧率与粒子模拟一致。
                foreach (ParticleSystem system in _systems)
                {
                    if (system.gameObject.activeInHierarchy) system.Simulate(FrameInterval, false, false, false);
                }
                _nextFrame = Time.unscaledTime + 0.025f;
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static Values Read(ParticleSystem system)
        {
            var main = system.main;
            var emission = system.emission;
            var color = main.startColor;
            Color sample = color.Evaluate(0f, 1f);
            return new Values
            {
                size = main.startSize3D ? main.startSizeXMultiplier : main.startSizeMultiplier,
                lifetime = main.startLifetimeMultiplier,
                speed = main.simulationSpeed,
                rate = emission.rateOverTimeMultiplier,
                bursts = ReadBursts(emission),
                color = "#" + ColorUtility.ToHtmlStringRGB(sample),
                colorMode = color.mode.ToString(),
                loop = main.loop
            };
        }

        private static int ReadBursts(ParticleSystem.EmissionModule emission)
        {
            int peak = 0;
            for (int i = 0; i < emission.burstCount; i++)
            {
                peak += Mathf.RoundToInt(emission.GetBurst(i).count.Evaluate(0f, 1f));
            }
            return peak;
        }

        private static void Apply(ParticleSystem system, Edit edit)
        {
            if (edit == null) return;
            var main = system.main;
            float size = Clamp(edit.size, 0.1f, 4f, 1f);
            if (size != 1f)
            {
                if (main.startSize3D)
                {
                    main.startSizeXMultiplier *= size;
                    main.startSizeYMultiplier *= size;
                    main.startSizeZMultiplier *= size;
                }
                else main.startSizeMultiplier *= size;
            }
            main.startLifetimeMultiplier *= Clamp(edit.lifetime, 0.1f, 4f, 1f);
            main.simulationSpeed *= Clamp(edit.speed, 0.1f, 4f, 1f);
            if (!string.IsNullOrEmpty(edit.color) && ColorUtility.TryParseHtmlString(edit.color, out Color tint))
            {
                main.startColor = tint;
            }
            float density = Clamp(edit.density, 0f, 4f, 1f);
            if (density == 1f) return;
            var emission = system.emission;
            emission.rateOverTimeMultiplier *= density;
            emission.rateOverDistanceMultiplier *= density;
            for (int i = 0; i < emission.burstCount; i++)
            {
                var burst = emission.GetBurst(i);
                var count = burst.count;
                switch (count.mode)
                {
                    case ParticleSystemCurveMode.Constant: count.constant *= density; break;
                    case ParticleSystemCurveMode.TwoConstants:
                        count.constantMin *= density;
                        count.constantMax *= density;
                        break;
                    default: count.curveMultiplier *= density; break;
                }
                burst.count = count;
                emission.SetBurst(i, burst);
            }
        }

        private static float Clamp(float value, float low, float high, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || (value == 0f && low > 0f)
                ? fallback : Mathf.Clamp(value, low, high);
        }

        private void Fail(Exception error)
        {
            StopPreview();
            _reply.phase = "error";
            _reply.error = error.Message;
            try { WriteReply(); }
            catch (IOException) { }
            AttackTargetPlugin.LogError("预制体试播失败：" + error);
        }

        private void WriteReply() => WriteAtomic("response.json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_reply)));

        private void WriteAtomic(string name, byte[] bytes)
        {
            string path = Path.Combine(_directory, name);
            string writing = path + ".new";
            File.WriteAllBytes(writing, bytes);
            if (File.Exists(path)) File.Replace(writing, path, null);
            else File.Move(writing, path);
        }

        private void StopPreview()
        {
            RenderPipelineManager.endCameraRendering -= CaptureAfterSrp;
            Camera.onPostRender -= CaptureFrame;
            if (_camera != null)
            {
                _camera.enabled = false;
                _camera.targetTexture = null;
            }
            if (_stage != null)
            {
                _stage.SetActive(false);
                Object.Destroy(_stage);
            }
            if (_pixels != null) Object.Destroy(_pixels);
            if (_target != null)
            {
                _target.Release();
                Object.Destroy(_target);
            }
            _stage = null;
            _preview = null;
            _camera = null;
            _pixels = null;
            _target = null;
            _systems = Array.Empty<ParticleSystem>();
        }

        private void RestoreBackground()
        {
            if (!_runInBackgroundBefore.HasValue) return;
            Application.runInBackground = _runInBackgroundBefore.Value;
            _runInBackgroundBefore = null;
        }

        internal void Clear()
        {
            StopPreview();
            RestoreBackground();
            _session = null;
            _reply = null;
        }

        [Serializable]
        private sealed class Request
        {
            public string session = null;
            public int revision = 0;
            public string operation = null;
            public string preset = null;
            public Placement placement = null;
            public Edit[] edits = null;
        }

        [Serializable]
        private sealed class Placement
        {
            public float x = 0f, y = 0f, scale = 1f, angle = 0f;
        }

        [Serializable]
        private sealed class Edit
        {
            public int index = -1;
            public float size = 1f, lifetime = 1f, speed = 1f, density = 1f;
            public string color = null;
        }

        [Serializable]
        private sealed class Values
        {
            public float size, lifetime, speed, rate;
            public string color, colorMode;
            public int bursts;
            public bool loop;
        }

        [Serializable]
        private sealed class Layer
        {
            public int index;
            public string name;
            public Values before, after;
        }

        [Serializable]
        private sealed class Reply
        {
            public string session, preset, phase, error;
            public int revision, frameCount;
            public Layer[] layers;
        }
    }
}
