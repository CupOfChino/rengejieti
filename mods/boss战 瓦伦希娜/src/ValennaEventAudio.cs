using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Game;
using MOD;
using UnityEngine;

namespace ValennaBattle
{
    internal sealed class ValennaEventAudio
    {
        private const string BurstFile = "震颤引爆.ogg";
        private static readonly string[] Success = {
            "技能音频/使用 闪避技能 成功闪避时.ogg",
            "技能音频/使用 闪避技能 成功闪避时 若对方为维吉里乌斯 1.ogg"
        };
        private static readonly string[] Failure = {
            "技能音频/闪避技能 失败.ogg", "技能音频/闪避技能 失败（2）.ogg",
            "技能音频/闪避技能 失败（3）.ogg", "技能音频/闪避技能 失败（4）.ogg",
            "技能音频/闪避技能 失败（9SV-BAT8-11-2）.ogg"
        };
        private static readonly System.Random Random = new System.Random();
        private static ValennaEventAudio _current;
        private readonly BattleFightContent _fight;
        private readonly Dictionary<string, StrikeAudio.SubtitleLine> _subtitles;
        private readonly Dictionary<string, Task<AudioClip>> _files = new Dictionary<string, Task<AudioClip>>();
        private readonly Dictionary<int, AudioClip> _bursts = new Dictionary<int, AudioClip>();
        private readonly List<AudioClip> _owned = new List<AudioClip>();
        private readonly List<(AudioManager Manager, uint Handle, double Ends)> _playing =
            new List<(AudioManager, uint, double)>();

        private ValennaEventAudio(BattleFightContent fight, string resource)
        {
            _fight = fight;
            _subtitles = StrikeAudio.ReadSubtitleDefaults(resource);
        }

        internal static void Prepare()
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (fight == null || !BattleHelper.IsInBattle || fight.IsRestartBattleBreak || fight.IsGiveUpBattleBreak) return;
            if (ReferenceEquals(_current?._fight, fight)) return;
            Clear();
            string root = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ValennaPlugin.Instance.Info.Location)), "resource");
            var bank = new ValennaEventAudio(fight, root);
            _current = bank;
            foreach (string file in Success.Concat(Failure).Append(BurstFile))
                bank._files[file] = bank.Load(Path.Combine(root, "sound", file));
        }

        private bool IsActive() => ReferenceEquals(_current, this) && BattleHelper.IsInBattle &&
            ReferenceEquals(BattleHelper.FightContent, _fight) && !_fight.IsRestartBattleBreak && !_fight.IsGiveUpBattleBreak;

        private async Task<AudioClip> Load(string path)
        {
            AudioClip clip = await StrikeAudio.ReadClip(path, Time.realtimeSinceStartup + 5f, IsActive);
            if (clip == null) return null;
            if (!IsActive()) { UnityEngine.Object.Destroy(clip); return null; }
            _owned.Add(clip);
            return clip;
        }

        internal static void PlayDodge(BattleRole role, bool evaded)
        {
            Prepare();
            if (_current != null) _ = _current.PlayVoice(role, evaded ? Success : Failure);
        }

        private async Task PlayVoice(BattleRole role, string[] pool)
        {
            try
            {
                await Task.WhenAll(pool.Select(file => _files[file]));
                if (!IsActive() || role?.Model == null) return;
                string[] playable = pool.Where(file => _files[file].Result != null && _subtitles.ContainsKey(file)).ToArray();
                if (playable.Length == 0) return;
                string chosen = playable[Random.Next(playable.Length)];
                AudioClip clip = _files[chosen].Result;
                if (Play(clip)) StrikeVoiceCaption.Show(role, _subtitles[chosen].ForLanguage(), clip.length);
            }
            catch (Exception error) { ValennaPlugin.Instance.Warn($"预知眼语音播放失败。{error.Message}"); }
        }

        internal static void PlayBursts(BattleRole target, int count)
        {
            if (target == null || count <= 0) return;
            Prepare();
            if (_current != null) _ = _current.PlayBurstBatch(count);
        }

        private async Task PlayBurstBatch(int count)
        {
            try
            {
                AudioClip original = await _files[BurstFile];
                if (!IsActive() || original == null) return;
                if (count == 1) { Play(original); return; }
                if (!_bursts.TryGetValue(count, out AudioClip batch))
                {
                    var source = new float[checked(original.samples * original.channels)];
                    if (!original.GetData(source, 0)) throw new IOException("无法读取引爆音效采样");
                    float[] mix = MixBursts(source, original.channels, original.frequency, count);
                    batch = AudioClip.Create("震颤引爆 ×" + count, mix.Length / original.channels, original.channels, original.frequency, false);
                    if (batch == null) throw new IOException("无法创建引爆音效");
                    _owned.Add(batch);
                    if (!batch.SetData(mix, 0)) throw new IOException("无法写入引爆音效采样");
                    _bursts.Add(count, batch);
                }
                Play(batch);
            }
            catch (Exception error) { ValennaPlugin.Instance.Warn($"震颤引爆音效播放失败。{error.Message}"); }
        }

        // 在同一条 PCM 缓冲里安排每次起音，低帧率下仍保留各声部和完整尾音。
        internal static float[] MixBursts(float[] source, int channels, int rate, int count)
        {
            int span = (int)Math.Round(rate * Math.Min(0.080, 0.020 * (count - 1)));
            var mix = new float[checked(source.Length + span * channels)];
            for (int i = 0; i < count; i++)
            {
                int start = count == 1 ? 0 : (int)Math.Round((double)i * span / (count - 1)) * channels;
                for (int sample = 0; sample < source.Length; sample++) mix[start + sample] += source[sample];
            }
            float peak = 0;
            foreach (float sample in mix) peak = Math.Max(peak, Math.Abs(sample));
            if (peak > 0.98f)
                for (int i = 0; i < mix.Length; i++) mix[i] *= 0.98f / peak;
            return mix;
        }

        private bool Play(AudioClip clip)
        {
            if (!PrefabSingleton<AudioManager>.HasInstance) return false;
            var manager = PrefabSingleton<AudioManager>.Instance;
            uint handle = manager.PlaySound(clip, 1f, false);
            if (handle == 0) return false;
            _playing.Add((manager, handle, AudioSettings.dspTime + clip.length + .05));
            return true;
        }

        internal static void Update()
        {
            var bank = _current;
            if (bank == null) return;
            if (!bank.IsActive()) { Clear(); return; }
            double now = AudioSettings.dspTime;
            for (int i = bank._playing.Count - 1; i >= 0; i--)
                if (bank._playing[i].Ends <= now) bank._playing.RemoveAt(i);
        }

        internal static void Clear()
        {
            var bank = _current;
            if (bank == null) return;
            _current = null;
            foreach (var sound in bank._playing)
                if (sound.Manager != null) sound.Manager.StopSound(sound.Handle);
            foreach (var clip in bank._owned)
                if (clip != null) UnityEngine.Object.Destroy(clip);
        }
    }
}
