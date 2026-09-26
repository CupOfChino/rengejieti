// 私货内容用的原版特效 / 音效（一次性播放）。
//
// 全是游戏自带资源，一个自定义素材都没有：
//   · 吸血回血 → Effect/Prefabs/FX_CommonRecover  + Sound/Audio/FX/Battle/common_recover
//   · 突刺命中 → Effect/Prefabs/FXSkillHit_Puncture + Sound/Audio/FX/weapon_knife
//   · 斩击命中 → Effect/Prefabs/FX_BladeHit        + Sound/Audio/FX/Battle/blade_hit
//
// 播放走的是游戏自己的 EffectShowData.Play —— 和武器数据里 Damage.EffectShow 完全同一条路
// （`PlayRoleEffectData` 播特效、`PlaySoundData` 播音效），所以表现跟原版武器是一致的。

using System;
using System.Collections.Generic;
using Game;
using MOD;

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

        /// <summary>回血（吸血时自己身上那一下）。</summary>
        internal static void Recover(BattleRole self)
        {
            Play(FxRecover, SndRecover, self, self, 1f);
        }

        /// <summary>突刺命中（荆棘的先发攻击）。</summary>
        internal static void Puncture(BattleRole self, BattleRole target)
        {
            Play(FxPuncture, SndKnife, self, target, 1.2f);
        }

        /// <summary>斩击命中（灰暗孤影的反击）。</summary>
        internal static void Slash(BattleRole self, BattleRole target)
        {
            Play(FxSlash, SndBlade, self, target, 1.2f);
        }

        /// <summary>被吸取那一下（只在敌人身上闪一下，不配音）。</summary>
        internal static void Drain(BattleRole self, BattleRole target)
        {
            Play(FxHit, null, self, target, 0.8f);
        }

        /// <summary>在 target 身上播一次性特效 + 音效；self 是"发起者"（决定演出归属）。</summary>
        private static async void Play(string fxPath, string sndPath, BattleRole self, BattleRole target, float duration)
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
                    fx.FxPath = fxPath;
                    fx.Duration = duration;
                    fx.PointType = ERolePointType.Center;
                    fx.UseSameFx = true;
                    show.EffectShows.Add(fx);
                }
                if (!string.IsNullOrEmpty(sndPath))
                {
                    PlaySoundData snd = new PlaySoundData();
                    snd.AudioClip = new AudioClipData();
                    snd.AudioClip.AudioRes = sndPath;
                    snd.AudioClip.Volume = 1f;
                    show.EffectShows.Add(snd);
                }
                List<BattleRole> targets = new List<BattleRole>();
                if (target != null)
                {
                    targets.Add(target);
                }
                await show.Play(caster, targets);
            }
            catch (Exception e)
            {
                AttackTargetPlugin.LogError("私货特效：播放出错：" + e.Message);
            }
        }
    }
}
