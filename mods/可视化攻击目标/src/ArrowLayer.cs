using System.Collections.Generic;
using System.Text;
using Game;
using Game.SkillData;
using UnityEngine;

namespace AttackTargetVisualizer
{
    /// <summary>
    /// 挂在箭头画布上：每帧从战斗数据算出"该画哪些箭头"，交给 <see cref="ArrowGraphic"/> 画。
    ///
    /// 规则（v0.1）：
    ///   · 打敌人＝红，打队友＝蓝；单体＝实线，范围/随机＝虚线；
    ///   · 目标是自己的不画；
    ///   · 双方互为目标且都是敌方单体 → 换成两个黄色箭头，头对头撞在中点；
    ///   · 多条箭头指向同一目标 → 共用终点，只画一个箭头头；
    ///   · 只在"选行动"阶段（EFightPhase.ActionStage）显示，一进入出手演出（ResultStage）就全清。
    /// </summary>
    internal class ArrowLayer : MonoBehaviour
    {
        internal ArrowGraphic Graphic;
        internal Canvas Canvas;

        private readonly List<ArrowSpec> _specs = new List<ArrowSpec>();
        private readonly List<BattleRole> _roles = new List<BattleRole>();
        private readonly List<BattleRole> _targets = new List<BattleRole>();
        private readonly HashSet<BattleRole> _headDone = new HashSet<BattleRole>();
        private readonly HashSet<BattleRole> _duelled = new HashSet<BattleRole>();
        private readonly StringBuilder _desc = new StringBuilder();

        private float _dashPhase;
        private bool _errorLogged;
        private bool _loggedThisRound;
        private float _roundTimer;
        private bool _mirrored;
        private bool _selfChecked;

        private void Update()
        {
            if (Graphic == null)
            {
                return;
            }

            _specs.Clear();
            _headDone.Clear();
            _duelled.Clear();
            _desc.Length = 0;

            bool drawing = ShouldDraw();

            if (drawing)
            {
                MirrorFromHudIfNeeded();
                try
                {
                    Collect();
                }
                catch (System.Exception e)
                {
                    if (!_errorLogged)
                    {
                        _errorLogged = true;
                        AttackTargetPlugin.Log.LogError("计算箭头时出错：" + e);
                    }
                }
            }
            else
            {
                // 离开选行动阶段（＝开始出手，或战斗结束）→ 下一轮重新统计
                _loggedThisRound = false;
                _roundTimer = 0f;
            }

            LogRoundState(drawing);

            Graphic.DashPhase = _dashPhase;
            Graphic.SetArrows(_specs);

            if (_specs.Count > 0)
            {
                _dashPhase += Time.unscaledDeltaTime * ArrowStyle.DashSpeed;
                if (_dashPhase > 100000f)
                {
                    _dashPhase = 0f;
                }
            }
        }

        /// <summary>
        /// 相对行动槽牌子往上抬多少层。
        /// 抬上去以后箭头压在牌子、HP 条、buff 图标这些战斗 UI 之上，不会再被遮挡。
        /// </summary>
        private const int SortingLift = 10;

