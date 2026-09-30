// 战斗 BGM 的切换 / 记录 / 复原。
//
// 规则（用户口径）：
//   · 一场战斗只记一次"原始 BGM"（战斗开始时记，先清空旧记录再记）；
//   · 切换 = 直接切（不做淡入淡出）；自定义 BGM 可选循环；
//   · 非循环的播完一遍后，自动切回"原有的战斗 BGM"；
//   · 战斗结束时，比较当前 BGM 与记录值，不同就以记录值复原。
//
// 实现上不走 AudioManager.SetBgm（它内部写死 loop=true），而是自己把 AudioClip
// 加载好后直接操作 AudioManager.BgmSource，这样循环/单次都能精确控制。

using System;
using System.Collections;
using System.Reflection;
using System.IO;
using UnityEngine;
using MOD;

namespace CustomBattleBg
{
    internal class BattleBgm : MonoBehaviour
    {
        private static BattleBgm _instance;

        // ---- 原始记录（一场战斗一份）----
        private bool _hasOrigin;
        private string _originKey = "";
        private EAudioReferenceType _originType = EAudioReferenceType.None;

        // ---- 当前的自定义 BGM ----
        private AudioClip _customClip;
        private bool _customLoop;
        private bool _customActive;     // 正在播我们切进来的 BGM
        private float _customStartTime; // 开始播放的时刻（给单次播放的检测留点宽限）
        private int _token;             // 每次切换 +1，旧回调/监控看到对不上就放弃
        private Coroutine _loadCo;

        internal static BattleBgm Instance
        {
            get { return _instance; }
        }

