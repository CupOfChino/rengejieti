// 「自定义播放战斗背景和BGM」的运行时扩展接口（2026-10-18）。
//
// 给其它模组作者用：
//   · 注册一套自己的领域（背景 / BGM / 领域效果），不要求玩家先建角色配置；
//   · 在任意战斗方法里直接展开，或者把领域预先绑定到某个技能 / 法术；
//   · 只切背景、只切 BGM、查询当前领域、停止播放。
//
// 这个文件只放公开 API 和内存注册表。真正的播放 / 效果逻辑仍在
// DomainBattle / DomainEffects / BattleBgLayer / BattleBgm 里，和玩家配置共用同一条路径。

using System;
using System.Collections.Generic;
using System.IO;
using Game;

namespace CustomBattleBg
{
    /// <summary>领域作用于友方的范围（数值与 DomainProfile.AllyTarget 一致）。</summary>
    public enum DomainTargetType
    {
        None = 0,
        Caster = 1,
        AllAllies = 2
    }

    /// <summary>展开领域的代价类型（数值与 DomainProfile.CostType 一致）。</summary>
    public enum DomainCostType
    {
        None = 0,
        Hp = 1,
        San = 2,
        Mp = 3
    }

    /// <summary>预先绑定时，行动属于战斗技能还是法术。</summary>
    public enum DomainActionType
    {
        /// <summary>具体战斗技能（BattleSkillData.Id）。</summary>
        BattleSkill = 1,

        /// <summary>任意法术（不检查 id）。</summary>
        AnyMagic = 2,

        /// <summary>具体法术（MagicData.Id）。</summary>
        Magic = 3
    }

    /// <summary>
    /// 外部模组注册的一套领域定义。只作为注册入参，注册后插件会复制一份，
    /// 之后修改这个对象不会影响已经注册的领域（重新 RegisterDomain 即可更新）。
    /// </summary>
    public sealed class DomainDefinition
    {
        /// <summary>状态栏里显示的领域名；留空则使用注册 id。</summary>
        public string DisplayName = "";

        /// <summary>
        /// 相对资源路径的根目录。可留空：留空时相对路径按玩家资源库的
        /// Background / Bgm / Sfx 目录解析；绝对路径则直接使用。
        /// </summary>
        public string AssetRoot = "";

        /// <summary>背景文件（mp4 / png / jpg），绝对路径或相对 AssetRoot 的路径。</summary>
        public string BackgroundPath = "";

        /// <summary>背景只占上半屏（仅视频生效，和玩家编辑窗一致）。</summary>
        public bool HalfScreen = false;

        /// <summary>BGM 文件（ogg / mp3），绝对路径或相对 AssetRoot 的路径。</summary>
        public string BgmPath = "";

        /// <summary>BGM 是否循环。</summary>
        public bool BgmLoop = true;

        /// <summary>展开成功时是否显示角色对话框。</summary>
        public bool ShowDialogOnOpen = false;

        /// <summary>对话框里使用的领域后缀；留空时用 DisplayName。</summary>
        public string DialogSuffix = "";

        /// <summary>展开成功时是否播放一次音效。</summary>
        public bool PlaySoundOnOpen = false;

        /// <summary>展开音效文件（ogg / mp3 / wav），绝对路径或相对 AssetRoot 的路径。</summary>
        public string SoundPath = "";

        /// <summary>领域作用于友方的范围。</summary>
        public DomainTargetType AllyTarget = DomainTargetType.None;

        /// <summary>是否作用于敌方（对所有当前敌人施加敌方效果）。</summary>
        public bool EnemyTarget = false;

        /// <summary>友方效果：DomainEffectPreset 常量，或 DomainEffectPreset.BuffOffset + buffId。</summary>
        public int AllyEffectType = DomainEffectPreset.None;

        /// <summary>友方效果数值（层数 / 百分比档位 / 资源点数，随效果类型而定）。</summary>
        public int AllyEffectValue = 0;

        /// <summary>敌方效果：DomainEffectPreset 常量，或 DomainEffectPreset.BuffOffset + buffId。</summary>
        public int EnemyEffectType = DomainEffectPreset.None;

        /// <summary>敌方效果数值。</summary>
        public int EnemyEffectValue = 0;

        /// <summary>展开代价类型。</summary>
        public DomainCostType CostType = DomainCostType.None;