        /// <summary>
        /// 把自己这层画布对齐到游戏行动槽牌子的画布上。
        ///
        /// 为什么要抄：行动槽牌子是游戏自己的预制体，它的 GameObject 层级（layer）、
        /// 渲染相机、排序层、plane distance 都是配好的；我们 new 出来的节点默认在第 0 层，
        /// 而 UI 相机的 culling mask 很可能不包含第 0 层 —— 结果就是"建出来了但看不见"。
        /// 抄一份最省事，也不用去猜游戏的层名。
        /// </summary>
        private void MirrorFromHudIfNeeded()
        {
            if (_mirrored || Canvas == null)
            {
                return;
            }

            HUDElement sample = FindSampleHud();
            if (sample == null)
            {
                return;
            }
            Canvas sc = sample.GetComponent<Canvas>();
            if (sc == null)
            {
                return;
            }

            SetLayerRecursive(gameObject, sample.gameObject.layer);

            Canvas.renderMode = sc.renderMode;
            Canvas.worldCamera = sc.worldCamera;
            Canvas.planeDistance = sc.planeDistance;
            Canvas.overrideSorting = sc.overrideSorting;
            Canvas.sortingLayerID = sc.sortingLayerID;
            Canvas.sortingOrder = sc.sortingOrder + SortingLift;   // 压在牌子上面
            Canvas.pixelPerfect = sc.pixelPerfect;

            _mirrored = true;
            AttackTargetPlugin.Log.LogInfo(string.Format(
                "已对齐行动槽画布：layer={0}({1}) renderMode={2} sortingLayer={3}({4}) order={5}→{6} planeDistance={7} overrideSorting={8}",
                sample.gameObject.layer, LayerMask.LayerToName(sample.gameObject.layer),
                sc.renderMode, sc.sortingLayerName, sc.sortingLayerID,
                sc.sortingOrder, Canvas.sortingOrder, sc.planeDistance, sc.overrideSorting));

            LogOtherCanvasOrders();

            if (!_selfChecked)
            {
                _selfChecked = true;
                AttackTargetPlugin.Log.LogInfo(string.Format(
                    "自我检查：graphic.canvas={0} material={1} graphicActive={2} canvasActive={3} 本层layer={4}({5})",
                    Graphic != null && Graphic.canvas != null ? Graphic.canvas.name : "null",
                    Graphic != null && Graphic.material != null ? Graphic.material.name : "null",
                    Graphic != null && Graphic.isActiveAndEnabled,
                    Canvas.isActiveAndEnabled,
                    gameObject.layer, LayerMask.LayerToName(gameObject.layer)));
            }
        }

        private static HUDElement FindSampleHud()
        {
            BattleFightContent fc = BattleHelper.FightContent;
            if (fc == null)
            {
                return null;
            }
            if (fc.Allies != null)
            {
                for (int i = 0; i < fc.Allies.Count; i++)
                {
                    HUDElement hud = TryGetHud(fc.Allies[i]);
                    if (hud != null && hud.Trans_Battle != null && hud.Trans_Battle.gameObject.activeInHierarchy)
                    {
                        return hud;
                    }
                }
            }
            if (fc.CurWaveEnemies != null)
            {
                for (int i = 0; i < fc.CurWaveEnemies.Count; i++)
                {
                    HUDElement hud = TryGetHud(fc.CurWaveEnemies[i]);
                    if (hud != null && hud.Trans_Battle != null && hud.Trans_Battle.gameObject.activeInHierarchy)
                    {
                        return hud;
                    }
                }
            }
            return null;
        }

        private static HUDElement TryGetHud(BattleRole role)
        {
            if (role == null || role.Model == null)
            {
                return null;
            }
            return role.Model.Hud;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
            }
        }

        /// <summary>把相关界面的排序值打进日志，方便判断箭头还要不要再往上抬。</summary>
        private static void LogOtherCanvasOrders()
        {
            StringBuilder sb = new StringBuilder();
            if (PrefabSingleton<UIBattlePanel>.HasInstance)
            {
                AppendCanvasOrder(sb, "战斗面板", PrefabSingleton<UIBattlePanel>.Instance.gameObject);
            }
            if (PrefabSingleton<UIRoot>.HasInstance)
            {
                AppendCanvasOrder(sb, "UI根", PrefabSingleton<UIRoot>.Instance.Canvas);
            }
            AttackTargetPlugin.Log.LogInfo("相关界面排序值：" + sb.ToString().TrimEnd());
        }

        private static void AppendCanvasOrder(StringBuilder sb, string label, GameObject go)
        {
            if (go == null)
            {
                return;
            }
            Canvas c = go.GetComponentInParent<Canvas>();
            if (c == null)
            {
                c = go.GetComponentInChildren<Canvas>();
            }
            sb.Append(label).Append('=');
            if (c == null)
            {
                sb.Append("无画布 ");
                return;
            }
            sb.Append(c.sortingLayerName).Append('/').Append(c.sortingOrder)
              .Append("(层").Append(c.gameObject.layer).Append(") ");
        }

