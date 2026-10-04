// 与「可视化攻击目标」的运行时兼容层（**不改它的源码 / DLL**）。
//
// 可视化模组只认"每个角色当前指针所指的那个行动槽"。多行动槽要在不动它的前提下做到：
//   1) 多槽角色每个槽各画一条箭头（颜色 / 虚线 / 去重 / 箭头头沿用它的原逻辑）；
//   2) 互指（对抗）时**每个槽各出一条黄线**，全部在双方中间汇集；
//   3) 单槽角色的一切行为保持原样。
//
// 做法：反射找到 AttackTargetVisualizer.ArrowLayer，用 Harmony 动态打 4 个补丁：
//   · Collect        —— Prefix 预判"多槽参与的对抗对"；Postfix 逐槽补普通箭头 + 自绘黄线；
//   · AddNormal      —— 多槽角色 / 被接管对抗的角色：跳过（不画单条）；
//   · TryGetSlot     —— 正在补画某个源槽时，把起点换成该槽节点的顶边中点；
//   · IsSingleHostile—— 多槽角色不参与它自己的互指配对（黄线由我们画）。
//
// 找不到可视化模组 → 整层不激活；它的方法改名 / 改结构 → 补丁挂不上也只是
// 退回"每角色一条"或"没有多槽箭头"，不影响两边运行。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Game;
using HarmonyLib;
using UnityEngine;

namespace DuoActionSlot
{
    internal static class PatchArrowCompat
    {
        private sealed class DuelPair
        {
            internal BattleRole A;
            internal BattleRole B;
        }

        private static bool _tried;
        private static bool _available;
        private static float _nextRetry;
        private static Harmony _harmony;
        private static MethodInfo _mAddNormal;
        private static MethodInfo _mCollect;
        private static MethodInfo _mTryGetSlot;
        private static MethodInfo _mIsSingleHostile;
        private static MethodInfo _mAddArrow;
        private static MethodInfo _mAddDuel;
        private static MethodInfo _mToLocal;
        private static FieldInfo _specsField;
        private static Type _arrowSpecType;
        private static FieldInfo _fFrom;
        private static FieldInfo _fTo;
        private static FieldInfo _fColor;
        private static FieldInfo _fDashed;
        private static FieldInfo _fPS0;
        private static FieldInfo _fPS1;
        private static FieldInfo _fHead;
        private static Color _clashColor = new Color(1f, 0.85f, 0.2f, 1f);

        private static readonly List<DuelPair> DuelPairs = new List<DuelPair>();
        private static readonly HashSet<BattleRole> Handled = new HashSet<BattleRole>();
        private static string _lastDuelLog = "";
        private static string _lastSpecLog = "";

        // 对抗点高度：照抄可视化模组画弧线用的那三个参数（ArcFactor / ArcMin / ArcMax），
        // 取"弧线中点"＝直线中点 + 垂直方向 × 0.5×弧高 —— 和它 AddDuel 的半弧终点完全同一个位置。
        private static float _arcFactor = 0.30f;
        private static float _arcMin = 48f;
        private static float _arcMax = 300f;
        private static FieldInfo _canvasField;

        // 补画上下文：告诉 TryGetSlot "现在正在给哪个角色的第几个槽取起点"。
        // （补普通箭头时只放一个角色；调可视化的 AddDuel 画主对抗时同时放双方。）
        private static bool _callingOriginal;
        private static readonly Dictionary<BattleRole, int> CtxSlots = new Dictionary<BattleRole, int>();

        internal static bool Available
        {
            get { return _available; }
        }

