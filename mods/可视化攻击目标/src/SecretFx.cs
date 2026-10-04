// 私货内容用的原版特效 / 音效（一次性播放）。
//
// 全是游戏自带资源，一个自定义素材都没有：
//   · 吸血回血 → Effect/Prefabs/FX_CommonRecover   + Sound/Audio/FX/Battle/common_recover
//   · 突刺命中 → Effect/Prefabs/FXSkillHit_Puncture + Sound/Audio/FX/weapon_knife
//   · 斩击命中 → Effect/Prefabs/FX_BladeHit         + Sound/Audio/FX/Battle/blade_hit
//
// 播放走的是游戏自己的 EffectShowData.Play —— 和武器数据里 Damage.EffectShow 完全同一条路
// （`PlayRoleEffectData` 播特效、`PlaySoundData` 播音效），所以表现跟原版武器是一致的。
//
// 2026-09-27 排查"特效/音效都没出现"，补齐了两个**新版引用字段**（游戏运行时真正读的是它们）：
//   · PlayRoleEffectData.FxPathReference（Key = 资源库里的文件名，如 FX_BladeHit）
//     —— 运行时优先走它；`FxPath` 只是旧版路径、留给编辑器查看，仍照原版数据填上做兼容。
//   · AudioClipData.AudioReference（ReferenceType=Sound + Key = 音效文件名，如 blade_hit）
//     —— AudioManager.PlaySound 只读 AudioReference；只填旧的 AudioRes 会**静默无声**。
// 两组 Key 都能在游戏自带注册表 InternalConfigure.txt 里查到（EffectResUnitDatas / SoundResUnitDatas）。

using System;
using System.Collections.Generic;
using Game;
using MOD;
using UnityEngine;

namespace AttackTargetVisualizer
{
    internal static class SecretFx
    {
        internal const string FxRecover  = "Effect/Prefabs/FX_CommonRecover";
        internal const string FxPuncture = "Effect/Prefabs/FXSkillHit_Puncture";
        internal const string FxSlash    = "Effect/Prefabs/FX_BladeHit";
        internal const string FxHit      = "Effect/Prefabs/FX_CommonHit";

        internal const string SndRecover = "Sound/Audio/FX/Battle/common_recover";
        internal const string SndBlade   = "Sound/Audio/FX/Battle/blade_hit";
        internal const string SndKnife   = "Sound/Audio/FX/weapon_knife";

        // 资源库（InternalConfigure.txt）里登记的文件名 —— 新版引用字段要用它，不是路径。
        private const string KeyRecover  = "FX_CommonRecover";
        private const string KeyPuncture = "FXSkillHit_Puncture";
        private const string KeySlash    = "FX_BladeHit";
        private const string KeyHit      = "FX_CommonHit";

        private const string SndKeyRecover = "common_recover";
        private const string SndKeyBlade   = "blade_hit";
        private const string SndKeyKnife   = "weapon_knife";

        // 每种资源只做一次"到底能不能加载"的自检，免得每次命中都同步读一遍资源。
        private static readonly HashSet<string> Probed = new HashSet<string>();

        /// <summary>回血（吸血时自己身上那一下）。</summary>
        internal static void Recover(BattleRole self)
        {
            Play(KeyRecover, FxRecover, SndKeyRecover, SndRecover, self, self, 1f);
        }

        /// <summary>突刺命中（荆棘：先发 + 主动攻击，整次攻击只播一次）。</summary>
        internal static void Puncture(BattleRole self, BattleRole target)
        {
            Play(KeyPuncture, FxPuncture, SndKeyKnife, SndKnife, self, target, 1.2f);
        }

        /// <summary>斩击命中（葬花：主动攻击 + 反击；灰暗孤影的反击也用它）。</summary>
        internal static void Slash(BattleRole self, BattleRole target)
        {
            Play(KeySlash, FxSlash, SndKeyBlade, SndBlade, self, target, 1.2f);
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
}