        /// <summary>刚开打时往日志里写一条统计，方便"一条箭头都没有"的时候定位。</summary>
        private void LogRoundState(bool drawing)
        {
            if (!drawing || _loggedThisRound)
            {
                return;
            }
            _roundTimer += Time.unscaledDeltaTime;
            if (_specs.Count == 0 && _roundTimer < 3f)
            {
                return;   // 还没确定任何行动，先不刷日志
            }
            _loggedThisRound = true;
            AttackTargetPlugin.Log.LogInfo(
                "选行动阶段：参战角色 " + _roles.Count + " 个，箭头 " + _specs.Count + " 条");
            if (_specs.Count > 0)
            {
                AttackTargetPlugin.Log.LogInfo("箭头明细：" + _desc.ToString().TrimEnd());
                AttackTargetPlugin.Log.LogInfo("落点：" + DescribeAnchors());
            }

            if (Canvas != null && Graphic != null)
            {
                RectTransform canvasRt = Canvas.GetComponent<RectTransform>();
                AttackTargetPlugin.Log.LogInfo(string.Format(
                    "画布尺寸：canvasRect={0} graphicRect={1} scaleFactor={2:F2} worldCam={3}",
                    canvasRt != null ? canvasRt.rect.size.ToString() : "null",
                    Graphic.rectTransform != null ? Graphic.rectTransform.rect.size.ToString() : "null",
                    Canvas.scaleFactor,
                    Canvas.worldCamera != null ? Canvas.worldCamera.name : "null"));
            }

            if (_specs.Count > 0)
            {
                ArrowSpec s = _specs[_specs.Count - 1];
                AttackTargetPlugin.Log.LogInfo(string.Format(
                    "末条箭头局部坐标：{0:F0},{1:F0} → {2:F0},{3:F0}（虚线={4} 带头={5}）",
                    s.From.x, s.From.y, s.To.x, s.To.y, s.Dashed, s.Head));
            }
        }

