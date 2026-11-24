// 私货武器特效分发，以及吸血、回血和原版音效。
//
// 以下路径供原版效果或贴图载入失败时使用：
//   · 吸血回血 → Effect/Prefabs/FX_CommonRecover   + Sound/Audio/FX/Battle/common_recover
//   · 突刺命中 → Effect/Prefabs/FXSkillHit_Puncture + Sound/Audio/FX/weapon_knife
//   · 斩击命中 → Effect/Prefabs/FX_BladeHit         + Sound/Audio/FX/Battle/blade_hit
//
// 回血和吸取沿用 EffectShowData。正常的武器粒子由 BattleBladeFx 播放。
//
// 2026-09-27 排查"特效/音效都没出现"，补齐了两个**新版引用字段**（游戏运行时真正读的是它们）：
//   · PlayRoleEffectData.FxPathReference（Key = 资源库里的文件名，如 FX_BladeHit）
//     —— 运行时优先走它；`FxPath` 只是旧版路径、留给编辑器查看，仍照原版数据填上做兼容。
//   · AudioClipData.AudioReference（ReferenceType=Sound + Key = 音效文件名，如 blade_hit）
//     —— AudioManager.PlaySound 只读 AudioReference；只填旧的 AudioRes 会**静默无声**。
// 两组 Key 都能在游戏自带注册表 InternalConfigure.txt 里查到（EffectResUnitDatas / SoundResUnitDatas）。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using HarmonyLib;
using MOD;
using UnityEngine;

namespace AttackTargetVisualizer
{
    internal static class SecretFx
    {
        internal const string FxRecover  = "Effect/Prefabs/FX_CommonRecover";
        internal const string FxHit      = "Effect/Prefabs/FX_CommonHit";

        internal const string SndRecover = "Sound/Audio/FX/Battle/common_recover";

        // 资源库（InternalConfigure.txt）里登记的文件名 —— 新版引用字段要用它，不是路径。
        private const string KeyRecover  = "FX_CommonRecover";
        private const string KeyPuncture = "FXSkillHit_Puncture";
        internal const string KeySlash   = "FX_BladeHit";
        private const string KeyHit      = "FX_CommonHit";

        private const string SndKeyRecover = "common_recover";
        private const string SndKeyKnife   = "weapon_knife";
        internal const string ZangHuaSlashEffect = "ZangHuaSlash";
        private const int SweepSkillId = 5;
        private const int PunctureIntervalMs = 100;
        private static readonly System.Random VisualRandom = new System.Random();

        // 每种资源只做一次"到底能不能加载"的自检，免得每次命中都同步读一遍资源。
        private static readonly HashSet<string> Probed = new HashSet<string>();

        /// <summary>回血（吸血时自己身上那一下）。</summary>
        internal static void Recover(BattleRole self)
        {
            Play(KeyRecover, FxRecover, SndKeyRecover, SndRecover, self, self, 1f);
        }

        /// <summary>先发或反击只在目标身上播放一次突刺。</summary>
        internal static void Puncture(BattleRole target, BattleRole source)
        {
            _ = PlayPuncture(target, source, 0f);
        }

        /// <summary>主动攻击的三次突刺与伤害计时各自运行。</summary>
        internal static async void PunctureBurst(BattleRole target, BattleRole source)
        {
            try
            {
                BattleFightContent fight = BattleHelper.FightContent;
                float direction = VisualRandom.Next(2) == 0 ? 1f : -1f;
                Puncture(target, source);
                for (int i = 0; i < 2; i++)
                {
                    await Task.Delay(PunctureIntervalMs);
                    if (fight == null || !BattleHelper.IsInBattle || BattleHelper.FightContent != fight)
                    {
                        return;
                    }
                    float offset = direction * (0.12f + (float)VisualRandom.NextDouble() * 0.12f);
                    _ = PlayPuncture(target, source, offset);
                    direction = -direction;
                }
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("荆棘：三段突刺特效出错：" + e.Message);
            }
        }