        private static BattleBgm Ensure()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("CustomBattleBgm");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<BattleBgm>();
            }
            return _instance;
        }

        /// <summary>战斗开始：清空旧记录，记下当前的战斗 BGM。</summary>
        internal static void RecordOrigin()
        {
            BattleBgm inst = Ensure();
            inst._hasOrigin = false;
            inst._originKey = "";
            inst._originType = EAudioReferenceType.None;
            inst._customActive = false;
            inst._token++;
            try
            {
                AudioManager mgr = GetManager();
                if (mgr == null)
                {
                    return;
                }
                if (mgr.CurrentBgm != null && !string.IsNullOrEmpty(mgr.CurrentBgm.Key))
                {
                    inst._originKey = mgr.CurrentBgm.Key;
                    inst._originType = (EAudioReferenceType)mgr.CurrentBgm.ReferenceType;
                }
                inst._hasOrigin = true;
                CustomBattleBgPlugin.LogInfo("已记录战斗 BGM：[" + inst._originType + "] " +
                    (string.IsNullOrEmpty(inst._originKey) ? "(无)" : inst._originKey));
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("记录原始 BGM 失败：" + e.Message);
            }
        }

        /// <summary>把 BGM 切成某个文件；loop=是否循环。</summary>
        internal static void SwitchTo(string filePath, bool loop)
        {
            BattleBgm inst = Ensure();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                CustomBattleBgPlugin.LogError("BGM 文件不存在，跳过切换：" + filePath);
                return;
            }
            inst._token++;
            int token = inst._token;
            inst._customLoop = loop;
            inst._customActive = false;

            if (inst._loadCo != null)
            {
                inst.StopCoroutine(inst._loadCo);
                inst._loadCo = null;
            }
            inst._loadCo = inst.StartCoroutine(inst.LoadAndPlay(filePath, loop, token));
        }

        /// <summary>
        /// 视频背景接管音乐通道：把游戏自带的战斗 BGM 关掉（用户口径：视频就像自定义 BGM 一样，
        /// 属于"音乐接管"；配了用户 BGM 时不会走到这里，因为用户 BGM 已经把 BgmSource 换掉了）。
        /// 打上 VideoMuteKey 标记，战斗结束时的"比较-复原"才能识别出 BGM 变过。
        /// </summary>
        internal static void MuteGameBgm(string reason)
        {
            try
            {
                AudioManager mgr = GetManager();
                if (mgr == null || mgr.BgmSource == null)
                {
                    return;
                }
                BattleBgm inst = Ensure();
                KillBgmTween(mgr);
                mgr.BgmSource.Stop();
                mgr.BgmSource.clip = null;
                mgr.CurrentBgm = new AudioResourceReference
                {
                    ReferenceType = EAudioReferenceType.Bgm,
                    Key = DomainConstants.VideoMuteKey
                };
                inst._customActive = false;
                CustomBattleBgPlugin.LogInfo("（" + reason + "）视频背景接管：已关闭游戏自带的战斗 BGM");
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("关闭游戏自带 BGM 失败：" + e.Message);
            }
        }

        private IEnumerator LoadAndPlay(string filePath, bool loop, int token)
        {
            AudioClip clip = null;
            yield return MediaLoader.LoadAudioClip(filePath, delegate(AudioClip c) { clip = c; });
            _loadCo = null;
            if (token != _token)
            {
                // 期间又切换过一次，这次的结果作废
                if (clip != null) { UnityEngine.Object.Destroy(clip); }
                yield break;
            }
            if (clip == null)
            {
                CustomBattleBgPlugin.LogError("BGM 加载失败，跳过切换：" + filePath);
                yield break;
            }

            try
            {
                AudioManager mgr = GetManager();
                if (mgr == null || mgr.BgmSource == null)
                {
                    CustomBattleBgPlugin.LogError("AudioManager 不可用，跳过切换");
                    UnityEngine.Object.Destroy(clip);
                    yield break;
                }

                // 换掉上一首自定义 BGM 的 clip（迟一点再销毁，免得正在播的被拆掉）
                AudioClip old = _customClip;
                _customClip = clip;

                // 游戏切战斗 BGM 时用 SetBgmFadeInOut 做了 1.5 秒的淡入，
                // 那个 tween 结束时会把 BgmSource 整个停掉重设——必须先把 tween 杀掉，
                // 否则它会把我们刚切进去的 BGM 顶掉（甚至 Stop 掉）。
                KillBgmTween(mgr);

                if (mgr.MixerGroups != null && mgr.MixerGroups.Length > 0)
                {
                    mgr.BgmSource.outputAudioMixerGroup = mgr.MixerGroups[0];
                }
                mgr.BgmSource.Stop();
                mgr.BgmSource.clip = clip;
                mgr.BgmSource.loop = loop;
                // 音量固定用 1（和游戏 SetBgm 的 data.Volume 默认值一致）。
                // 不能抄 BgmSource.volume：进战斗时它正处在 1.5 秒淡入动画的中途，
                // 抄到的会是 0.x 的中间值，听感上"比游戏 BGM 小一大截"（2026-09-30 用户实测）。
                mgr.BgmSource.volume = 1f;
                mgr.BgmSource.Play();
                mgr.CurrentBgm = new AudioResourceReference
                {
                    ReferenceType = EAudioReferenceType.Bgm,
                    Key = DomainConstants.CustomBgmKeyPrefix + Path.GetFileName(filePath)
                };
                _customActive = true;
                _customStartTime = Time.realtimeSinceStartup;

                CustomBattleBgPlugin.LogInfo("已切换战斗 BGM：" + Path.GetFileName(filePath) +
                    "（" + (loop ? "循环" : "单次") + "）");

                if (old != null)
                {
                    UnityEngine.Object.Destroy(old, 5f);
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("切换 BGM 失败：" + e);
            }
        }

        /// <summary>战斗结束：当前 BGM 与记录值不同才复原。</summary>
        internal static void RestoreOrigin()
        {
            BattleBgm inst = _instance;
            if (inst == null)
            {
                return;
            }
            try
            {
                inst._token++;
                if (inst._loadCo != null)
                {
                    inst.StopCoroutine(inst._loadCo);
                    inst._loadCo = null;
                }
                bool changed = inst.IsCurrentDifferentFromOrigin();
                if (!changed)
                {
                    inst.ClearCustom();
                    CustomBattleBgPlugin.LogInfo("战斗结束：BGM 没有变化，无需复原");
                    return;
                }
                if (!inst._hasOrigin || string.IsNullOrEmpty(inst._originKey))
                {
                    // 记不到原始 BGM（进战斗时本来就没有），交给游戏自己的流程
                    inst.ClearCustom();
                    CustomBattleBgPlugin.LogInfo("战斗结束：没有可复原的 BGM 记录，交给游戏处理");
                    return;
                }

                AudioManager mgr = GetManager();
                if (mgr == null)
                {
                    return;
                }
                AudioClipData data = new AudioClipData();
                data.AudioReference = new AudioResourceReference();
                data.AudioReference.ReferenceType = inst._originType;
                data.AudioReference.Key = inst._originKey;
                data.Volume = 1f;
                mgr.SetBgm(data);
                inst.ClearCustom();
                CustomBattleBgPlugin.LogInfo("战斗结束：BGM 已复原到 [" + inst._originType + "] " + inst._originKey);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("复原 BGM 失败：" + e.Message);
            }
        }

        /// <summary>战斗完全结束后清掉记录，等待下一场。</summary>
        internal static void ClearRecord()
        {
            BattleBgm inst = _instance;
            if (inst == null)
            {
                return;
            }
            inst._hasOrigin = false;
            inst._originKey = "";
            inst._originType = EAudioReferenceType.None;
            inst._customActive = false;
            inst._customLoop = false;
            inst._token++;
        }

        private bool IsCurrentDifferentFromOrigin()
        {
            try
            {
                AudioManager mgr = GetManager();
                if (mgr == null)
                {
                    return false;
                }
                string key = mgr.CurrentBgm != null ? mgr.CurrentBgm.Key : "";
                if (string.IsNullOrEmpty(key))
                {
                    return !string.IsNullOrEmpty(_originKey);
                }
                return key != _originKey;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ClearCustom()
        {
            _customActive = false;
                if (_customClip != null)
                {
                    try
                    {
                        AudioManager mgr = GetManager();
                    if (mgr != null && mgr.BgmSource != null && mgr.BgmSource.clip == _customClip)
                    {
                        // 还在播它就留着（等待游戏的下一首接管），只清引用
                        _customClip = null;
                        return;
                    }
                }
                catch (Exception)
                {
                }
                // 延迟销毁：可能还在播，留几秒给切换过渡
                UnityEngine.Object.Destroy(_customClip, 5f);
                _customClip = null;
            }
        }

        private void Update()
        {
            try
            {
                if (!_customActive || _customLoop)
                {
                    return;
                }
                if (Time.realtimeSinceStartup - _customStartTime < 0.5f)
                {
                    return;   // 刚播上，给 AudioSource 一点时间
                }
                AudioManager mgr = GetManager();
                if (mgr == null || mgr.BgmSource == null)
                {
                    return;
                }
                // 单次播完 → 回到原有的战斗 BGM
                if (!mgr.BgmSource.isPlaying)
                {
                    CustomBattleBgPlugin.LogInfo("自定义 BGM 播放结束，切回原有的战斗 BGM");
                    _customActive = false;
                    RestoreOrigin();
                }
            }
            catch (Exception)
            {
            }
        }

        private static AudioManager GetManager()
        {
            try
            {
                if (!PrefabSingleton<AudioManager>.HasInstance)
                {
                    return null;
                }
                return PrefabSingleton<AudioManager>.Instance;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>反射杀掉 AudioManager 私有的 BGM 音量 tween（字段名改了就安静跳过）。</summary>
        private static void KillBgmTween(AudioManager mgr)
        {
            try
            {
                FieldInfo field = typeof(AudioManager).GetField("_volumeTween",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null)
                {
                    return;
                }
                object tween = field.GetValue(mgr);
                if (tween != null)
                {
                    DG.Tweening.TweenExtensions.Kill((DG.Tweening.Tween)tween, false);
                    field.SetValue(mgr, null);
                    CustomBattleBgPlugin.LogInfo("已结束游戏的 BGM 音量过渡动画");
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogInfo("结束 BGM 过渡动画失败（不影响播放）：" + e.Message);
            }
        }
    }
}