        /// <summary>展开代价数值（不可为负；负数按 0 处理）。</summary>
        public int CostValue = 0;

        internal DomainDefinition Clone()
        {
            return (DomainDefinition)MemberwiseClone();
        }
    }

    /// <summary>
    /// 预先绑定：某个技能 / 法术被使用时，自动展开指定领域。
    /// Requester + DomainId 指向 RegisterDomain 注册的定义。
    /// </summary>
    public sealed class DomainActionBinding
    {
        public string Requester = "";
        public string DomainId = "";
        public DomainActionType ActionType = DomainActionType.BattleSkill;

        /// <summary>战斗技能 id 或法术 id；AnyMagic 时忽略。</summary>
        public int ActionId = 0;

        /// <summary>限定施法者的 RoleLibraryKey；留空不按存档编号限制。</summary>
        public string RoleKey = "";

        /// <summary>限定施法者的名字；留空不按名字限制。</summary>
        public string RoleName = "";

        /// <summary>限定施法者的角色配置编号（怪物 / NPC 常用）；0 = 不限制。</summary>
        public int RoleConfigId = 0;

        /// <summary>
        /// true = 每次使用都重新展开并刷新效果；
        /// false = 这个领域已经是当前领域时不重复展开（被别的领域顶掉后可以再次展开）。
        /// </summary>
        public bool ReapplyEveryUse = false;

        internal DomainActionBinding Clone()
        {
            return (DomainActionBinding)MemberwiseClone();
        }
    }

    /// <summary>当前领域的只读快照。</summary>
    public sealed class DomainRuntimeInfo
    {
        public bool Active;
        public string DisplayName = "";
        public string Owner = "";
        public string DomainId = "";
        public string CasterRoleKey = "";
        public string CasterName = "";
        public bool HasBackground;
        public bool HasBgm;
        public bool ShowOpenDialog;
        public bool PlayOpenSound;
    }

    public static partial class DomainApi
    {
        private sealed class RegisteredDomain
        {
            internal string Requester;
            internal string Id;
            internal DomainProfile Profile;
        }

        private static readonly object RuntimeGate = new object();
        private static readonly Dictionary<string, RegisteredDomain> RegisteredDomains =
            new Dictionary<string, RegisteredDomain>();
        private static readonly List<DomainActionBinding> ActionBindings =
            new List<DomainActionBinding>();

        // ==================== 领域定义 ====================

        /// <summary>
        /// 注册或更新一套外部领域。同一个 requester + id 重复注册会覆盖旧定义
        /// （建议在插件 Awake / 游戏启动阶段注册）。
        /// 返回 false 只表示参数或内容无效；文件暂时不存在会写警告但允许注册，
        /// 真正展开时仍然按“缺文件就跳过”的容错处理。
        /// </summary>
        public static bool RegisterDomain(string requester, string id, DomainDefinition definition)
        {
            try
            {
                requester = Clean(requester);
                id = Clean(id);
                if (string.IsNullOrEmpty(requester) || string.IsNullOrEmpty(id) || definition == null)
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：requester / id / definition 不能为空");
                    return false;
                }

                DomainDefinition def = definition.Clone();
                string displayName = Clean(def.DisplayName);
                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = id;
                }

                DomainStore store = CustomBattleBgPlugin.EnsureStore();
                string backgroundPath = ResolveAssetPath(def.BackgroundPath, def.AssetRoot, store.BackgroundDir);
                string bgmPath = ResolveAssetPath(def.BgmPath, def.AssetRoot, store.BgmDir);
                string sfxPath = ResolveAssetPath(def.SoundPath, def.AssetRoot, store.SfxDir);