        private static Task PlayPuncture(BattleRole target, BattleRole source, float offset)
        {
            try
            {
                if (BattleBladeFx.TryPuncture(target, source, offset))
                {
                    if (PrefabSingleton<AudioManager>.HasInstance)
                    {
                        PrefabSingleton<AudioManager>.Instance.PlaySound(SndKeyKnife);
                    }
                    return Task.CompletedTask;
                }
            }
            catch (Exception error)
            {
                AttackTargetPlugin.LogError("荆棘剑气播放失败：" + error.Message);
            }
            Transform center = target?.Model != null ? target.Model.Trans_Center : null;
            return PlayPositioned(KeyPuncture, SndKeyKnife, center,
                Vector3.up * offset, Vector3.one, "突刺");
        }

        internal static bool PlayZangHuaSlash(BattleRole source, List<BattleRole> targets)
        {
            BattleActiveBehaviorData action = source?.CurrentBehaviourData;
            bool sweep = action?.BattleSkillData?.Id == SweepSkillId && action.IsRunning
                && BattleHelper.FightContent?.CurActionRole == source;

            try
            {
                if (BattleBladeFx.TrySlash(source, targets, sweep)) return true;
            }
            catch (Exception error)
            {
                AttackTargetPlugin.LogError("葬花剑气播放失败：" + error.Message);
            }

            if (!sweep)
            {
                bool hasTarget = false;
                foreach (BattleRole target in targets)
                {
                    Transform center = target?.Model != null ? target.Model.Trans_Center : null;
                    if (center == null) continue;
                    _ = PlayPositioned(KeySlash, null, center, Vector3.zero,
                        new Vector3(0.8f, 0.8f, 1f), "单体斩击");
                    hasTarget = true;
                }
                return hasTarget;
            }

            Transform scene = BattleHelper.FightContent.Room?.RoomEntity?.SceneTipRoot;
            if (scene == null) return false;

            Vector3 sum = Vector3.zero;
            float left = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            int count = 0;
            foreach (BattleRole target in targets)
            {
                if (target?.Model == null || target.Model.Trans_Center == null) continue;
                Vector3 pos = scene.InverseTransformPoint(target.Model.CenterPos);
                sum += pos;
                left = Mathf.Min(left, pos.x);
                right = Mathf.Max(right, pos.x);
                count++;
            }
            if (count == 0) return false;

            Vector3 centerPoint = sum / count;
            centerPoint.x = (left + right) * 0.5f;
            float width = Mathf.Clamp(1.7f + right - left, 1.7f, 4f);
            _ = PlayPositioned(KeySlash, null, scene, centerPoint,
                new Vector3(width, 0.55f, 1f), "横扫斩击");
            return true;
        }

        private static async Task PlayPositioned(string key, string sound, Transform parent,
            Vector3 position, Vector3 scale, string label)
        {
            BattleFightContent fight = BattleHelper.FightContent;
            if (parent == null || fight == null || !BattleHelper.IsInBattle) return;

            GameObject instance = null;
            try
            {
                var reference = new PrefabResoureReference
                {
                    ReferenceType = EPrefabReferenceType.Effect,
                    Key = key
                };
                await reference.GetResLoad(true, loaded => instance = loaded);
                if (parent == null || !BattleHelper.IsInBattle || BattleHelper.FightContent != fight) return;
                if (instance == null)
                {
                    AttackTargetPlugin.LogError("武器特效资源无法加载：" + key);
                    return;
                }
                instance.transform.RestTransform(parent);
                instance.transform.localPosition = position;
                instance.transform.localScale = scale;
                if (sound != null)
                {
                    PrefabSingleton<AudioManager>.Instance.PlaySound(sound);
                }
                AttackTargetPlugin.LogInfo("武器特效：" + label + "，位置 " + position + "，缩放 " + scale);
                await Task.Delay(650);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("武器特效：" + label + "播放出错：" + e.Message);
            }
            finally
            {
                if (instance != null)
                {
                    GameHelper.Recycle(instance.transform, isclear: true);
                }
            }
        }

        /// <summary>被吸取那一下（只在敌人身上闪一下，不配音）。</summary>
        internal static void Drain(BattleRole self, BattleRole target)
        {
            Play(KeyHit, FxHit, null, null, self, target, 0.8f);
        }