        private string DescribeAnchors()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _roles.Count; i++)
            {
                BattleRole r = _roles[i];
                if (r == null)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(Name(r)).Append('=').Append(AnchorKind(r));
            }
            return sb.ToString();
        }

        /// <summary>这个角色现在的落点取的是哪种牌子（排查落点问题用）。</summary>
        private static string AnchorKind(BattleRole role)
        {
            if (role == null || role.Model == null)
            {
                return "无模型";
            }
            HUDElement hud = role.Model.Hud;
            if (hud == null)
            {
                return "无HUD";
            }
            if (hud.Trans_SkillShow != null && hud.Trans_SkillShow.gameObject.activeInHierarchy)
            {
                return "技能牌子";
            }
            if (hud.Trans_EditorAction != null && hud.Trans_EditorAction.gameObject.activeInHierarchy)
            {
                return "编辑行动";
            }
            return "头顶";
        }

        private static string Name(BattleRole role)
        {
            if (role == null)
            {
                return "?";
            }
            if (role.Data != null && !string.IsNullOrEmpty(role.Data.Name))
            {
                return role.Data.Name;
            }
            return "角色" + role.GetHashCode();
        }

        /// <summary>什么时候该显示箭头。</summary>
        private static bool ShouldDraw()
        {
            if (!BattleHelper.IsInBattle)
            {
                return false;
            }
            BattleFightContent fc = BattleHelper.FightContent;
            if (fc == null || !fc.EnterRoomFinish)
            {
                return false;
            }
            // 选行动阶段才画；一进 ResultStage（开始出手）就自然全清了
            return fc.CurrentPhase == EFightPhase.ActionStage;
        }

        // ---------- 收集 ----------

        private void Collect()
        {
            BattleFightContent fc = BattleHelper.FightContent;

            _roles.Clear();
            if (fc.Allies != null)
            {
                _roles.AddRange(fc.Allies);
            }
            if (fc.CurWaveEnemies != null)
            {
                for (int i = 0; i < fc.CurWaveEnemies.Count; i++)
                {
                    if (fc.CurWaveEnemies[i] != null)
                    {
                        _roles.Add(fc.CurWaveEnemies[i]);
                    }
                }
            }

            // 1) 先挑出争锋相对的一对一对，配对成功的这轮不再画普通箭头
            for (int i = 0; i < _roles.Count; i++)
            {
                BattleRole a = _roles[i];
                if (_duelled.Contains(a) || !IsSingleHostile(a))
                {
                    continue;
                }
                BattleRole b = SingleTarget(a);
                if (b == null || _duelled.Contains(b) || !IsSingleHostile(b))
                {
                    continue;
                }
                if (SingleTarget(b) != a)
                {
                    continue;
                }
                _duelled.Add(a);
                _duelled.Add(b);
                AddDuel(a, b);
            }

            // 2) 剩下的按普通规则画
            for (int i = 0; i < _roles.Count; i++)
            {
                BattleRole r = _roles[i];
                if (r != null && !_duelled.Contains(r))
                {
                    AddNormal(r);
                }
            }

        }

        private void AddNormal(BattleRole src)
        {
            if (!HasAction(src))
            {
                return;
            }

            BattleActiveBehaviorData data = src.CurrentBehaviourData;
            ETargetSelect type = data.TargetSelectType;

            // 自身（以及没定目标）不画
            if (type == ETargetSelect.None || type == ETargetSelect.Self)
            {
                return;
            }

            _targets.Clear();
            bool anyHostile = false;
            for (int i = 0; i < data.Targets.Count; i++)
            {
                BattleRole t = data.Targets[i];
                if (t == null || t.IsDeath || t == src)
                {
                    continue;   // 打自己的那一段不算
                }
                if (_targets.Contains(t))
                {
                    continue;   // 多段打同一个目标只算一条
                }
                _targets.Add(t);
                if (t.IsAlly != src.IsAlly)
                {
                    anyHostile = true;
                }
            }
            if (_targets.Count == 0)
            {
                return;   // 全是自己 → 不画
            }

            Color color = PickColor(data, anyHostile);
            bool dashed = IsRange(type);

            Vector2 fromScreen;
            if (!TryGetSlot(src, false, out fromScreen))
            {
                return;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                BattleRole t = _targets[i];
                Vector2 toScreen;
                if (!TryGetSlot(t, true, out toScreen))
                {
                    continue;
                }
                // 同一个目标只在第一条箭头上画箭头头，后面的箭杆都收束到它
                bool head = _headDone.Add(t);
                AddArrow(fromScreen, toScreen, color, dashed, head);

                if (head)
                {
                    _desc.Append(Name(src)).Append('→').Append(Name(t))
                         .Append('(').Append(ColorName(color))
                         .Append(dashed ? "虚)" : "实)").Append(' ');
                }
            }
        }

        /// <summary>争锋相对：A、B 互为目标，两个黄箭头共用一条弧线，各画一半，头对头撞在中点。</summary>
        private void AddDuel(BattleRole a, BattleRole b)
        {
            Vector2 pa;
            Vector2 pb;
            if (!TryGetSlot(a, false, out pa) || !TryGetSlot(b, false, out pb))
            {
                return;
            }
            if (Vector2.Distance(pa, pb) < 4f)
            {
                return;
            }

            Vector2 from;
            Vector2 to;
            if (!ToLocal(pa, out from) || !ToLocal(pb, out to))
            {
                return;
            }

            ArrowSpec spec = new ArrowSpec
            {
                From = from,
                To = to,
                Color = ArrowStyle.Clash,
                Dashed = false,
                Head = true
            };

            spec.PS0 = 0f;
            spec.PS1 = 0.5f;
            _specs.Add(spec);

            spec.PS0 = 1f;
            spec.PS1 = 0.5f;
            _specs.Add(spec);

            _desc.Append(Name(a)).Append('⇄').Append(Name(b)).Append("(黄) ");
        }

        private void AddArrow(Vector2 fromScreen, Vector2 toScreen, Color color, bool dashed, bool head)
        {
            Vector2 from;
            Vector2 to;
            if (!ToLocal(fromScreen, out from) || !ToLocal(toScreen, out to))
            {
                return;
            }
            to.y += ArrowStyle.TargetGap;   // 落点抬到牌子上边框外面，免得箭头头被牌子挡住
            _specs.Add(new ArrowSpec
            {
                From = from,
                To = to,
                Color = color,
                Dashed = dashed,
                PS0 = 0f,
                PS1 = 1f,
                Head = head
            });
        }

        // ---------- 判定 ----------

        /// <summary>是不是"装好行动、可以画"的角色。</summary>
        private static bool HasAction(BattleRole r)
        {
            if (r == null || r.Data == null || r.IsDeath || r.IsUnableAct)
            {
                return false;
            }
            BattleActiveBehaviorData data = r.CurrentBehaviourData;
            if (data == null || data.ActionFinish)
            {
                return false;
            }
            return data.BattleSkillData != null || data.MagicData != null;
        }

        /// <summary>敌对单体指向，且只锁了一个目标。</summary>
        private static bool IsSingleHostile(BattleRole r)
        {
            if (!HasAction(r) || r.CurrentBehaviourData.TargetSelectType != ETargetSelect.Enemy_Single)
            {
                return false;
            }
            return SingleTarget(r) != null;
        }

        /// <summary>去重后只有一个目标就返回它，否则返回 null。</summary>
        private static BattleRole SingleTarget(BattleRole r)
        {
            List<BattleRole> list = r.CurrentBehaviourData.Targets;
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

        /// <summary>
        /// 原版「干扰」类行动（技艺、非伤害、指向敌人）→ 蓝色。
        /// 只登记游戏本体的技能 id；mod 新增的技能不走这张表，免得误判。
        /// 来源：游戏 StreamingAssets\Game\BattleSkill\*.txt 里 ActionType=技艺 且目标为敌人的技能。
        /// </summary>
        private static readonly HashSet<int> InterferenceSkillIds = new HashSet<int>
        {
            7, 11, 12, 14, 28, 33, 36, 39, 42, 1002, 1006, 1012
        };

        /// <summary>定颜色：干扰＝蓝，投掷＝红，其余按目标阵营（敌红／友绿）。</summary>
        private static Color PickColor(BattleActiveBehaviorData data, bool anyHostile)
        {
            if (IsThrowAction(data))
            {
                return ArrowStyle.Hostile;      // 投掷类固定红
            }
            BattleSkillData skill = data.BattleSkillData;
            if (skill != null && InterferenceSkillIds.Contains(skill.Id))
            {
                return ArrowStyle.Interference; // 干扰类固定蓝
            }
            return anyHostile ? ArrowStyle.Hostile : ArrowStyle.Friendly;
        }

        /// <summary>投掷类行动（行动类型是投掷，或技能配置里写的是投掷）。</summary>
        private static bool IsThrowAction(BattleActiveBehaviorData data)
        {
            if (data.ActionType == EBattleActionType.Throw)
            {
                return true;
            }
            BattleSkillData skill = data.BattleSkillData;
            return skill != null && skill.Config != null && skill.Config.ActionType == EBattleActionType.Throw;
        }

        private static string ColorName(Color c)
        {
            if (c == ArrowStyle.Hostile)
            {
                return "红";
            }
            if (c == ArrowStyle.Friendly)
            {
                return "绿";
            }
            if (c == ArrowStyle.Interference)
            {
                return "蓝";
            }
            return "黄";
        }

        /// <summary>范围类目标类型 → 虚线；其余单体类 → 实线。</summary>
        private static bool IsRange(ETargetSelect type)
        {
            switch (type)
            {
                case ETargetSelect.Enemy_All:
                case ETargetSelect.Enemy_Random:
                case ETargetSelect.Self_All:
                case ETargetSelect.Self_Random:
                case ETargetSelect.All:
                case ETargetSelect.All_Random:
                    return true;
                default:
                    return false;
            }
        }

        // ---------- 坐标 ----------

        /// <summary>
        /// 取角色"行动槽"的屏幕坐标。三级优先：
        ///   1) 技能牌子（技能图标＋先攻序号）——已经确定行动的角色；
        ///   2) 「编辑行动」牌子——还没选行动、正在被玩家编辑的那个角色，
        ///      游戏把这块牌子放在和行动槽一样的位置（见 HUDElement._UpdateRoleActionOverShow）；
        ///   3) 头顶——目标本身就没有牌子（还没轮到，或者已经无法行动）。
        ///
        /// <paramref name="topEdge"/>：取元素"上边框中点"而不是中心。
        /// 我们的箭头层排在牌子下面，扎在中心会被牌子挡住，所以落点用上边框。
        /// </summary>
        private static bool TryGetSlot(BattleRole role, bool topEdge, out Vector2 screen)
        {
            screen = Vector2.zero;
            if (role == null || role.Model == null)
            {
                return false;
            }

            HUDElement hud = role.Model.Hud;
            if (hud != null)
            {
                Canvas canvas = hud.GetComponent<Canvas>();
                Camera cam = canvas != null ? canvas.worldCamera : null;

                if (hud.Trans_SkillShow != null && hud.Trans_SkillShow.gameObject.activeInHierarchy)
                {
                    RectTransform anchor = hud.Trans_SkillShow as RectTransform;
                    if (hud.Image_ActionOrder != null && hud.Image_ActionOrder.gameObject.activeSelf)
                    {
                        anchor = hud.Image_ActionOrder.rectTransform;
                    }
                    else if (hud.Button_SkillIcon != null && hud.Button_SkillIcon.gameObject.activeInHierarchy)
                    {
                        anchor = hud.Button_SkillIcon.transform as RectTransform;
                    }
                    if (anchor == null)
                    {
                        anchor = hud.Trans_SkillShow as RectTransform;
                    }
                    if (anchor != null)
                    {
                        screen = topEdge ? TopCenterOf(anchor, cam) : ScreenOf(cam, anchor.position);
                        return true;
                    }
                }

                if (hud.Trans_EditorAction != null && hud.Trans_EditorAction.gameObject.activeInHierarchy)
                {
                    RectTransform ea = hud.Trans_EditorAction as RectTransform;
                    if (ea != null)
                    {
                        screen = topEdge ? TopCenterOf(ea, cam) : ScreenOf(cam, ea.position);
                        return true;
                    }
                }
            }

            // 退化：钉在头顶
            Camera battleCam = BattleHelper.BattleCam;
            if (battleCam == null)
            {
                return false;
            }
            Transform head = role.Model.Trans_Head != null
                ? role.Model.Trans_Head
                : (role.Model.Trans_Center != null ? role.Model.Trans_Center : role.Model.transform);
            Vector3 p = battleCam.WorldToScreenPoint(head.position);
            if (p.z <= 0f)
            {
                return false;
            }
            screen = new Vector2(p.x, p.y);
            return true;
        }

        private static Vector2 ScreenOf(Camera cam, Vector3 world)
        {
            Vector3 sp = RectTransformUtility.WorldToScreenPoint(cam, world);
            return new Vector2(sp.x, sp.y);
        }

        /// <summary>元素上边框的中点（世界坐标先取角点再转屏幕）。</summary>
        private static Vector2 TopCenterOf(RectTransform rt, Camera cam)
        {
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);   // 0 左下 / 1 左上 / 2 右上 / 3 右下
            return ScreenOf(cam, (corners[1] + corners[2]) * 0.5f);
        }

        private bool ToLocal(Vector2 screen, out Vector2 local)
        {
            local = Vector2.zero;
            if (Graphic == null)
            {
                return false;
            }
            Camera cam = Canvas != null ? Canvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(Graphic.rectTransform, screen, cam, out local);
        }
    }
}