                if (!string.IsNullOrEmpty(backgroundPath) &&
                    !HasAllowedExtension(backgroundPath, DomainConstants.BackgroundExtensions))
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：背景只支持 mp4 / png / jpg —— " + backgroundPath);
                    return false;
                }
                if (!string.IsNullOrEmpty(bgmPath) &&
                    !HasAllowedExtension(bgmPath, DomainConstants.BgmExtensions))
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：BGM 只支持 ogg / mp3 —— " + bgmPath);
                    return false;
                }
                if (!string.IsNullOrEmpty(sfxPath) &&
                    !HasAllowedExtension(sfxPath, DomainConstants.SfxExtensions))
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：音效只支持 ogg / mp3 / wav —— " + sfxPath);
                    return false;
                }
                if (string.IsNullOrEmpty(backgroundPath) && string.IsNullOrEmpty(bgmPath) &&
                    string.IsNullOrEmpty(sfxPath) &&
                    def.AllyTarget == DomainTargetType.None && !def.EnemyTarget &&
                    def.AllyEffectType == DomainEffectPreset.None &&
                    def.EnemyEffectType == DomainEffectPreset.None &&
                    def.CostType == DomainCostType.None &&
                    !def.ShowDialogOnOpen && !def.PlaySoundOnOpen)
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：背景、BGM、领域效果都没有配置 —— " + id);
                    return false;
                }
                if (def.AllyEffectType != DomainEffectPreset.None &&
                    def.AllyTarget == DomainTargetType.None)
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：配了友方效果但没有设置 AllyTarget —— " + id);
                    return false;
                }
                if (def.EnemyEffectType != DomainEffectPreset.None && !def.EnemyTarget)
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：配了敌方效果但没有打开 EnemyTarget —— " + id);
                    return false;
                }
                if (def.PlaySoundOnOpen && string.IsNullOrEmpty(sfxPath))
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：PlaySoundOnOpen=true 但没有提供 SoundPath —— " + id);
                    return false;
                }

                string key = DomainKey(requester, id);
                RegisteredDomain registered;
                lock (RuntimeGate)
                {
                    if (!RegisteredDomains.TryGetValue(key, out registered))
                    {
                        registered = new RegisteredDomain();
                        registered.Requester = requester;
                        registered.Id = id;
                        registered.Profile = new DomainProfile();
                        RegisteredDomains[key] = registered;
                    }
                }

                DomainProfile profile = registered.Profile;
                if (DomainEffects.HasActiveDomain && ReferenceEquals(DomainEffects.Current, profile))
                {
                    CustomBattleBgPlugin.LogError("注册领域失败：「" + displayName +
                        "」正在战斗中生效，请先调用 StopPlayback 再更新定义");
                    return false;
                }

                // 保留运行时 buff 编号，避免每次更新定义都换号。
                profile.RuntimeDisplayName = displayName;
                profile.ExternalOwner = requester;
                profile.ExternalId = id;
                profile.RoleKey = "";
                profile.RoleName = displayName;
                profile.Suffix = "";
                profile.Section = "";
                profile.BackgroundFullPath = backgroundPath;
                profile.BgFile = string.IsNullOrEmpty(backgroundPath) ? "" : Path.GetFileName(backgroundPath);
                profile.BgmFullPath = bgmPath;
                profile.BgmFile = string.IsNullOrEmpty(bgmPath) ? "" : Path.GetFileName(bgmPath);
                profile.SfxFullPath = sfxPath;
                profile.SfxFile = string.IsNullOrEmpty(sfxPath) ? "" : Path.GetFileName(sfxPath);
                profile.BgmLoop = def.BgmLoop;
                profile.ShowOpenDialog = def.ShowDialogOnOpen;
                profile.PlayOpenSound = def.PlaySoundOnOpen;
                profile.RuntimeDialogSuffix = Clean(def.DialogSuffix);
                profile.HalfScreen = def.HalfScreen;
                profile.MountType = 0;
                profile.MountId = 0;
                profile.MountName = "";
                profile.AllyTarget = (int)def.AllyTarget;
                profile.EnemyTarget = def.EnemyTarget;
                profile.AllyEffectType = def.AllyEffectType;
                profile.AllyEffectValue = def.AllyEffectValue;
                profile.EnemyEffectType = def.EnemyEffectType;
                profile.EnemyEffectValue = def.EnemyEffectValue;
                profile.CostType = (int)def.CostType;
                profile.CostValue = Math.Max(0, def.CostValue);

                LogAssetWarning(backgroundPath, "背景");
                LogAssetWarning(bgmPath, "BGM");
                LogAssetWarning(sfxPath, "音效");
                DomainBuffBuilder.Rebuild(profile);
                CustomBattleBgPlugin.LogInfo("已注册外部领域：「" + requester + "/" + id + "」→ " + displayName +
                    "，背景=" + (profile.HasBackground ? profile.BgFile : "(无)") +
                    "，BGM=" + (profile.HasBgm ? profile.BgmFile : "(无)") +
                    "，展开表现=" + (profile.HasOpeningPresentation ? "对话框/音效" : "(无)"));
                return true;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("注册领域失败：" + e);
                return false;
            }
        }

        /// <summary>注销一套外部领域；如果它正在生效，会先停止播放。不会删除资源文件。</summary>
        public static bool UnregisterDomain(string requester, string id)
        {
            try
            {
                requester = Clean(requester);
                id = Clean(id);
                string key = DomainKey(requester, id);
                RegisteredDomain registered = null;
                lock (RuntimeGate)
                {
                    if (RegisteredDomains.TryGetValue(key, out registered))
                    {
                        RegisteredDomains.Remove(key);
                    }
                    for (int i = ActionBindings.Count - 1; i >= 0; i--)
                    {
                        DomainActionBinding b = ActionBindings[i];
                        if (string.Equals(b.Requester, requester, StringComparison.Ordinal) &&
                            string.Equals(b.DomainId, id, StringComparison.Ordinal))
                        {
                            ActionBindings.RemoveAt(i);
                        }
                    }
                }
                if (registered == null)
                {
                    return false;
                }
                if (DomainEffects.HasActiveDomain && ReferenceEquals(DomainEffects.Current, registered.Profile))
                {
                    DomainBattle.StopPlayback("外部领域注销");
                }
                DomainBuffBuilder.Unregister(registered.Profile);
                CustomBattleBgPlugin.LogInfo("已注销外部领域：「" + requester + "/" + id + "」");
                return true;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("注销领域失败：" + e);
                return false;
            }
        }

        // ==================== 立即播放 / 展开 ====================

        /// <summary>
        /// 在任意战斗方法里立即展开一套外部领域（背景 + BGM + 领域效果）。
        /// 不要求角色有【领域】特质或玩家配置；每次调用都会重新应用。
        /// </summary>
        public static bool ExpandDomain(string requester, string id, BattleRole caster, string reason = "")
        {
            try
            {
                RegisteredDomain registered = FindDomain(requester, id);
                if (registered == null)
                {
                    CustomBattleBgPlugin.LogError("展开领域失败：没有注册 「" + Clean(requester) + "/" + Clean(id) + "」");
                    return false;
                }
                return DomainBattle.ExpandExternal(registered.Profile, caster,
                    SafeReason(reason, "外部 API 展开"), true);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("展开外部领域失败：" + e);
                return false;
            }
        }

        /// <summary>只有 RoleData、没有 BattleRole 时的便捷重载（用 RoleData.Role 找战斗实例）。</summary>
        public static bool ExpandDomainForRole(string requester, string id, RoleData caster, string reason = "")
        {
            try
            {
                if (caster == null || caster.Role == null)
                {
                    CustomBattleBgPlugin.LogError("展开领域失败：RoleData 没有关联的 BattleRole（通常表示不在战斗中）");
                    return false;
                }
                return ExpandDomain(requester, id, caster.Role, reason);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("展开外部领域失败：" + e);
                return false;
            }
        }

        /// <summary>只切背景 + BGM，不挂领域状态、不结算效果。</summary>
        public static bool PlayVisual(string requester, string id, string reason = "")
        {
            RegisteredDomain registered = FindDomain(requester, id);
            return registered != null &&
                   DomainBattle.PlayVisual(registered.Profile, SafeReason(reason, "外部 API 播放背景和BGM"));
        }

        /// <summary>只切背景，BGM 保持不动。</summary>
        public static bool PlayBackground(string requester, string id, string reason = "")
        {
            RegisteredDomain registered = FindDomain(requester, id);
            return registered != null &&
                   DomainBattle.PlayBackgroundOnly(registered.Profile, SafeReason(reason, "外部 API 播放背景"));
        }

        /// <summary>只切 BGM（任意场景可调用；战斗结束/StopPlayback 时复原）。</summary>
        public static bool PlayBgm(string requester, string id, string reason = "")
        {
            RegisteredDomain registered = FindDomain(requester, id);
            return registered != null &&
                   DomainBattle.PlayBgmOnly(registered.Profile, SafeReason(reason, "外部 API 播放BGM"));
        }

        /// <summary>停止当前领域：移除效果、隐藏背景、BGM 复原。</summary>
        public static bool StopPlayback(string requester, string reason = "")
        {
            CustomBattleBgPlugin.LogInfo("领域停止请求来自「" + Clean(requester) + "」");
            return DomainBattle.StopPlayback(SafeReason(reason, "外部 API 停止播放"));
        }

        /// <summary>当前有没有领域正在生效（包括玩家配置和外部定义）。</summary>
        public static bool IsDomainActive()
        {
            return DomainEffects.HasActiveDomain;
        }

        /// <summary>
        /// 战斗房间是否已经创建。`BattleStart` / `BeforeBattleStart` 触发时通常已经是 true；
        /// 更早的 `GameWorld.EnterBattle` Prefix 可能还是 false，请延后到战斗开始再调用。
        /// </summary>
        public static bool IsBattleReady()
        {
            return DomainBattle.IsBattleActive();
        }

        /// <summary>当前领域的快照；没有领域时 Active=false，其余字段为空。</summary>
        public static DomainRuntimeInfo GetCurrentDomain()
        {
            DomainRuntimeInfo info = new DomainRuntimeInfo();
            try
            {
                DomainProfile profile = DomainEffects.Current;
                if (profile == null)
                {
                    return info;
                }
                info.Active = DomainEffects.HasActiveDomain;
                info.DisplayName = profile.DisplayName;
                info.Owner = profile.IsExternal ? profile.ExternalOwner : "玩家配置";
                info.DomainId = profile.IsExternal ? profile.ExternalId : profile.RoleKey;
                info.HasBackground = profile.HasBackground;
                info.HasBgm = profile.HasBgm;
                info.ShowOpenDialog = profile.ShowOpenDialog;
                info.PlayOpenSound = profile.PlayOpenSound;
                BattleRole owner = DomainEffects.Owner;
                if (owner != null && owner.Data != null)
                {
                    info.CasterRoleKey = Clean(owner.Data.RoleLibraryKey);
                    info.CasterName = DomainStore.SafeRoleName(owner.Data);
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("读取当前领域失败：" + e.Message);
            }
            return info;
        }

        // ==================== 行动绑定 ====================

        /// <summary>
        /// 预先绑定一条领域到技能 / 法术：战斗中使用该行动时自动展开。
        /// 同一个 requester + domain + 行动 + 角色条件重复注册会覆盖旧绑定；
        /// 多个绑定同时匹配时，最后注册的生效。
        /// </summary>
        public static bool BindAction(DomainActionBinding binding)
        {
            try
            {
                if (binding == null)
                {
                    return false;
                }
                DomainActionBinding b = binding.Clone();
                b.Requester = Clean(b.Requester);
                b.DomainId = Clean(b.DomainId);
                b.RoleKey = Clean(b.RoleKey);
                b.RoleName = Clean(b.RoleName);
                if (string.IsNullOrEmpty(b.Requester) || string.IsNullOrEmpty(b.DomainId))
                {
                    CustomBattleBgPlugin.LogError("绑定行动失败：Requester / DomainId 不能为空");
                    return false;
                }
                if (FindDomain(b.Requester, b.DomainId) == null)
                {
                    CustomBattleBgPlugin.LogError("绑定行动失败：领域未注册 —— " + b.Requester + "/" + b.DomainId);
                    return false;
                }
                if (b.ActionType != DomainActionType.AnyMagic && b.ActionId <= 0)
                {
                    CustomBattleBgPlugin.LogError("绑定行动失败：具体技能 / 法术必须提供 ActionId");
                    return false;
                }
                lock (RuntimeGate)
                {
                    for (int i = ActionBindings.Count - 1; i >= 0; i--)
                    {
                        if (ActionBindingKey(ActionBindings[i]) == ActionBindingKey(b))
                        {
                            ActionBindings.RemoveAt(i);
                        }
                    }
                    ActionBindings.Add(b);
                }
                CustomBattleBgPlugin.LogInfo("已绑定行动：" + DescribeBinding(b));
                return true;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("绑定行动失败：" + e);
                return false;
            }
        }

        public static bool BindAction(string requester, string domainId, DomainActionType actionType,
            int actionId, string roleKey = "", string roleName = "", int roleConfigId = 0,
            bool reapplyEveryUse = false)
        {
            DomainActionBinding b = new DomainActionBinding();
            b.Requester = requester;
            b.DomainId = domainId;
            b.ActionType = actionType;
            b.ActionId = actionId;
            b.RoleKey = roleKey;
            b.RoleName = roleName;
            b.RoleConfigId = roleConfigId;
            b.ReapplyEveryUse = reapplyEveryUse;
            return BindAction(b);
        }

        public static bool UnbindAction(string requester, string domainId, DomainActionType actionType,
            int actionId, string roleKey = "", string roleName = "", int roleConfigId = 0)
        {
            try
            {
                DomainActionBinding probe = new DomainActionBinding();
                probe.Requester = Clean(requester);
                probe.DomainId = Clean(domainId);
                probe.ActionType = actionType;
                probe.ActionId = actionId;
                probe.RoleKey = Clean(roleKey);
                probe.RoleName = Clean(roleName);
                probe.RoleConfigId = roleConfigId;
                string key = ActionBindingKey(probe);
                lock (RuntimeGate)
                {
                    for (int i = ActionBindings.Count - 1; i >= 0; i--)
                    {
                        if (ActionBindingKey(ActionBindings[i]) == key)
                        {
                            ActionBindings.RemoveAt(i);
                            return true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("解除行动绑定失败：" + e.Message);
            }
            return false;
        }

        /// <summary>清掉某个模组注册的行动绑定；domainId 留空则清掉该模组全部绑定。</summary>
        public static int ClearActionBindings(string requester, string domainId = "")
        {
            int removed = 0;
            try
            {
                requester = Clean(requester);
                domainId = Clean(domainId);
                lock (RuntimeGate)
                {
                    for (int i = ActionBindings.Count - 1; i >= 0; i--)
                    {
                        DomainActionBinding b = ActionBindings[i];
                        if (string.Equals(b.Requester, requester, StringComparison.Ordinal) &&
                            (string.IsNullOrEmpty(domainId) ||
                             string.Equals(b.DomainId, domainId, StringComparison.Ordinal)))
                        {
                            ActionBindings.RemoveAt(i);
                            removed++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("清理行动绑定失败：" + e.Message);
            }
            return removed;
        }

        // ==================== 内部：行动触发 ====================

        /// <summary>
        /// 由 DomainBattle.OnActionStart 调用。返回 true 表示这条行动已被外部绑定接管
        /// （即使这次因为去重没有重新展开，也不再回落到角色自己的玩家配置）。
        /// </summary>
        internal static bool TryTriggerActionBinding(BattleActiveBehaviorData data)
        {
            if (data == null || data.Self == null || data.Self.Data == null)
            {
                return false;
            }
            DomainActionBinding matched = null;
            RegisteredDomain registered = null;

            lock (RuntimeGate)
            {
                for (int i = ActionBindings.Count - 1; i >= 0; i--)
                {
                    DomainActionBinding b = ActionBindings[i];
                    if (!ActionMatches(b, data) || !RoleMatches(b, data.Self.Data))
                    {
                        continue;
                    }
                    RegisteredDomain r;
                    if (!RegisteredDomains.TryGetValue(DomainKey(b.Requester, b.DomainId), out r))
                    {
                        continue;
                    }
                    matched = b;
                    registered = r;
                    break;
                }
            }

            if (matched == null || registered == null)
            {
                return false;
            }
            CustomBattleBgPlugin.LogInfo("行动命中外部领域绑定：" + DescribeBinding(matched));
            DomainBattle.ExpandExternal(registered.Profile, data.Self,
                "绑定行动（" + DescribeAction(matched) + "）", matched.ReapplyEveryUse);
            // 已经命中绑定就不再回落玩家配置；即使这次因不在战斗 / 参数问题没有展开，
            // 也算这条行动由外部绑定接管。
            return true;
        }

        // ==================== 内部：查找 / 工具 ====================

        internal static bool IsBuffIdInUse(int id)
        {
            if (id <= 0)
            {
                return false;
            }
            lock (RuntimeGate)
            {
                foreach (RegisteredDomain registered in RegisteredDomains.Values)
                {
                    DomainProfile p = registered.Profile;
                    if (p != null && (p.StatusBuffId == id || p.AllyEffectBuffId == id ||
                                      p.EnemyEffectBuffId == id))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static RegisteredDomain FindDomain(string requester, string id)
        {
            requester = Clean(requester);
            id = Clean(id);
            if (string.IsNullOrEmpty(requester) || string.IsNullOrEmpty(id))
            {
                return null;
            }
            lock (RuntimeGate)
            {
                RegisteredDomain registered;
                RegisteredDomains.TryGetValue(DomainKey(requester, id), out registered);
                return registered;
            }
        }

        private static bool ActionMatches(DomainActionBinding b, BattleActiveBehaviorData data)
        {
            switch (b.ActionType)
            {
                case DomainActionType.BattleSkill:
                    return data.BattleSkillData != null && data.BattleSkillData.Id == b.ActionId;
                case DomainActionType.AnyMagic:
                    return data.MagicData != null;
                case DomainActionType.Magic:
                    return data.MagicData != null && data.MagicData.Id == b.ActionId;
                default:
                    return false;
            }
        }

        private static bool RoleMatches(DomainActionBinding b, RoleData role)
        {
            if (role == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(b.RoleKey) &&
                !string.Equals(Clean(role.RoleLibraryKey), b.RoleKey, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(b.RoleName) &&
                !string.Equals(DomainStore.SafeRoleName(role), b.RoleName, StringComparison.Ordinal))
            {
                return false;
            }
            if (b.RoleConfigId != 0 && role.ConfigId != b.RoleConfigId)
            {
                return false;
            }
            return true;
        }

        private static string ActionBindingKey(DomainActionBinding b)
        {
            return Clean(b.Requester) + "\u001f" + Clean(b.DomainId) + "\u001f" + (int)b.ActionType +
                   "\u001f" + b.ActionId + "\u001f" + Clean(b.RoleKey) + "\u001f" +
                   Clean(b.RoleName) + "\u001f" + b.RoleConfigId;
        }

        private static string DescribeBinding(DomainActionBinding b)
        {
            List<string> role = new List<string>();
            if (!string.IsNullOrEmpty(b.RoleKey)) role.Add("RoleKey=" + b.RoleKey);
            if (!string.IsNullOrEmpty(b.RoleName)) role.Add("RoleName=" + b.RoleName);
            if (b.RoleConfigId != 0) role.Add("RoleConfigId=" + b.RoleConfigId);
            return b.Requester + "/" + b.DomainId + " ← " + DescribeAction(b) +
                   (role.Count > 0 ? "（限定：" + string.Join("、", role.ToArray()) + "）" : "（任意角色）") +
                   (b.ReapplyEveryUse ? "，每次刷新" : "，在场时不重复");
        }

        private static string DescribeAction(DomainActionBinding b)
        {
            switch (b.ActionType)
            {
                case DomainActionType.BattleSkill: return "战斗技能 " + b.ActionId;
                case DomainActionType.AnyMagic: return "任意法术";
                case DomainActionType.Magic: return "法术 " + b.ActionId;
                default: return "未知行动";
            }
        }

        private static string ResolveAssetPath(string path, string assetRoot, string fallbackDir)
        {
            path = Clean(path);
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            try
            {
                if (Path.IsPathRooted(path))
                {
                    return Path.GetFullPath(path);
                }
                assetRoot = Clean(assetRoot);
                if (!string.IsNullOrEmpty(assetRoot))
                {
                    return Path.GetFullPath(Path.Combine(assetRoot, path));
                }
                return Path.GetFullPath(Path.Combine(fallbackDir, path));
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("解析资源路径失败：" + path + " —— " + e.Message);
                return path;
            }
        }

        private static void LogAssetWarning(string path, string kind)
        {
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                CustomBattleBgPlugin.LogError("警告：领域" + kind + "文件暂时不存在，展开时会跳过：" + path);
            }
        }

        private static bool HasAllowedExtension(string path, string[] allowed)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            for (int i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] == ext)
                {
                    return true;
                }
            }
            return false;
        }

        private static string DomainKey(string requester, string id)
        {
            return Clean(requester) + "\u001f" + Clean(id);
        }

        private static string Clean(string value)
        {
            return value == null ? "" : value.Trim();
        }

        private static string SafeReason(string reason, string fallback)
        {
            return string.IsNullOrEmpty(Clean(reason)) ? fallback : reason.Trim();
        }
    }
}
