// 心效果的自清理。
//
// 心 buff 被移除时（战斗结束、精神值衰弱/衰竭、倒下、手动移除），
// 把这条心曾经施加给自己/友方的状态按 ID 全部移除，避免某些没有主动
// 到期逻辑的 buff 变成永久效果。敌人身上的效果不在这里清，保留其
// 原本的 debuff 生命周期。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using MOD;

namespace XinEditor
{
    public class HeartEffectCleanupOption : BaseBuffOption
    {
        public class CleanupEntry
        {
            public int BuffId;
            public BaseBuffOption.EBuffOptionTargetType TargetType;

            public CleanupEntry(int buffId, BaseBuffOption.EBuffOptionTargetType targetType)
            {
                BuffId = buffId;
                TargetType = targetType;
            }
        }

        public List<CleanupEntry> Entries = new List<CleanupEntry>();

        private bool _cleaning;

        internal void Add(int buffId, BaseBuffOption.EBuffOptionTargetType targetType)
        {
            if (buffId <= 0)
            {
                return;
            }
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].BuffId == buffId && Entries[i].TargetType == targetType)
                {
                    return;
                }
            }
            Entries.Add(new CleanupEntry(buffId, targetType));
        }

        public override async Task OnRemove(BuffData buff, BattleRole target)
        {
            if (_cleaning || buff == null || target == null || Entries.Count == 0)
            {
                return;
            }

            _cleaning = true;
            int removed = 0;
            try
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    CleanupEntry entry = Entries[i];
                    if (entry == null || entry.BuffId <= 0)
                    {
                        continue;
                    }
                    List<BattleRole> targets;
                    try
                    {
                        targets = GetTargets(buff, target, entry.TargetType, null);
                    }
                    catch (Exception e)
                    {
                        XinEditorPlugin.LogError("心效果清理取目标失败：" + e.Message);
                        continue;
                    }
                    for (int j = 0; j < targets.Count; j++)
                    {
                        BattleRole cur = targets[j];
                        if (cur == null)
                        {
                            continue;
                        }
                        try
                        {
                            if (cur.GetBuff(entry.BuffId) != null)
                            {
                                await cur.RemoveBuff(entry.BuffId);
                                removed++;
                            }
                        }
                        catch (Exception e)
                        {
                            XinEditorPlugin.LogError("心效果清理移除状态失败（" + entry.BuffId + "）：" + e.Message);
                        }
                    }
                }
            }
            finally
            {
                _cleaning = false;
            }

            if (removed > 0)
            {
                XinEditorPlugin.LogInfo("心效果结束，已清理自己/友方状态 " + removed + " 条");
            }
        }
    }
}
