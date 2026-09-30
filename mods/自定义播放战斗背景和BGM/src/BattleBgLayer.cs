// 战斗背景叠加层。
//
// 玩法：不改战斗场景本身，而是在战斗相机前叠一整块"背景板"——
//   · 渲染在排序层 BG 的最底部（比场景里所有背景都低），所以会盖住原背景、但不挡角色和前景；
//   · 图片 / 视频都支持；视频循环播放、默认静音；
//   · 显示时有一个"从屏幕右边缘向左铺满"的展开动画（1 秒，RectMask2D 裁剪）；
//   · 战斗结束（或需要还原时）把这一层整个销毁，原背景自然就回来了。

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CustomBattleBg
{
    internal class BattleBgLayer : MonoBehaviour
    {
        private static BattleBgLayer _instance;

        private GameObject _root;
        private Canvas _canvas;
        private RectTransform _canvasRt;
        private RectTransform _maskRt;
        private RectTransform _visualRt;
        private RawImage _visual;
        private VideoPlayer _video;
        private RenderTexture _renderTex;
        private Texture2D _tex;
        private Rect _baseUv = new Rect(0f, 0f, 1f, 1f);
        private bool _hasBaseUv;
        private bool _isVideoContent;
        private bool _halfScreen;
        private float _maskH;
        private Camera _cam;
        private float _canvasW;
        private float _canvasH;
        private Coroutine _reveal;
        private Coroutine _videoCo;
        private int _lastScreenW;
        private int _lastScreenH;

        internal static bool IsShowing
        {
            get { return _instance != null && _instance._root != null; }
        }

        /// <summary>显示（或替换成）一张背景。图片立即展开，视频等准备好再展开。</summary>
        internal static void Show(Camera cam, string filePath, bool isVideo, bool halfScreen)
        {
            if (cam == null || string.IsNullOrEmpty(filePath))
            {
                return;
            }
            if (_instance == null)
            {
                GameObject go = new GameObject("CustomBattleBgLayer");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<BattleBgLayer>();
            }
            _instance.ShowInternal(cam, filePath, isVideo, halfScreen);
        }

        /// <summary>销毁叠加层，背景复原。</summary>
        internal static void Hide()
        {
            if (_instance != null)
            {
                _instance.HideInternal();
            }
        }

        private void ShowInternal(Camera cam, string filePath, bool isVideo, bool halfScreen)
        {
            try
            {
                _cam = cam;
                _isVideoContent = isVideo;
                _halfScreen = halfScreen;
                _hasBaseUv = false;
                EnsureRoot();
                if (_root == null)
                {
                    return;
                }
                _root.transform.SetParent(cam.transform, false);
                _root.transform.localPosition = new Vector3(0f, 0f, 10f);
                _root.transform.localRotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;
                UpdateCanvasSize();
                HideMaskImmediately();
                ClearContent();

                if (isVideo)
                {
                    if (_videoCo != null)
                    {
                        StopCoroutine(_videoCo);
                    }
                    _videoCo = StartCoroutine(ShowVideoCo(filePath));
                }
                else
                {
                    ShowImageNow(filePath);
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("显示战斗背景失败：" + e);
            }
        }

        private void HideInternal()
        {
            try
            {
                if (_reveal != null) { StopCoroutine(_reveal); _reveal = null; }
                if (_videoCo != null) { StopCoroutine(_videoCo); _videoCo = null; }
                ClearContent();
                if (_root != null)
                {
                    UnityEngine.Object.Destroy(_root);
                    _root = null;
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("移除战斗背景层失败：" + e.Message);
            }
        }

        private void EnsureRoot()
        {
            if (_root != null)
            {
                return;
            }
            _root = new GameObject("BattleBgCanvas");
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingLayerName = "BG";
            // 关键：要排在场景自己那些背景元素的**上面**（它们是 BG 层里 order 0~个位数），
            // 但又必须留在 BG 层里——排序层优先于 order，所以仍然会被角色(Chara)/前景(Front)盖住。
            // 之前放在 -1000 时会被原背景整个盖掉，只在边缘露一点（2026-09-30 用户实测反馈）。
            _canvas.sortingOrder = 1000;
            _canvasRt = _root.GetComponent<RectTransform>();
            _canvasRt.pivot = new Vector2(0.5f, 0.5f);
            _canvasRt.anchorMin = new Vector2(0.5f, 0.5f);
            _canvasRt.anchorMax = new Vector2(0.5f, 0.5f);
            _canvasRt.anchoredPosition = Vector2.zero;

            // 裁剪容器：右边缘固定，宽度 0 → 全宽 → "从右往左展开"
            // 锚点取右上角：全屏模式高度=整屏，只占上半屏模式高度=比例高度（下方露出原背景）
            GameObject maskGo = new GameObject("Mask", typeof(RectTransform));
            RectTransform maskRt = maskGo.GetComponent<RectTransform>();
            maskRt.SetParent(_root.transform, false);
            maskRt.anchorMin = new Vector2(1f, 1f);
            maskRt.anchorMax = new Vector2(1f, 1f);
            maskRt.pivot = new Vector2(1f, 1f);
            maskRt.anchoredPosition = Vector2.zero;
            maskRt.sizeDelta = new Vector2(0f, 0f);
            maskGo.AddComponent<RectMask2D>();
            _maskRt = maskRt;

            // 黑色底：视频在"只占上半屏"模式下完整缩进去以后，四周露出来的就是它（黑边）。
            // 全屏模式视频/图片是铺满的，这层看不见，留着无害。
            GameObject blackGo = new GameObject("BlackBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform blackRt = blackGo.GetComponent<RectTransform>();
            blackRt.SetParent(maskRt, false);
            blackRt.anchorMin = Vector2.zero;
            blackRt.anchorMax = Vector2.one;
            blackRt.offsetMin = Vector2.zero;
            blackRt.offsetMax = Vector2.zero;
            Image blackImg = blackGo.GetComponent<Image>();
            blackImg.color = Color.black;
            blackImg.raycastTarget = false;

            // 画面本体：右上角对齐裁剪容器，尺寸始终是整屏——被裁掉的部分由 Mask 决定，
            // 所以"只占上半屏"时画面内容不会缩放，只是下方被遮住。
            GameObject visualGo = new GameObject("Visual", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            RectTransform visualRt = visualGo.GetComponent<RectTransform>();
            visualRt.SetParent(maskRt, false);
            visualRt.anchorMin = new Vector2(1f, 1f);
            visualRt.anchorMax = new Vector2(1f, 1f);
            visualRt.pivot = new Vector2(1f, 1f);
            visualRt.anchoredPosition = Vector2.zero;
            visualRt.sizeDelta = new Vector2(0f, 0f);
            RawImage img = visualGo.GetComponent<RawImage>();
            img.raycastTarget = false;
            img.color = Color.white;
            _visual = img;
            _visualRt = visualRt;
        }

        private void UpdateCanvasSize()
        {
            if (_cam == null || _canvasRt == null || _maskRt == null || _visualRt == null)
            {
                return;
            }
            float h;
            if (_cam.orthographic)
            {
                h = _cam.orthographicSize * 2f;
            }
            else
            {
                float dist = Mathf.Abs(_root != null ? _root.transform.localPosition.z : 10f);
                h = 2f * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * dist;
            }
            float w = h * Mathf.Max(_cam.aspect, 0.1f);

            _canvasW = w;
            _canvasH = h;
            float ratio = Mathf.Clamp(CustomBattleBgPlugin.HalfScreenRatio, 0.1f, 1f);
            _maskH = _halfScreen ? h * ratio : h;
            _canvasRt.sizeDelta = new Vector2(w, h);
            _visualRt.sizeDelta = new Vector2(w, h);
            _maskRt.sizeDelta = new Vector2(_canvasW, _maskH);
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            // 尺寸变了要重新摆一次内容（视频的"包含式"尺寸依赖画布/区域大小）
            if (_isVideoContent)
            {
                LayoutVideo();
            }
            else
            {
                LayoutImage();
            }
        }

        private void HideMaskImmediately()
        {
            if (_maskRt != null)
            {
                _maskRt.sizeDelta = new Vector2(0f, _maskH);
            }
        }

        private void ShowImageNow(string filePath)
        {
            _tex = MediaLoader.LoadTexture(filePath);
            if (_tex == null)
            {
                CustomBattleBgPlugin.LogError("背景图片读取失败，跳过切换：" + filePath);
                return;
            }
            if (_visual != null)
            {
                _visual.texture = _tex;
                _baseUv = BuildCoverUv(_tex.width, _tex.height, true);
                _hasBaseUv = true;
                _visual.uvRect = _baseUv;
            }
            LayoutImage();
            StartReveal();
        }

        private IEnumerator ShowVideoCo(string filePath)
        {
            GameObject vGo = new GameObject("VideoPlayer");
            vGo.transform.SetParent(_root != null ? _root.transform : transform, false);
            VideoPlayer vp = vGo.AddComponent<VideoPlayer>();
            _video = vp;
            vp.playOnAwake = false;
            vp.isLooping = true;
            vp.source = VideoSource.Url;
            vp.url = "file://" + filePath;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.skipOnDrop = true;

            // 视频自带的声音要照常播（用户口径：视频本身有声音就该出声音）。
            // 走一个自己的 AudioSource，并挂到游戏的 BGM 混音组上——这样设置里的"背景音乐"音量能管到它。
            AudioSource vAudio = vGo.AddComponent<AudioSource>();
            vAudio.playOnAwake = false;
            vAudio.spatialBlend = 0f;
            vAudio.volume = 1f;
            try
            {
                if (PrefabSingleton<AudioManager>.HasInstance)
                {
                    AudioManager mgr = PrefabSingleton<AudioManager>.Instance;
                    if (mgr != null && mgr.MixerGroups != null && mgr.MixerGroups.Length > 0)
                    {
                        vAudio.outputAudioMixerGroup = mgr.MixerGroups[0];
                    }
                }
            }
            catch (Exception)
            {
            }
            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.controlledAudioTrackCount = 1;
            vp.EnableAudioTrack(0, true);
            vp.SetTargetAudioSource(0, vAudio);

            bool failed = false;
            vp.errorReceived += delegate(VideoPlayer source, string message)
            {
                failed = true;
                CustomBattleBgPlugin.LogError("视频播放出错（" + filePath + "）：" + message);
            };

            // 第一步：拿一个探测用的小 RT 先 Prepare，只为读出视频的原始宽高
            //（VideoPlayer.width/height 要准备好之后才有值）。
            RenderTexture probe = null;
            try
            {
                probe = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32);
                probe.Create();
                vp.targetTexture = probe;
                vp.Prepare();
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("视频准备失败：" + e.Message);
                yield break;
            }

            float timeout = 10f;
            while (!vp.isPrepared && !failed && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
                if (_video != vp || vp == null)
                {
                    yield break;
                }
            }
            if (vp == null || !vp.isPrepared)
            {
                CustomBattleBgPlugin.LogError("视频没有准备好，跳过切换：" + filePath);
                yield break;
            }

            int vw = Mathf.Max((int)vp.width, 16);
            int vh = Mathf.Max((int)vp.height, 16);
            // 4K 视频没必要按原始分辨率解，最长边压到 1920 以内
            float scale = Mathf.Min(1f, 1920f / Mathf.Max(vw, vh));
            vw = Mathf.Max(Mathf.RoundToInt(vw * scale), 16);
            vh = Mathf.Max(Mathf.RoundToInt(vh * scale), 16);

            if (probe != null)
            {
                vp.targetTexture = null;
                probe.Release();
                UnityEngine.Object.Destroy(probe);
            }

            // 第二步：按视频自己的比例建 RT —— 视频完整渲染进去，不做任何裁切；
            // 显示时"全屏模式"再按 cover 裁切铺满，"只占上半屏"模式则整幅缩进黑框里。
            try
            {
                _renderTex = new RenderTexture(vw, vh, 0, RenderTextureFormat.ARGB32);
                _renderTex.Create();
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("创建视频 RenderTexture 失败：" + e.Message);
                yield break;
            }

            vp.targetTexture = _renderTex;
            vp.aspectRatio = VideoAspectRatio.Stretch;

            try
            {
                vp.Prepare();
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("视频准备失败：" + e.Message);
                yield break;
            }

            // 等第二次准备好（最多 10 秒），期间被停止/销毁就直接退出
            timeout = 10f;
            while (!vp.isPrepared && !failed && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
                if (_video != vp || vp == null)
                {
                    yield break;
                }
            }
            if (vp == null || !vp.isPrepared)
            {
                CustomBattleBgPlugin.LogError("视频没有准备好（第二次），跳过切换：" + filePath);
                yield break;
            }

            if (_visual != null)
            {
                _visual.texture = _renderTex;
                _hasBaseUv = false;   // 视频不参与摇晃
            }
            LayoutVideo();
            vp.Play();
            StartReveal();
            _videoCo = null;
        }

        /// <summary>
        /// 保持比例铺满（cover），并整体多放大一点（overscan）给"轻微摇晃"留左右余量。
        /// 返回的是"纹理坐标区域"：把这个区域映射到屏幕，就等于 cover 铺满。
        /// </summary>
        private Rect BuildCoverUv(int texWidth, int texHeight, bool overscan)
        {
            if (texWidth <= 0 || texHeight <= 0 || _canvasW <= 0f || _canvasH <= 0f)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }
            float screenAspect = _canvasW / _canvasH;
            float texAspect = texWidth / (float)texHeight;
            float zoom = overscan ? Mathf.Max(DomainConstants.BgSwayOverscan, 1f) : 1f;
            // 宽的维度占满 1，窄的维度按比例取；再整体除以 zoom 做 overscan（两个方向都有余量）
            float w = texAspect > screenAspect ? screenAspect / texAspect : 1f;
            float h = texAspect > screenAspect ? 1f : texAspect / screenAspect;
            w /= zoom;
            h /= zoom;
            return new Rect((1f - w) * 0.5f, (1f - h) * 0.5f, w, h);
        }

        /// <summary>
        /// 图片：始终按整屏尺寸摆（cover 铺满，多出来的由 Mask 裁掉）。
        /// "只占上半屏"模式下显示的就是图片的上半部分。
        /// </summary>
        private void LayoutImage()
        {
            if (_visualRt == null)
            {
                return;
            }
            _visualRt.anchorMin = new Vector2(1f, 1f);
            _visualRt.anchorMax = new Vector2(1f, 1f);
            _visualRt.pivot = new Vector2(1f, 1f);
            _visualRt.anchoredPosition = Vector2.zero;
            _visualRt.sizeDelta = new Vector2(_canvasW, _canvasH);
        }

        /// <summary>
        /// 视频的摆放：
        /// · 全屏模式 = 按整屏铺满（cover，超出的裁掉）；
        /// · "只占上半屏"模式 = **整幅视频按比例缩小**放进上方区域，四周留黑边（黑底由 BlackBg 提供）。
        /// </summary>
        private void LayoutVideo()
        {
            if (_visualRt == null || _visual == null || _renderTex == null)
            {
                return;
            }
            float rtAspect = _renderTex.width / (float)_renderTex.height;
            if (!_halfScreen)
            {
                _visualRt.anchorMin = new Vector2(1f, 1f);
                _visualRt.anchorMax = new Vector2(1f, 1f);
                _visualRt.pivot = new Vector2(1f, 1f);
                _visualRt.anchoredPosition = Vector2.zero;
                _visualRt.sizeDelta = new Vector2(_canvasW, _canvasH);
                _visual.uvRect = BuildCoverUv(_renderTex.width, _renderTex.height, false);
                return;
            }

            // 上半屏模式：contain（完整显示）——先在"区域高度扣掉上下黑边"的可用空间里按比例放
            float margin = Mathf.Clamp(CustomBattleBgPlugin.VideoHalfEdge, 0f, 0.45f);
            float availH = Mathf.Max(_maskH * (1f - 2f * margin), 1f);
            float w = availH * rtAspect;
            float h = availH;
            if (w > _canvasW)
            {
                w = _canvasW;
                h = w / rtAspect;
            }
            _visualRt.anchorMin = new Vector2(0.5f, 0.5f);
            _visualRt.anchorMax = new Vector2(0.5f, 0.5f);
            _visualRt.pivot = new Vector2(0.5f, 0.5f);
            _visualRt.anchoredPosition = Vector2.zero;
            _visualRt.sizeDelta = new Vector2(w, h);
            _visual.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        /// <summary>只有图片才做"左右缓慢轻微摇晃"——摆的是纹理坐标，不动物体，不吃性能。</summary>
        private void UpdateSway()
        {
            if (_visual == null || !_hasBaseUv || _isVideoContent)
            {
                return;
            }
            float period = Mathf.Max(CustomBattleBgPlugin.SwayPeriod, 1f);
            float amp = CustomBattleBgPlugin.SwayAmplitude;
            // 留出来的横向余量最多能摆多少（两边各一半）
            float maxAmp = (1f - 1f / Mathf.Max(DomainConstants.BgSwayOverscan, 1f)) * 0.5f;
            amp = Mathf.Clamp(amp, 0f, maxAmp);
            if (amp <= 0f)
            {
                if (_visual.uvRect != _baseUv)
                {
                    _visual.uvRect = _baseUv;
                }
                return;
            }
            float offset = Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / period)) * amp;
            Rect uv = _baseUv;
            uv.x = Mathf.Clamp01(_baseUv.x + offset);
            _visual.uvRect = uv;
        }

        private void StartReveal()
        {
            if (_reveal != null)
            {
                StopCoroutine(_reveal);
            }
            _reveal = StartCoroutine(RevealCo());
        }

        private IEnumerator RevealCo()
        {
            float duration = Mathf.Max(DomainConstants.BgRevealDuration, 0.01f);
            float t = 0f;
            if (_maskRt != null)
            {
                _maskRt.sizeDelta = new Vector2(0f, _maskH);
            }
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                if (_maskRt != null)
                {
                    _maskRt.sizeDelta = new Vector2(_canvasW * k, _maskH);
                }
                yield return null;
            }
            if (_maskRt != null)
            {
                _maskRt.sizeDelta = new Vector2(_canvasW, _maskH);
            }
            _reveal = null;
        }

        private void ClearContent()
        {
            try
            {
                if (_video != null)
                {
                    try
                    {
                        _video.Stop();
                    }
                    catch (Exception)
                    {
                    }
                    if (_video != null && _video.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(_video.gameObject);
                    }
                    _video = null;
                }
                if (_visual != null)
                {
                    _visual.texture = null;
                }
                if (_renderTex != null)
                {
                    _renderTex.Release();
                    UnityEngine.Object.Destroy(_renderTex);
                    _renderTex = null;
                }
                if (_tex != null)
                {
                    UnityEngine.Object.Destroy(_tex);
                    _tex = null;
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("清理背景内容失败：" + e.Message);
            }
        }

        private void Update()
        {
            try
            {
                UpdateSway();
                // 相机没了（战斗结束）或者分辨率变了，就把画布尺寸跟上
                if (_root == null)
                {
                    return;
                }
                if (_cam == null)
                {
                    HideInternal();
                    return;
                }
                if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
                {
                    UpdateCanvasSize();
                    if (_tex != null && _visual != null)
                    {
                        _baseUv = BuildCoverUv(_tex.width, _tex.height, true);
                        _visual.uvRect = _baseUv;
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
