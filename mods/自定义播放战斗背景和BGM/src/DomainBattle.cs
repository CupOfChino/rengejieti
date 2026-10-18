// 战斗侧的总调度。
//
// 规则（用户口径）：
//   · 战斗开始时：所有"特质苏醒 + 有配置"的调查员里，**只让速度最慢的那一个**执行切换；
//   · 战斗中【领域展开】：使用者自己执行一次切换（后出手覆盖先出手的内容）；
//   · "原始 BGM"一场战斗只记一次（战斗开始清空，第一次真正切换前记录）；
//   · 战斗结束（离开战斗）时：背景层销毁 + 比较 BGM 后复原。

using System;
using System.Collections.Generic;
using System.IO;
using Game;
using UnityEngine;

namespace CustomBattleBg
{
    internal static class DomainBattle
    {
        private static bool _sessionActive;     // 本场战斗的会话是否已经开始
        private static bool _originRecorded;    // 本场是否已经记录过原始 BGM
        private static string _domainOwnerKey = "";   // 当前领域的归属（角色标识；同一个角色不重复展开）

        /// <summary>战斗侧的统一入口（由 Harmony 在 BattleRole.TriggerBuffs 上转发）。</summary>
        internal static void OnBattleTrigger(BattleRole role, EBuffTriggerType trigger)
        {
            try
            {
                if (role == null || role.Data == null)
                {
                    return;
                }
                if (trigger == EBuffTriggerType.BattleStart)
                {
                    DomainSkill.OnBattleStart(role);
                    if (IsAllyRole(role) && !_sessionActive)
                    {
                        _sessionActive = true;
                        _originRecorded = false;
                        ApplySlowestDomain();
                    }
                }
                else if (trigger == EBuffTriggerType.BattleEnd)
                {
                    DomainSkill.OnBattleEnd(role);
                }
                else if (trigger == EBuffTriggerType.BeforeRoundStart)
                {
                    // 每轮开始：结算代价 + 施加 buff 类效果（只认当前领域的展开者）
                    DomainEffects.OnRoundStart(role, "轮开始");
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("处理战斗触发失败（" + trigger + "）：" + e);
            }
        }

        private static bool IsAllyRole(BattleRole role)
        {
            try
            {
                return role.IsAlly;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 战斗开始的自动展开：收集所有"特质苏醒 + 有配置"的调查员，
        /// 取速度最慢的那一名执行（同时触发时，最慢的等于最后出手，覆盖其他人）。
        /// </summary>
        private static void ApplySlowestDomain()
        {
            try
            {
                if (BattleHelper.FightContent == null || BattleHelper.FightContent.Allies == null)
                {
                    return;
                }
                List<BattleRole> allies = BattleHelper.FightContent.Allies;
                BattleRole slowest = null;
                int slowestSpeed = int.MaxValue;
                for (int i = 0; i < allies.Count; i++)
                {
                    BattleRole ally = allies[i];
                    if (ally == null || ally.Data == null)
                    {
                        continue;
                    }
                    if (!HasAwakeTrait(ally.Data))
                    {
                        continue;
                    }
                    DomainProfile profile = CustomBattleBgPlugin.Store.Find(ally.Data);
                    if (profile == null || !profile.HasAnyContent)
                    {
                        continue;
                    }
                    int speed = 0;
                    try
                    {
                        speed = ally.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.Speed);
                    }
                    catch (Exception)
                    {
                    }
                    if (slowest == null || speed < slowestSpeed)
                    {
                        slowest = ally;
                        slowestSpeed = speed;
                    }
                }

                if (slowest == null)
                {
                    CustomBattleBgPlugin.LogInfo("战斗开始：没有需要展开领域的调查员");
                    return;
                }

                DomainProfile p = CustomBattleBgPlugin.Store.Find(slowest.Data);
                string name = DomainStore.SafeRoleName(slowest.Data);
                CustomBattleBgPlugin.LogInfo("战斗开始：由速度最慢的「" + name + "」（速度 " + slowestSpeed +
                    "）展开领域：" + (p != null ? p.DisplayName : "?"));
                _domainOwnerKey = GetRoleKey(slowest.Data);
                bool backgroundApplied = ApplyProfile(p, "战斗开始");
                DomainEffects.ApplyOnOpen(slowest, p, "战斗开始", backgroundApplied);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("战斗开始展开领域失败：" + e);
            }
        }

        internal static bool HasAwakeTrait(RoleData roleData)
        {
            try
            {
                if (roleData == null)
                {
                    return false;
                }
                MOD_Dynamic_Trait trait = roleData.GetTraitData(DomainConstants.TraitId);
                return trait != null && trait.CurrentState == ETraitState.Wake;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>只要拥有【领域】特质就行（不看醒/睡）——主动展开和挂载行动用这个条件。</summary>
        internal static bool HasDomainTrait(RoleData roleData)
        {
            try
            {
                if (roleData == null)
                {
                    return false;
                }
                return roleData.GetTraitData(DomainConstants.TraitId) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>保证"原始 BGM"已经记录（一场战斗只记一次，记在第一次切换之前）。</summary>
        internal static void EnsureOriginRecorded()
        {
            // 只有确实在战斗里才开启/复用"本场战斗会话"。
            // 外部模组在非战斗场景只切 BGM 时，不能把会话标记弄脏，
            // 否则下一场战斗开始时不会重新记录、不会自动展开。
            if (IsBattleActive() && !_sessionActive)
            {
                _sessionActive = true;
                _originRecorded = false;
            }
            if (!_originRecorded)
            {
                BattleBgm.RecordOrigin();
                _originRecorded = true;
            }
        }

        /// <summary>当前是否真的有战斗场景和战斗相机（探索/大厅为 false）。</summary>
        internal static bool IsBattleActive()
        {
            try
            {
                return BattleHelper.FightContent != null && BattleHelper.FightContent.Room != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ==================== 外部模组 API 的运行时入口（2026-10-18）====================

        /// <summary>
        /// 外部定义展开：不要求【领域】特质，也不要求角色有玩家配置。
        /// force=false 时，同一个施法者 + 同一个定义已经在场就不重复展开；
        /// force=true 时每次都重新应用背景/BGM并刷新领域效果。
        /// </summary>
        internal static bool ExpandExternal(DomainProfile profile, BattleRole self, string reason, bool force)
        {
            try
            {
                if (profile == null || self == null || self.Data == null)
                {
                    return false;
                }
                if (!IsBattleActive())
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）当前不在战斗中，不能展开战斗背景：" +
                        profile.DisplayName);
                    return false;
                }

                string key = "external:" + profile.ExternalOwner + "/" + profile.ExternalId + "/" +
                             GetRoleKey(self.Data);
                if (!force && key == _domainOwnerKey)
                {
                    CustomBattleBgPlugin.LogInfo("（" + reason + "）「" + profile.DisplayName +
                        "」已经是当前领域，跳过重复展开");
                    return true;
                }

                EnsureOriginRecorded();
                _sessionActive = true;
                _domainOwnerKey = key;
                CustomBattleBgPlugin.LogInfo("（" + reason + "）外部领域展开：「" + profile.DisplayName +
                    "」，施法者：" + DomainStore.SafeRoleName(self.Data));
                bool backgroundApplied = ApplyProfile(profile, reason);
                DomainEffects.ApplyOnOpen(self, profile, reason, backgroundApplied);
                return true;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("外部领域展开失败：" + e);
                return false;
            }
        }

        /// <summary>只切背景 + BGM，不挂领域状态、不结算领域效果。</summary>
        internal static bool PlayVisual(DomainProfile profile, string reason)
        {
            try
            {
                if (profile == null || !profile.HasAnything)
                {
                    return false;
                }
                if (!IsBattleActive() || GetBattleCamera() == null)
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）当前不在战斗中，不能播放战斗背景：" +
                        profile.DisplayName);
                    return false;
                }
                EnsureOriginRecorded();
                bool backgroundApplied;
                bool videoShown = ApplyBackground(profile, reason, out backgroundApplied);
                ApplyBgm(profile, reason, videoShown);
                return backgroundApplied;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("播放外部背景/BGM失败：" + e);
                return false;
            }
        }

        /// <summary>只切背景（BGM 保持不动）。</summary>
        internal static bool PlayBackgroundOnly(DomainProfile profile, string reason)
        {
            try
            {
                if (profile == null || !profile.HasBackground)
                {
                    return false;
                }
                if (!IsBattleActive() || GetBattleCamera() == null)
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）当前不在战斗中，不能播放战斗背景：" +
                        profile.DisplayName);
                    return false;
                }
                EnsureOriginRecorded();
                bool backgroundApplied;
                ApplyBackground(profile, reason, out backgroundApplied);
                return backgroundApplied;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("播放外部背景失败：" + e);
                return false;
            }
        }

        /// <summary>只切 BGM；任意场景可用。战斗内会在战斗结束/手动停止时复原。</summary>
        internal static bool PlayBgmOnly(DomainProfile profile, string reason)
        {
            try
            {
                if (profile == null || !profile.HasBgm)
                {
                    return false;
                }
                string path = CustomBattleBgPlugin.EnsureStore().ResolveBgmPath(profile);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）BGM 文件不存在：" + path);
                    return false;
                }
                EnsureOriginRecorded();
                BattleBgm.SwitchTo(path, profile.BgmLoop);
                CustomBattleBgPlugin.LogInfo("（" + reason + "）播放外部 BGM：" + profile.BgmFile);
                return true;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("播放外部 BGM 失败：" + e);
                return false;
            }
        }

        /// <summary>停止当前领域：移除效果、隐藏背景层、BGM 复原。</summary>
        internal static bool StopPlayback(string reason)
        {
            try
            {
                DomainEffects.RemoveCurrent(reason);
                BattleBgLayer.Hide();
                BattleBgm.RestoreOrigin(reason);
                _domainOwnerKey = "";
                BattleBgm.ClearRecord();
                _originRecorded = false;
                if (!IsBattleActive())
                {
                    _sessionActive = false;
                }
                CustomBattleBgPlugin.LogInfo("（" + reason + "）已停止当前领域播放");
                return true;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("停止领域播放失败：" + e);
                return false;
            }
        }

        /// <summary>把某个调查员的配置应用上去（背景 + BGM）。</summary>
        internal static bool ApplyProfile(DomainProfile profile, string reason)
        {
            if (profile == null)
            {
                CustomBattleBgPlugin.LogInfo("（" + reason + "）没有配置，跳过");
                return false;
            }
            if (!profile.HasAnyContent)
            {
                CustomBattleBgPlugin.LogInfo("（" + reason + "）" + profile.DisplayName + " 没有配任何背景/BGM/效果，跳过");
                return false;
            }
            EnsureOriginRecorded();

            bool backgroundApplied;
            bool videoShown = ApplyBackground(profile, reason, out backgroundApplied);
            ApplyBgm(profile, reason, videoShown);
            return backgroundApplied;
        }

        /// <summary>应用背景；返回"这次是不是换了视频背景"（视频会接管音乐通道）。</summary>
        private static bool ApplyBackground(DomainProfile profile, string reason, out bool backgroundApplied)
        {
            backgroundApplied = false;
            try
            {
                if (!profile.HasBackground)
                {
                    CustomBattleBgPlugin.LogInfo("（" + reason + "）没有配置战斗背景，背景保持原样");
                    // 没有背景可换，不阻止纯 BGM/效果/对话框领域继续表现。
                    backgroundApplied = true;
                    return false;
                }
                string path = CustomBattleBgPlugin.EnsureStore().ResolveBackgroundPath(profile);
                if (!File.Exists(path))
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）背景文件不存在，跳过背景切换：" + path);
                    return false;
                }
                Camera cam = GetBattleCamera();
                if (cam == null)
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）找不到战斗相机，跳过背景切换");
                    return false;
                }
                bool isVideo = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
                // 「只占上半屏」只对视频生效；图片永远全屏铺满（用户 2026-10-01 口径）
                bool useHalfScreen = isVideo && profile.HalfScreen;
                if (!BattleBgLayer.Show(cam, path, isVideo, useHalfScreen))
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）战斗背景层没有成功显示，跳过表现层");
                    return false;
                }
                backgroundApplied = true;
                CustomBattleBgPlugin.LogInfo("（" + reason + "）切换战斗背景：" + profile.BgFile +
                    (isVideo ? "（视频）" : "（图片）") + (useHalfScreen ? "（只占上半屏）" : ""));
                return isVideo;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("切换战斗背景失败：" + e);
                return false;
            }
        }

        private static void ApplyBgm(DomainProfile profile, string reason, bool videoShown)
        {
            try
            {
                string path = profile.HasBgm
                    ? CustomBattleBgPlugin.EnsureStore().ResolveBgmPath(profile)
                    : null;
                if (path != null && File.Exists(path))
                {
                    BattleBgm.SwitchTo(path, profile.BgmLoop);
                    CustomBattleBgPlugin.LogInfo("（" + reason + "）切换战斗BGM：" + profile.BgmFile +
                        (profile.BgmLoop ? "（循环）" : "（单次）"));
                    return;
                }
                if (profile.HasBgm)
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）BGM 文件不存在，跳过 BGM 切换：" + path);
                }

                // 没配 BGM（或文件丢了）：
                // 这次展开带的是**视频**背景 → 视频自带声音，按用户口径关掉游戏自带的战斗 BGM；
                // 图片背景则保持现状（游戏 BGM 照常）。
                if (videoShown)
                {
                    BattleBgm.MuteGameBgm(reason);
                }
                else
                {
                    CustomBattleBgPlugin.LogInfo("（" + reason + "）没有配置战斗BGM，BGM 保持原样");
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("切换战斗BGM失败：" + e);
            }
        }

        private static Camera GetBattleCamera()
        {
            try
            {
                if (BattleHelper.FightContent == null || BattleHelper.FightContent.Room == null)
                {
                    return null;
                }
                BattleRoom room = BattleHelper.FightContent.Room;
                if (room.RoomEntity == null)
                {
                    return null;
                }
                return room.RoomEntity.Cam;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>离开战斗（GameWorld.ExitBattle 的 Prefix）：背景复原 + BGM 比较复原。</summary>
        internal static void OnExitBattle()
        {
            try
            {
                DomainEffects.RemoveCurrent("战斗结束");
                BattleBgLayer.Hide();
                BattleBgm.RestoreOrigin();
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("战斗结束复原失败：" + e);
            }
            finally
            {
                _sessionActive = false;
                _originRecorded = false;
                _domainOwnerKey = "";
                BattleBgm.ClearRecord();
            }
        }

        /// <summary>
        /// 重开战斗（GameWorld.RestartBattle 的 Prefix）：
        /// 把本场会话标记清掉，新的战斗开始时会重新记录、重新展开。
        /// </summary>
        internal static void OnRestartBattle()
        {
            try
            {
                DomainEffects.RemoveCurrent("重开战斗");
                BattleBgLayer.Hide();
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("重开战斗时清理背景层失败：" + e.Message);
            }
            _sessionActive = false;
            _originRecorded = false;
            _domainOwnerKey = "";
        }

        /// <summary>战斗内的手动展开（【领域展开】技艺）。</summary>
        internal static void ExpandFor(BattleRole self, string reason)
        {
            try
            {
                if (self == null || self.Data == null)
                {
                    return;
                }
                RoleData roleData = self.Data;
                DomainProfile profile = CustomBattleBgPlugin.Store.Find(roleData);
                string name = DomainStore.SafeRoleName(roleData);
                if (profile == null)
                {
                    CustomBattleBgPlugin.LogInfo("（" + reason + "）「" + name + "」没有配置，跳过");
                    return;
                }
                if (!HasDomainTrait(roleData))
                {
                    CustomBattleBgPlugin.LogInfo("（" + reason + "）「" + name + "」没有【领域】特质，跳过");
                    return;
                }
                // 领域归属判定：现在就是他的领域 → 不重复展开；是别人的 → 展开并刷新归属
                string key = GetRoleKey(roleData);
                if (!string.IsNullOrEmpty(key) && key == _domainOwnerKey)
                {
                    CustomBattleBgPlugin.LogInfo("（" + reason + "）领域现在就是「" + name + "」的，跳过重复展开");
                    return;
                }
                _domainOwnerKey = key;
                CustomBattleBgPlugin.LogInfo("（" + reason + "）「" + name + "」展开领域：" + profile.DisplayName);
                bool backgroundApplied = ApplyProfile(profile, reason);
                DomainEffects.ApplyOnOpen(self, profile, reason, backgroundApplied);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("领域展开失败：" + e);
            }
        }

        /// <summary>角色的稳定标识（拿不到 RoleLibraryKey 就用名字兜底）。</summary>
        private static string GetRoleKey(RoleData roleData)
        {
            try
            {
                if (roleData == null)
                {
                    return "";
                }
                if (!string.IsNullOrEmpty(roleData.RoleLibraryKey))
                {
                    return roleData.RoleLibraryKey;
                }
                return "name:" + (roleData.Name != null ? roleData.Name : "");
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// 战斗中使用行动时的检查（BattleActiveBehaviorData.Run 的 Prefix）：
        /// 这个角色如果挂了"用了这个行动就展开领域"，就在这里展开。
        /// </summary>
        internal static void OnActionStart(BattleActiveBehaviorData data)
        {
            try
            {
                if (data == null || data.Self == null || data.Self.Data == null)
                {
                    return;
                }
                // 外部模组预先绑定在技能 / 法术上的领域优先于角色自己的玩家配置；
                // 同一条行动只触发最后注册的那个外部绑定。
                if (DomainApi.TryTriggerActionBinding(data))
                {
                    return;
                }
                RoleData roleData = data.Self.Data;
                DomainProfile profile = CustomBattleBgPlugin.Store.Find(roleData);
                if (profile == null || !profile.HasAnyContent || profile.MountType == 0)
                {
                    return;
                }
                if (!HasDomainTrait(roleData))
                {
                    return;
                }
                if (!MatchesMountAction(data, profile))
                {
                    return;
                }
                ExpandFor(data.Self, "挂载行动");
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("挂载行动触发失败：" + e);
            }
        }

        private static bool MatchesMountAction(BattleActiveBehaviorData data, DomainProfile profile)
        {
            switch (profile.MountType)
            {
                case 1:   // 具体战斗技能
                    return data.BattleSkillData != null && data.BattleSkillData.Id == profile.MountId;
                case 2:   // 任意法术
                    return data.MagicData != null;
                case 3:   // 具体法术
                    return data.MagicData != null && data.MagicData.Id == profile.MountId;
                default:
                    return false;
            }
        }
    }

    // 战斗开始 / 战斗结束时，每个角色都会走这里；转发给总调度（自定义心事同款挂点）。
    [HarmonyLib.HarmonyPatch(typeof(BattleRole), "TriggerBuffs")]
    internal static class Patch_BattleRole_TriggerBuffs
    {
        private static void Prefix(BattleRole __instance, EBuffTriggerType trigger)
        {
            DomainBattle.OnBattleTrigger(__instance, trigger);
        }
    }

    // 离开战斗时复原（此时游戏还没切回地图 BGM，我们先归位，随后游戏自己也会恢复房间 BGM）
    [HarmonyLib.HarmonyPatch(typeof(GameWorld), "ExitBattle")]
    internal static class Patch_GameWorld_ExitBattle
    {
        private static void Prefix()
        {
            DomainBattle.OnExitBattle();
        }
    }

    // 重开战斗：清掉会话标记，让新战斗重新展开
    [HarmonyLib.HarmonyPatch(typeof(GameWorld), "RestartBattle")]
    internal static class Patch_GameWorld_RestartBattle
    {
        private static void Prefix()
        {
            DomainBattle.OnRestartBattle();
        }
    }

    // 战斗中使用行动（BattleActiveBehaviorData.Run 的开头）：
    // 挂了"使用某行动就展开领域"的角色，在这里触发。
    [HarmonyLib.HarmonyPatch(typeof(BattleActiveBehaviorData), "Run")]
    internal static class Patch_BattleActiveBehaviorData_Run
    {
        private static void Prefix(BattleActiveBehaviorData __instance)
        {
            DomainBattle.OnActionStart(__instance);
        }
    }
}