        /// <summary>
        /// 在 target 身上播一次性特效 + 音效；self 是"发起者"（决定演出归属）。
        /// fxKey / sndKey 是资源库文件名（新版字段），fxPath / sndPath 是旧版路径（兼容 + 编辑器查看）。
        /// </summary>
        private static async void Play(string fxKey, string fxPath, string sndKey, string sndPath,
            BattleRole self, BattleRole target, float duration)
        {
            try
            {
                if (self == null && target == null)
                {
                    return;
                }
                BattleRole caster = self != null ? self : target;
                EffectShowData show = new EffectShowData();
                show.Duration = duration;
                if (!string.IsNullOrEmpty(fxPath))
                {
                    PlayRoleEffectData fx = new PlayRoleEffectData();
                    fx.IsSelf = self != null && target == self;   // 点在"自己"身上还是目标身上
                    fx.FxPath = fxPath;                           // 旧字段：运行时只在 FxPathReference 为空时兜底
                    fx.FxPathReference = new PrefabResoureReference
                    {
                        ReferenceType = EPrefabReferenceType.Effect,
                        Key = fxKey                               // 新字段：运行时优先读它
                    };
                    fx.Duration = duration;
                    fx.PointType = ERolePointType.Center;
                    fx.UseSameFx = true;
                    show.EffectShows.Add(fx);
                }
                if (!string.IsNullOrEmpty(sndPath))
                {
                    PlaySoundData snd = new PlaySoundData();
                    snd.AudioClip = new AudioClipData();
                    snd.AudioClip.AudioRes = sndPath;             // 旧字段（类里已标"仅作查看"）
                    snd.AudioClip.AudioReference = new AudioResourceReference
                    {
                        ReferenceType = EAudioReferenceType.Sound,
                        Key = sndKey                              // 新字段：AudioManager 实际读它
                    };
                    snd.AudioClip.Volume = 1f;
                    show.EffectShows.Add(snd);
                }
                List<BattleRole> targets = new List<BattleRole>();
                if (target != null)
                {
                    targets.Add(target);
                }
                AttackTargetPlugin.LogInfo("私货特效：" + FxLabel(fxKey) + " → 「" +
                    SecretTraits.NameOf(target != null ? target : caster) + "」");
                Probe(fxPath);
                await show.Play(caster, targets);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特效：播放出错：" + e.Message);
            }
        }

        private static string FxLabel(string fxKey)
        {
            if (fxKey == KeySlash)    return "斩击";
            if (fxKey == KeyPuncture) return "突刺";
            if (fxKey == KeyRecover)  return "回血";
            if (fxKey == KeyHit)      return "吸取";
            return fxKey ?? "未知";
        }

        /// <summary>
        /// 诊断：首次播放时自检一次资源是否真的能从 Resources 里读出来。
        /// 只为排查"特效看不见"，稳定后可以删（不影响任何游戏行为）。
        /// </summary>
        private static void Probe(string fxPath)
        {
            if (string.IsNullOrEmpty(fxPath) || Probed.Contains(fxPath))
            {
                return;
            }
            Probed.Add(fxPath);
            try
            {
                GameObject go = Resources.Load<GameObject>(fxPath);
                AttackTargetPlugin.LogInfo("私货特效：资源自检 " + fxPath + " → " +
                    (go != null ? "可加载（" + go.name + "）" : "**读不到（Resources 里没有）**"));
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特效：资源自检出错：" + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(PlayRoleEffectData), nameof(PlayRoleEffectData.Play))]
    internal static class Patch_ZangHua_SlashEffect
    {
        private static bool Prefix(PlayRoleEffectData __instance, BattleRole self, List<BattleRole> targets)
        {
            if (__instance.UseFrameEffect || __instance.EffectName != SecretFx.ZangHuaSlashEffect
                || __instance.FxPathReference?.Key != SecretFx.KeySlash || !BattleHelper.IsInBattle
                || BattleHelper.FightContent == null || targets == null || targets.Count == 0)
            {
                return true;
            }

            // 使用预制体时原版不读取 EffectName，葬花用它标记自己的那条表现。
            try
            {
                return !SecretFx.PlayZangHuaSlash(self, targets);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("葬花：斩击特效出错：" + e.Message);
                return true;
            }
        }
    }
}
