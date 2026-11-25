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
        private readonly Dictionary<BattleRole, double> _lastBurstStart = new Dictionary<BattleRole, double>();
        private readonly List<(AudioSource Source, double FreeAt)> _burstSources = new List<(AudioSource, double)>();
        private GameObject _burstNode;
        private readonly List<AudioClip> _owned = new List<AudioClip>();
        private readonly List<(AudioManager Manager, uint Handle, double Ends)> _playing =
            new List<(AudioManager, uint, double)>();

        private ValennaEventAudio(BattleFightContent fight, string resource)
        {
            _fight = fight;
            _subtitles = StrikeAudio.ReadSubtitleDefaults(resource);
        }

        internal static Task Prepare()
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (fight == null || !BattleHelper.IsInBattle || fight.IsRestartBattleBreak || fight.IsGiveUpBattleBreak)
                return Task.CompletedTask;
            if (ReferenceEquals(_current?._fight, fight)) return _current._files[BurstFile];
            Clear();
            string root = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ValennaPlugin.Instance.Info.Location)), "resource");
            var bank = new ValennaEventAudio(fight, root);
            _current = bank;
            foreach (string file in new[] { BurstFile }.Concat(Success).Concat(Failure))
                bank._files[file] = bank.Load(Path.Combine(root, "sound", file));
            return bank._files[BurstFile];
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
            _ = Prepare();
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

        internal static void PlayBurst(BattleRole target)
        {
            if (target == null) return;
            _ = Prepare();
            if (_current != null) _ = _current.PlayBurstSound(target);
        }

        private async Task PlayBurstSound(BattleRole target)
        {
            try
            {
                AudioClip clip = await _files[BurstFile];
                if (!IsActive() || clip == null || !PrefabSingleton<AudioManager>.HasInstance) return;
                AudioManager manager = PrefabSingleton<AudioManager>.Instance;
                if (manager.AudioNode == null) return;
                if (_burstNode == null)
                {
                    _burstNode = new GameObject("ValennaTremorAudio");
                    _burstNode.transform.SetParent(manager.AudioNode, false);
                    _burstSources.Clear();
                }
                double now = AudioSettings.dspTime;
                double start = _lastBurstStart.TryGetValue(target, out double last)
                    ? Math.Clamp(last + 0.020, now, now + 0.080) : now;
                _lastBurstStart[target] = start;
                int index = _burstSources.FindIndex(voice => voice.FreeAt <= now && !voice.Source.isPlaying);
                AudioSource source = index < 0 ? _burstNode.AddComponent<AudioSource>() : _burstSources[index].Source;
                source.playOnAwake = false;
                source.outputAudioMixerGroup = manager.MixerGroups[1];
                source.volume = manager.SoundVolume;
                source.clip = clip;
                if (start <= now) source.Play();
                else source.PlayScheduled(start);
                var voice = (source, start + clip.length);
                if (index < 0) _burstSources.Add(voice);
                else _burstSources[index] = voice;
            }
            catch (Exception error) { ValennaPlugin.Instance.Warn($"震颤引爆音效播放失败。{error.Message}"); }
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
            if (bank._burstNode != null)
            {
                bank._burstNode.SetActive(false);
                UnityEngine.Object.Destroy(bank._burstNode);
            }
            foreach (var sound in bank._playing)
                if (sound.Manager != null) sound.Manager.StopSound(sound.Handle);
            foreach (var clip in bank._owned)
                if (clip != null) UnityEngine.Object.Destroy(clip);
        }
    }
}
