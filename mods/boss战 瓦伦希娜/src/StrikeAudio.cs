using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game;
using MOD;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace ValennaBattle
{
    internal sealed class StrikeAudio : IDisposable
    {
        [Serializable]
        private sealed class Config
        {
            public Cue[] cues = Array.Empty<Cue>();
        }

        [Serializable]
        private sealed class SubtitleTable
        {
            public SubtitleLine[] lines = Array.Empty<SubtitleLine>();
        }

        [Serializable]
        internal sealed class SubtitleLine
        {
            public string file = null;
            public string text = null;
            public string en = null;

            internal string ForLanguage(string custom = null) =>
                custom != null && custom != text ? custom :
                ValennaText.IsEnglish && !string.IsNullOrEmpty(en) ? en : text;
        }

        [Serializable]
        private sealed class Cue
        {
            public string file = "";
            public float at = -1f;
            public float volume = -1f;
            public float speed = 1f;
            public int group = 0;
            public string subtitle = null;
            [JsonIgnore] public AudioClip Clip;
        }

        private static readonly System.Random VariationRandom = new System.Random();
        private readonly List<Cue> _cues = new List<Cue>();
        private readonly HashSet<AudioClip> _clips = new HashSet<AudioClip>();
        private readonly List<(AudioManager Manager, uint Handle)> _playing = new List<(AudioManager, uint)>();
        private readonly List<IDisposable> _captions = new List<IDisposable>();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private BattleRole _speaker;
        private int _next;
        private bool _disposed;
        private double _tailEndsAt;

        internal async Task Load(int skillId, Func<bool> canAnimate, BattleRole speaker)
        {
            _speaker = speaker;
            try
            {
                string resource = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ValennaPlugin.Instance.Info.Location)), "resource");
                string configPath = Path.Combine(resource, "skill-audio", skillId + ".json");
                var info = new FileInfo(configPath);
                if (!info.Exists) return;
                if (info.Length > 256 * 1024) throw new InvalidDataException("音效配置过大");
                Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                if (config?.cues == null) return;

                string soundRoot = Path.Combine(resource, "sound");
                var subtitles = ReadSubtitleDefaults(resource);
                var candidates = new List<Cue>();
                foreach (Cue cue in config.cues.Take(64))
                {
                    if (cue == null || !(cue.at >= 0f && cue.at <= 60f) ||
                        !(cue.volume > 0f && cue.volume <= 100f)) continue;
                    string file = GetSoundPath(soundRoot, cue.file);
                    if (file == null) continue;
                    if (subtitles.TryGetValue(cue.file.Replace('\\', '/'), out var line))
                        cue.subtitle = line.ForLanguage(cue.subtitle);
                    cue.file = file;
                    if (!(cue.speed >= 0.5f && cue.speed <= 2f)) cue.speed = 1f;
                    if (cue.group < 0 || cue.group > 32) cue.group = 0;
                    cue.subtitle = cue.subtitle?.Trim();
                    if (cue.subtitle?.Length > 240)
                        cue.subtitle = cue.subtitle.Substring(0, char.IsHighSurrogate(cue.subtitle[239]) ? 239 : 240);
                    candidates.Add(cue);
                }

                const float loadTimeout = 1.5f;
                float deadline = Time.realtimeSinceStartup + loadTimeout;
                _cancel.CancelAfter(TimeSpan.FromSeconds(loadTimeout));
                var sources = new Dictionary<string, Task<AudioClip>>(StringComparer.OrdinalIgnoreCase);
                var variants = new Dictionary<(string, float), Task<AudioClip>>();
                var loading = new List<Task>();
                foreach (var group in candidates.GroupBy(cue => cue.group))
                {
                    if (group.Key == 0)
                        loading.AddRange(group.Select(Resolve));
                    else
                        loading.Add(PickPlayable(group.ToArray(), Resolve));
                }
                await Task.WhenAll(loading);
                if (_disposed || !canAnimate()) return;
                // OrderBy 保留同一时刻在编辑器音轨中的顺序。
                _cues.AddRange(candidates.Where(cue => cue.Clip != null).OrderBy(cue => cue.at));

                async Task Resolve(Cue cue)
                {
                    if (_disposed || !canAnimate()) return;
                    if (!sources.TryGetValue(cue.file, out Task<AudioClip> source))
                    {
                        source = LoadClip(cue.file, deadline, canAnimate);
                        sources.Add(cue.file, source);
                    }
                    if (cue.speed == 1f)
                    {
                        cue.Clip = await source;
                        return;
                    }
                    var key = (cue.file, cue.speed);
                    if (!variants.TryGetValue(key, out Task<AudioClip> variant))
                    {
                        variant = ChangeTempo(source, cue.speed, canAnimate);
                        variants.Add(key, variant);
                    }
                    cue.Clip = await variant;
                }
            }
            catch (Exception error)
            {
                ValennaPlugin.Instance.Warn($"瓦伦希娜音效配置读取失败 {skillId}。{error.Message}");
            }
        }

        internal static Dictionary<string, SubtitleLine> ReadSubtitleDefaults(string resource)
        {
            var result = new Dictionary<string, SubtitleLine>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var info = new FileInfo(Path.Combine(resource, "voice-subtitles.json"));
                if (!info.Exists) return result;
                if (info.Length > 64 * 1024) throw new InvalidDataException("默认字幕表过大");
                var table = JsonConvert.DeserializeObject<SubtitleTable>(File.ReadAllText(info.FullName));
                foreach (var line in table?.lines ?? Array.Empty<SubtitleLine>())
                {
                    if (!string.IsNullOrWhiteSpace(line?.file) && !string.IsNullOrWhiteSpace(line.text))
                        result[line.file.Replace('\\', '/')] = line;
                }
            }
            catch (Exception error)
            {
                ValennaPlugin.Instance.Warn($"瓦伦希娜默认字幕读取失败。{error.Message}");
            }
            return result;
        }

        private static async Task PickPlayable(Cue[] group, Func<Cue, Task> resolve)
        {
            // 随机排列后从头试播组内候选，有效候选的选择概率相同。
            for (int i = 0; i < group.Length; i++)
            {
                int selected = VariationRandom.Next(i, group.Length);
                (group[i], group[selected]) = (group[selected], group[i]);
            }
            foreach (Cue cue in group)
            {
                await resolve(cue);
                if (cue.Clip != null) return;
            }
        }

        private static string GetSoundPath(string root, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || Path.IsPathRooted(filename)) return null;
            string[] parts = filename.Split('/', '\\');
            if (parts.Any(part => part.Length == 0 || part == "." || part == ".." ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return null;
            if (!string.Equals(Path.GetExtension(filename), ".ogg", StringComparison.OrdinalIgnoreCase)) return null;
            string current = root;
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
                foreach (string part in parts)
                {
                    current = Path.Combine(current, part);
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
                }
                return File.Exists(current) ? current : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private async Task<AudioClip> LoadClip(string filename, float deadline, Func<bool> canAnimate)
        {
            AudioClip clip = await ReadClip(filename, deadline, () => !_disposed && canAnimate());
            if (clip != null)
            {
                if (_disposed) { UnityEngine.Object.Destroy(clip); return null; }
                _clips.Add(clip);
            }
            return clip;
        }

        internal static async Task<AudioClip> ReadClip(string filename, float deadline, Func<bool> canLoad)
        {
            try
            {
                if (!canLoad() || Time.realtimeSinceStartup >= deadline) return null;
                using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(filename).AbsoluteUri, AudioType.OGGVORBIS);
                ((DownloadHandlerAudioClip)request.downloadHandler).compressed = false;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await new WaitForEndOfFrame();
                    if (!canLoad() || Time.realtimeSinceStartup >= deadline)
                    {
                        request.Abort();
                        return null;
                    }
                }
                if (!canLoad()) return null;
                if (request.result != UnityWebRequest.Result.Success)
                    throw new IOException(request.error);
                return DownloadHandlerAudioClip.GetContent(request);
            }
            catch (Exception error)
            {
                ValennaPlugin.Instance.Warn($"瓦伦希娜音效加载失败 {Path.GetFileName(filename)}。{error.Message}");
                return null;
            }
        }

        private async Task<AudioClip> ChangeTempo(Task<AudioClip> source, float speed, Func<bool> canAnimate)
        {
            AudioClip adjusted = null;
            try
            {
                AudioClip clip = await source;
                if (_disposed || !canAnimate() || clip == null) return null;
                int size = checked(clip.samples * clip.channels);
                if (size <= 0 || size > 4_000_000) throw new InvalidDataException("音频样本过长");
                int channels = clip.channels, frequency = clip.frequency;
                var pcm = new float[size];
                if (!clip.GetData(pcm, 0)) throw new IOException("无法读取音频采样");
                CancellationToken token = _cancel.Token;
                float[] changed = await Task.Run(() => VoiceTempo.Change(pcm, channels, frequency, speed, token), token);
                if (_disposed || !canAnimate()) return null;
                adjusted = AudioClip.Create(clip.name, changed.Length / channels, channels, frequency, false);
                if (adjusted == null || !adjusted.SetData(changed, 0)) throw new IOException("无法生成调速后的音频");
                _clips.Add(adjusted);
                return adjusted;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception error)
            {
                if (adjusted != null) UnityEngine.Object.Destroy(adjusted);
                ValennaPlugin.Instance.Warn($"瓦伦希娜音效变速失败。{error.Message}");
                return null;
            }
        }

        internal void Advance(float time)
        {
            while (!_disposed && _next < _cues.Count && _cues[_next].at <= time + 0.0001f)
            {
                Cue cue = _cues[_next++];
                AudioClip clip = cue.Clip;
                if (clip == null) continue;
                try
                {
                    if (!PrefabSingleton<AudioManager>.HasInstance) continue;
                    var manager = PrefabSingleton<AudioManager>.Instance;
                    uint handle = manager.PlaySound(clip, cue.volume / 100f, false);
                    if (handle == 0) continue;
                    _playing.Add((manager, handle));
                    // 给最后一个混音缓冲留出时间，再销毁解码后的 Clip。
                    _tailEndsAt = Math.Max(_tailEndsAt, AudioSettings.dspTime + clip.length + 0.05);
                    if (!string.IsNullOrEmpty(cue.subtitle))
                    {
                        IDisposable caption = StrikeVoiceCaption.Show(_speaker, cue.subtitle, clip.length);
                        if (caption != null) _captions.Add(caption);
                    }
                }
                catch (Exception error)
                {
                    ValennaPlugin.Instance.Warn($"瓦伦希娜音效播放失败。{error.Message}");
                }
            }
        }

        internal async Task FinishTail(Func<bool> canContinue)
        {
            try
            {
                while (!_disposed && canContinue() && AudioSettings.dspTime < _tailEndsAt)
                    await new WaitForEndOfFrame();
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cancel.Cancel();
            _cancel.Dispose();
            foreach (var sound in _playing)
            {
                try
                {
                    if (sound.Manager != null) sound.Manager.StopSound(sound.Handle);
                }
                catch (Exception error)
                {
                    ValennaPlugin.Instance.Warn($"瓦伦希娜音效停止失败。{error.Message}");
                }
            }
            foreach (IDisposable caption in _captions)
            {
                try { caption.Dispose(); }
                catch (Exception error)
                {
                    ValennaPlugin.Instance.Warn($"瓦伦希娜字幕清理失败。{error.Message}");
                }
            }
            foreach (var clip in _clips)
                if (clip != null) UnityEngine.Object.Destroy(clip);
            _playing.Clear();
            _clips.Clear();
            _captions.Clear();
        }
    }
}