        /// <summary>插件 Update 每帧调；找到可视化模组就挂补丁（最多每 2 秒重试一次）。</summary>
        internal static void Tick()
        {
            if (_available)
            {
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (_tried && now < _nextRetry)
            {
                return;
            }
            _tried = true;
            _nextRetry = now + 2f;
            try
            {
                Type type = FindType("AttackTargetVisualizer.ArrowLayer");
                if (type == null)
                {
                    return;   // 没装可视化：整层不激活
                }
                _mAddNormal = AccessTools.Method(type, "AddNormal");
                _mCollect = AccessTools.Method(type, "Collect");
                _mTryGetSlot = AccessTools.Method(type, "TryGetSlot");
                _mIsSingleHostile = AccessTools.Method(type, "IsSingleHostile");
                _mAddArrow = AccessTools.Method(type, "AddArrow");
                _mAddDuel = AccessTools.Method(type, "AddDuel");
                _mToLocal = AccessTools.Method(type, "ToLocal");
                _specsField = AccessTools.Field(type, "_specs");
                _canvasField = AccessTools.Field(type, "Canvas");
                _arrowSpecType = FindType("AttackTargetVisualizer.ArrowSpec");
                if (_mAddNormal == null || _mCollect == null || _mTryGetSlot == null ||
                    _mIsSingleHostile == null || _mToLocal == null || _mAddArrow == null || _mAddDuel == null ||
                    _specsField == null || _arrowSpecType == null)
                {
                    _nextRetry = float.MaxValue;   // 结构对不上：重试也没用
                    DuoActionSlotPlugin.LogError(
                        "可视化攻击目标的方法签名和预期对不上，多槽箭头兼容层未生效（箭头会退回单条），游戏不受影响。");
                    return;
                }
                _fFrom = _arrowSpecType.GetField("From");
                _fTo = _arrowSpecType.GetField("To");
                _fColor = _arrowSpecType.GetField("Color");
                _fDashed = _arrowSpecType.GetField("Dashed");
                _fPS0 = _arrowSpecType.GetField("PS0");
                _fPS1 = _arrowSpecType.GetField("PS1");
                _fHead = _arrowSpecType.GetField("Head");
                if (_fFrom == null || _fTo == null || _fColor == null || _fDashed == null ||
                    _fPS0 == null || _fPS1 == null || _fHead == null)
                {
                    _nextRetry = float.MaxValue;
                    DuoActionSlotPlugin.LogError("ArrowSpec 字段对不上，多槽对抗黄线兼容层未生效（其它功能不受影响）。");
                    return;
                }
                ReadClashColor();
                ReadArrowStyleNumbers();
                _harmony = new Harmony("codex.depersonal.duoactionslot.arrowcompat");
                _harmony.Patch(_mAddNormal,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PatchArrowCompat), "Prefix_AddNormal")));
                _harmony.Patch(_mCollect,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PatchArrowCompat), "Prefix_Collect")),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(PatchArrowCompat), "Postfix_Collect")));
                _harmony.Patch(_mTryGetSlot,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PatchArrowCompat), "Prefix_TryGetSlot")));
                _harmony.Patch(_mIsSingleHostile,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(PatchArrowCompat), "Prefix_IsSingleHostile")));
                _available = true;
                DuoActionSlotPlugin.LogInfo(
                    "已接管可视化模组的箭头绘制：多槽角色每槽一条；对抗（互指）时每个槽各出一条黄线、在双方中间汇集。");
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("箭头兼容层初始化失败：" + e.Message);
            }
        }

        private static Type FindType(string fullName)
        {
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    try
                    {
                        Type type = assemblies[i].GetType(fullName, false);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static void ReadClashColor()
        {
            try
            {
                Type style = FindType("AttackTargetVisualizer.ArrowStyle");
                if (style == null)
                {
                    return;
                }
                FieldInfo field = style.GetField("Clash",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    _clashColor = (Color)field.GetValue(null);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>把可视化画弧线用的 ArcFactor / ArcMin / ArcMax 读过来（读不到就用当前默认值）。</summary>
        private static void ReadArrowStyleNumbers()
        {
            try
            {
                Type style = FindType("AttackTargetVisualizer.ArrowStyle");
                if (style == null)
                {
                    return;
                }
                FieldInfo f = style.GetField("ArcFactor",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    _arcFactor = Convert.ToSingle(f.GetValue(null));
                }
                f = style.GetField("ArcMin",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    _arcMin = Convert.ToSingle(f.GetValue(null));
                }
                f = style.GetField("ArcMax",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    _arcMax = Convert.ToSingle(f.GetValue(null));
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>箭头画布的缩放系数（屏幕像素 ↔ 画布单位），用来把弧高从画布单位换算到屏幕像素。</summary>
        private static float GetCanvasScale(object layer)
        {
            try
            {
                Canvas canvas = _canvasField != null ? _canvasField.GetValue(layer) as Canvas : null;
                if (canvas != null && canvas.scaleFactor > 0f)
                {
                    return canvas.scaleFactor;
                }
            }
            catch (Exception)
            {
            }
            return 1f;
        }

        // ---------------- Harmony 补丁 ----------------

        /// <summary>多槽角色（或被我们接管黄线的角色）：不画它自己的单条箭头。</summary>
        private static bool Prefix_AddNormal(BattleRole src)
        {
            try
            {
                if (!_available || _callingOriginal || src == null)
                {
                    return true;
                }
                if (Handled.Contains(src))
                {
                    return false;
                }
                if (SlotManager.IsMultiSlot(src))
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("箭头兼容（AddNormal）出错：" + e.Message);
            }
            return true;
        }

        /// <summary>收集之前：预处理"多槽参与的对抗对"和本轮由我们画线的角色。</summary>
        private static void Prefix_Collect()
        {
            try
            {
                if (!_available)
                {
                    return;
                }
                DuelPairs.Clear();
                Handled.Clear();
                List<BattleRole> roles = CollectRoles();
                for (int i = 0; i < roles.Count; i++)
                {
                    BattleRole a = roles[i];
                    if (a == null || Handled.Contains(a))
                    {
                        continue;
                    }
                    bool aMulti = SlotManager.IsMultiSlot(a);
                    BattleRole b = CommonHostileSingleTarget(a);
                    if (b == null || b == a || Handled.Contains(b))
                    {
                        continue;
                    }
                    bool bMulti = SlotManager.IsMultiSlot(b);
                    if (!aMulti && !bMulti)
                    {
                        continue;   // 纯单槽对：交给可视化模组自己画
                    }
                    if (CommonHostileSingleTarget(b) != a)
                    {
                        continue;
                    }
                    DuelPair pair = new DuelPair();
                    pair.A = a;
                    pair.B = b;
                    DuelPairs.Add(pair);
                    Handled.Add(a);
                    Handled.Add(b);
                    DuoActionSlotPlugin.LogInfo(string.Format("对抗汇集：{0} ⇄ {1}（每槽一条黄线，中间汇集）",
                        SlotManager.SafeName(a), SlotManager.SafeName(b)));
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("箭头兼容（对抗预判）出错：" + e.Message);
            }
        }

        /// <summary>收集之后：多槽角色逐槽补普通箭头；对抗对自绘黄线。</summary>
        private static void Postfix_Collect(object __instance)
        {
            try
            {
                if (!_available || __instance == null)
                {
                    return;
                }
                List<BattleRole> roles = CollectRoles();
                for (int i = 0; i < roles.Count; i++)
                {
                    BattleRole role = roles[i];
                    if (role == null || !SlotManager.IsMultiSlot(role))
                    {
                        continue;
                    }
                    AppendSlotArrows(__instance, role);
                }
                for (int i = 0; i < DuelPairs.Count; i++)
                {
                    DrawDuel(__instance, DuelPairs[i]);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("箭头兼容（补画）出错：" + e.Message);
            }
        }

        /// <summary>正在补画某个源角色的槽时，把"起点"换成该槽节点的顶边中点。</summary>
        private static bool Prefix_TryGetSlot(BattleRole role, bool topEdge, ref Vector2 screen, ref bool __result)
        {
            try
            {
                if (!_available || role == null || CtxSlots.Count == 0)
                {
                    return true;
                }
                int slot;
                if (!CtxSlots.TryGetValue(role, out slot) || slot < 0)
                {
                    return true;
                }
                if (TryGetSlotTopCenterScreen(role, slot, out screen))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>多槽角色不参与可视化模组自己的互指配对（黄线由我们画）。</summary>
        private static bool Prefix_IsSingleHostile(BattleRole r, ref bool __result)
        {
            try
            {
                if (!_available)
                {
                    return true;
                }
                if (r != null && SlotManager.IsMultiSlot(r))
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception)
            {
            }
            return true;
        }

        // ---------------- 补画逻辑 ----------------

        /// <summary>
        /// 多槽角色：逐个槽调用它的原 AddNormal（切换指针 + 起点上下文）。
        /// 指向"对抗对手"的槽跳过——那些槽由黄线负责，不能画成红/蓝。
        /// </summary>
        private static void AppendSlotArrows(object layer, BattleRole role)
        {
            SlotState state = SlotManager.Peek(role);
            if (state == null)
            {
                return;
            }
            BattleRole opponent = GetDuelOpponent(role);
            BattleActiveBehaviorData original = role.CurrentBehaviourData;
            try
            {
                for (int i = 0; i < state.SlotCount; i++)
                {
                    BattleActiveBehaviorData data = state.GetData(i);
                    if (data == null || SlotManager.IsEmptyAction(data))
                    {
                        continue;
                    }
                    if (opponent != null && SlotTargetsRole(role, i, opponent))
                    {
                        continue;   // 指向对抗目标的槽：黄线负责
                    }
                    role.CurrentBehaviourData = data;
                    CtxSlots.Clear();
                    CtxSlots[role] = i;
                    _callingOriginal = true;
                    try
                    {
                        _mAddNormal.Invoke(layer, new object[] { role });
                    }
                    catch (Exception e)
                    {
                        DuoActionSlotPlugin.LogError("补画行动槽箭头失败：" + e.Message);
                    }
                    finally
                    {
                        _callingOriginal = false;
                        CtxSlots.Clear();
                    }
                }
            }
            finally
            {
                role.CurrentBehaviourData = original;
            }
        }

        /// <summary>
        /// 对抗对：完全照可视化模组单槽对抗的算法——
        /// 以"从左往右第一个和该目标对抗的槽"为基准点，直接调它的 AddDuel 画那条标准对抗弧
        /// （两个半弧、箭头头在弧线中点相撞）；同一方其他"也指向对方"的槽，
        /// 只是各自拉一条线汇到同一个对抗点（不带头）——也就是"1 个对抗 + 其他槽的线"。
        /// </summary>
        private static void DrawDuel(object layer, DuelPair pair)
        {
            int idxA = FirstSlotTargeting(pair.A, pair.B);
            int idxB = FirstSlotTargeting(pair.B, pair.A);
            if (idxA < 0 || idxB < 0)
            {
                return;
            }
            Vector2 pa;
            Vector2 pb;
            if (!TryGetSlotTopCenterScreen(pair.A, idxA, out pa) ||
                !TryGetSlotTopCenterScreen(pair.B, idxB, out pb))
            {
                return;
            }
            if (Vector2.Distance(pa, pb) < 4f)
            {
                return;
            }
            float arc;
            float lift;
            Vector2 mid = ArcMidpoint(layer, pa, pb, out arc, out lift);
            LogDuel(pair, idxA, idxB, pa, pb, arc, lift);
            // 1) 主对抗：直接调可视化的 AddDuel（它自己画两条半弧 + 头），
            //    通过 CtxSlots 让它取"第一个对抗槽"的位置——算法和它单槽对抗一模一样。
            CtxSlots.Clear();
            CtxSlots[pair.A] = idxA;
            CtxSlots[pair.B] = idxB;
            try
            {
                _mAddDuel.Invoke(layer, new object[] { pair.A, pair.B });
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("调用可视化对抗绘制失败：" + e.Message);
            }
            finally
            {
                CtxSlots.Clear();
            }
            // 2) 其他也指向对方的槽：各自一条线汇到对抗点（不带头）。
            DrawExtraSlotsTo(layer, pair.A, pair.B, mid, idxA);
            DrawExtraSlotsTo(layer, pair.B, pair.A, mid, idxB);
        }

        /// <summary>对抗点＝可视化那条弧线的中点：直线中点 + 垂直方向 × 0.5×弧高。</summary>
        private static Vector2 ArcMidpoint(object layer, Vector2 pa, Vector2 pb, out float arc, out float lift)
        {
            Vector2 d = pb - pa;
            Vector2 perp = new Vector2(-d.y, d.x);
            if (perp.y < 0f)
            {
                perp = -perp;
            }
            perp = perp.sqrMagnitude > 1e-6f ? perp.normalized : Vector2.up;
            float scale = GetCanvasScale(layer);
            float distLocal = Vector2.Distance(pa, pb) / Mathf.Max(scale, 0.0001f);
            arc = Mathf.Clamp(distLocal * _arcFactor, _arcMin, _arcMax);
            lift = 0.5f * arc * scale;
            return (pa + pb) * 0.5f + perp * lift;
        }

        /// <summary>同一方其他"指向对抗目标"的槽：各拉一条线到对抗点（不带头，并入主对抗）。</summary>
        private static void DrawExtraSlotsTo(object layer, BattleRole role, BattleRole opponent,
                                             Vector2 mid, int skipIndex)
        {
            int count = ExtraActionSlotApi.GetSlotCount(role);
            for (int i = 0; i < count; i++)
            {
                if (i == skipIndex || !SlotTargetsRole(role, i, opponent))
                {
                    continue;
                }
                Vector2 fromScreen;
                if (!TryGetSlotTopCenterScreen(role, i, out fromScreen))
                {
                    continue;
                }
                try
                {
                    _mAddArrow.Invoke(layer, new object[] { fromScreen, mid, _clashColor, false, false });
                }
                catch (Exception e)
                {
                    DuoActionSlotPlugin.LogError("画对抗支线失败：" + e.Message);
                }
            }
        }

        /// <summary>从左往右第一个"就绪、单一敌对、锁定 target"的槽；没有返回 -1。</summary>
        private static int FirstSlotTargeting(BattleRole role, BattleRole target)
        {
            try
            {
                int count = ExtraActionSlotApi.GetSlotCount(role);
                for (int i = 0; i < count; i++)
                {
                    if (SlotTargetsRole(role, i, target))
                    {
                        return i;
                    }
                }
            }
            catch (Exception)
            {
            }
            return -1;
        }

        /// <summary>这个槽是不是"就绪、单一敌对、且锁定 target"。</summary>
        private static bool SlotTargetsRole(BattleRole role, int index, BattleRole target)
        {
            try
            {
                if (!IsSlotReadyForArrow(role, index))
                {
                    return false;
                }
                BattleActiveBehaviorData data = ExtraActionSlotApi.GetSlotData(role, index);
                return data != null && UniqueTarget(data) == target;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>这个角色在本轮对抗对里的对手（不在对里返回 null）。</summary>
        private static BattleRole GetDuelOpponent(BattleRole role)
        {
            try
            {
                for (int i = 0; i < DuelPairs.Count; i++)
                {
                    if (DuelPairs[i].A == role)
                    {
                        return DuelPairs[i].B;
                    }
                    if (DuelPairs[i].B == role)
                    {
                        return DuelPairs[i].A;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>对抗线细节日志（内容变化时才打一条，方便排查基准点与高度）。</summary>
        private static void LogDuel(DuelPair pair, int idxA, int idxB, Vector2 pa, Vector2 pb,
                                    float arc, float lift)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("对抗线细节：基准槽 ")
                  .Append(SlotManager.SafeName(pair.A)).Append(" 第").Append(idxA + 1).Append("槽=(")
                  .Append((int)pa.x).Append(',').Append((int)pa.y).Append(") ⇄ ")
                  .Append(SlotManager.SafeName(pair.B)).Append(" 第").Append(idxB + 1).Append("槽=(")
                  .Append((int)pb.x).Append(',').Append((int)pb.y).Append(") 弧高=")
                  .Append((int)arc).Append(" 抬升=").Append((int)lift);
                string text = sb.ToString();
                if (text != _lastDuelLog)
                {
                    _lastDuelLog = text;
                    DuoActionSlotPlugin.LogInfo(text);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>槽的起点：优先用多行动槽的槽节点；单槽（没接管）回退到角色槽的顶边中点。</summary>
        private static bool TryGetSlotTopCenterScreen(BattleRole role, int index, out Vector2 screen)
        {
            screen = Vector2.zero;
            try
            {
                RectTransform anchor = ExtraActionSlotApi.GetSlotAnchor(role, index);
                if (anchor != null)
                {
                    screen = TopCenterScreen(anchor, GetHudCamera(role));
                    return true;
                }
                if (role == null || role.Model == null || role.Model.Hud == null)
                {
                    return false;
                }
                HUDElement hud = role.Model.Hud;
                Transform target = null;
                if (hud.Anim_SkillShow != null && hud.Anim_SkillShow.gameObject.activeInHierarchy)
                {
                    target = hud.Anim_SkillShow.transform;
                }
                else if (hud.Trans_SkillShow != null)
                {
                    target = hud.Trans_SkillShow;
                }
                RectTransform rect = target as RectTransform;
                if (rect == null)
                {
                    return false;
                }
                screen = TopCenterScreen(rect, GetHudCamera(role));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool ToLocal(object layer, Vector2 screen, out Vector2 local)
        {
            local = Vector2.zero;
            try
            {
                object[] args = new object[] { screen, Vector2.zero };
                bool ok = (bool)_mToLocal.Invoke(layer, args);
                local = (Vector2)args[1];
                return ok;
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("箭头坐标换算失败：" + e.Message);
                return false;
            }
        }

        private static void AddSpec(object layer, Vector2 from, Vector2 to, float ps0, float ps1, bool head)
        {
            try
            {
                object spec = Activator.CreateInstance(_arrowSpecType);
                _fFrom.SetValue(spec, from);
                _fTo.SetValue(spec, to);
                _fColor.SetValue(spec, _clashColor);
                _fDashed.SetValue(spec, false);
                _fPS0.SetValue(spec, ps0);
                _fPS1.SetValue(spec, ps1);
                _fHead.SetValue(spec, head);
                System.Collections.IList specs = _specsField.GetValue(layer) as System.Collections.IList;
                if (specs != null)
                {
                    specs.Add(spec);
                }
                string log = string.Format("加黄线 from=({0:F0},{1:F0}) to=({2:F0},{3:F0}) PS={4:F1}->{5:F1} head={6} 列表={7}",
                    from.x, from.y, to.x, to.y, ps0, ps1, head, specs != null ? specs.Count : -1);
                if (log != _lastSpecLog)
                {
                    _lastSpecLog = log;
                    DuoActionSlotPlugin.LogInfo(log);
                }
            }
            catch (Exception e)
            {
                DuoActionSlotPlugin.LogError("画对抗黄线失败：" + e.Message);
            }
        }

        private static bool IsSlotReadyForArrow(BattleRole role, int index)
        {
            try
            {
                BattleActiveBehaviorData data = ExtraActionSlotApi.GetSlotData(role, index);
                if (data == null || data.ActionFinish)
                {
                    return false;
                }
                if (data.BattleSkillData == null && data.MagicData == null)
                {
                    return false;
                }
                return data.TargetSelectType == ETargetSelect.Enemy_Single;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 这个角色"所有已就绪的槽"是不是都指向同一个敌对单体（空槽忽略）；
        /// 是就返回那个目标，否则 null。
        /// </summary>
        private static BattleRole CommonHostileSingleTarget(BattleRole role)
        {
            try
            {
                if (role == null || role.Data == null || role.IsDeath || role.IsUnableAct)
                {
                    return null;
                }
                int count = ExtraActionSlotApi.GetSlotCount(role);
                BattleRole common = null;
                bool any = false;
                for (int i = 0; i < count; i++)
                {
                    BattleActiveBehaviorData data = ExtraActionSlotApi.GetSlotData(role, i);
                    if (data == null || data.ActionFinish)
                    {
                        continue;
                    }
                    if (data.BattleSkillData == null && data.MagicData == null)
                    {
                        continue;
                    }
                    if (data.TargetSelectType != ETargetSelect.Enemy_Single)
                    {
                        return null;
                    }
                    BattleRole target = UniqueTarget(data);
                    if (target == null)
                    {
                        return null;
                    }
                    any = true;
                    if (common == null)
                    {
                        common = target;
                    }
                    else if (common != target)
                    {
                        return null;
                    }
                }
                return any ? common : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static BattleRole UniqueTarget(BattleActiveBehaviorData data)
        {
            if (data == null)
            {
                return null;
            }
            List<BattleRole> list = data.Targets;
            BattleRole found = null;
            for (int i = 0; i < list.Count; i++)
            {
                BattleRole t = list[i];
                if (t == null || t.IsDeath)
                {
                    continue;
                }
                if (found == null)
                {
                    found = t;
                }
                else if (found != t)
                {
                    return null;
                }
            }
            return found;
        }

        // ---------------- 坐标 / 工具 ----------------

        private static List<BattleRole> CollectRoles()
        {
            List<BattleRole> roles = new List<BattleRole>();
            try
            {
                BattleFightContent fc = BattleHelper.FightContent;
                if (fc == null)
                {
                    return roles;
                }
                if (fc.Allies != null)
                {
                    roles.AddRange(fc.Allies);
                }
                if (fc.CurWaveEnemies != null)
                {
                    for (int i = 0; i < fc.CurWaveEnemies.Count; i++)
                    {
                        BattleRole role = fc.CurWaveEnemies[i];
                        if (role != null)
                        {
                            roles.Add(role);
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return roles;
        }

        private static bool TryGetRoleSlotCenter(BattleRole role, out Vector2 screen)
        {
            screen = Vector2.zero;
            try
            {
                if (role == null || role.Model == null || role.Model.Hud == null)
                {
                    return false;
                }
                Transform slotRoot = role.Model.Hud.Trans_SkillShow;
                if (slotRoot == null)
                {
                    return false;
                }
                Camera cam = GetHudCamera(role);
                screen = RectTransformUtility.WorldToScreenPoint(cam, slotRoot.position);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Camera GetHudCamera(BattleRole role)
        {
            try
            {
                if (role != null && role.Model != null && role.Model.Hud != null)
                {
                    Canvas canvas = role.Model.Hud.GetComponent<Canvas>();
                    if (canvas != null)
                    {
                        return canvas.worldCamera;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static Vector2 TopCenterScreen(RectTransform rect, Camera cam)
        {
            try
            {
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);   // 0 左下 / 1 左上 / 2 右上 / 3 右下
                Vector3 world = (corners[1] + corners[2]) * 0.5f;
                return RectTransformUtility.WorldToScreenPoint(cam, world);
            }
            catch (Exception)
            {
                return Vector2.zero;
            }
        }
    }
}
