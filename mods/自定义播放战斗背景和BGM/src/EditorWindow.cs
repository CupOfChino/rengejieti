// 「编辑领域」的编辑窗。
//
// 左边是编辑区（命名 / 战斗背景 / 战斗BGM / 循环勾选 / 确认 / 取消），
// 右边挂一个小窗列出现有的领域配置，每行带删除。
// 界面是纯代码搭的，字体借用游戏 UI 的那套（照抄「自定义心」的做法）。

using System;
using System.Collections.Generic;
using System.IO;
using Game;
using Game.SkillData;
using MOD;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;

namespace CustomBattleBg
{
    internal class DomainEditorWindow : MonoBehaviour
    {
        private const float PanelWidth = 820f;
        private const float PanelHeight = 780f;
        private const float SideWidth = 400f;
        private const int MaxSideRows = 9;

        private static DomainEditorWindow _instance;

        private GameObject _canvasGo;
        private bool _navWasOn;
        private HeroRoleData _role;
        private DomainProfile _def;

        private Text _title;
        private Text _namePrefix;
        private InputField _suffix;
        private Button _bgBox;
        private Text _bgBoxText;
        private Button _bgmBox;
        private Text _bgmBoxText;
        private Button _loopBox;
        private Button _halfBox;
        private SearchDropdown _mountDropdown;
        private SearchDropdown _allyTargetDropdown;
        private Button _enemyBox;
        private SearchDropdown _allyEffectDropdown;
        private InputField _allyEffectValue;
        private SearchDropdown _enemyEffectDropdown;
        private InputField _enemyEffectValue;
        private SearchDropdown _costDropdown;
        private InputField _costValue;
        private Text _hint;
        private GameObject _maskGo;
        private GameObject _panelGo;
        private GameObject _sideGo;
        private GameObject _confirmGo;
        private Text _confirmText;
        private UnityAction _confirmYes;
        private UnityAction _confirmNo;
        private Transform _sideList;
        private readonly List<GameObject> _sideRows = new List<GameObject>();

        // 编辑中的值（点确认才写回）
        private string _bgFile = "";
        private string _bgmFile = "";
        private bool _bgmLoop = true;
        private bool _halfScreen = false;

        internal static bool IsOpen
        {
            get { return _instance != null && _instance._canvasGo != null && _instance._canvasGo.activeSelf; }
        }

        internal static void Open(HeroRoleData role)
        {
            try
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("DomainEditorCanvas");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<DomainEditorWindow>();
                    _instance.Build(go);
                }
                _instance._canvasGo.SetActive(true);
                _instance.SuppressNavigation();
                _instance.LoadFrom(role);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("打开编辑窗失败：" + e);
            }
        }

        internal static void CloseWindow()
        {
            if (_instance == null)
            {
                return;
            }
            _instance.RestoreNavigation();
            if (_instance._canvasGo != null)
            {
                _instance._canvasGo.SetActive(false);
            }
        }

        // 窗口开着时把 UGUI 的方向键导航也关掉：W/S/A/D 和方向键本来会被当成"上下左右"
        // 去切换选中的控件（幕间列表就是这么被按走的）。只关导航，鼠标和打字不受影响。
        private void SuppressNavigation()
        {
            try
            {
                EventSystem system = EventSystem.current;
                if (system == null)
                {
                    return;
                }
                if (system.sendNavigationEvents)
                {
                    _navWasOn = true;
                    system.sendNavigationEvents = false;
                }
            }
            catch (Exception)
            {
            }
        }

        private void RestoreNavigation()
        {
            if (!_navWasOn)
            {
                return;
            }
            try
            {
                EventSystem system = EventSystem.current;
                if (system != null)
                {
                    system.sendNavigationEvents = true;
                }
            }
            catch (Exception)
            {
            }
            _navWasOn = false;
        }

        private void Build(GameObject go)
        {
            _canvasGo = go;
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            go.AddComponent<GraphicRaycaster>();

            // 挡住底下的游戏界面
            Image mask = UIFactory.Panel(go.transform, "Mask", new Color(0f, 0f, 0f, 0.5f));
            UIFactory.Stretch(mask.rectTransform, 0f, 0f, 0f, 0f);
            _maskGo = mask.gameObject;

            float mainX = -(SideWidth + 24f) * 0.5f;
            float sideX = (PanelWidth + 24f) * 0.5f;

            Image panel = UIFactory.Panel(go.transform, "Panel", UIFactory.PanelColor);
            CenterPanel(panel.rectTransform, mainX, PanelWidth, PanelHeight);
            BuildMain(panel.transform);
            _panelGo = panel.gameObject;

            Image side = UIFactory.Panel(go.transform, "Side", UIFactory.PanelColor);
            CenterPanel(side.rectTransform, sideX, SideWidth, PanelHeight);
            BuildSide(side.transform);
            _sideGo = side.gameObject;

            BuildConfirmDialog(go);

            DomainText.LocalizeTree(go.transform);
        }

        private static void CenterPanel(RectTransform rt, float x, float width, float height)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, height);
        }

        private void BuildMain(Transform panel)
        {
            float y = 20f;

            _title = UIFactory.Label(panel, "Title", "编辑「领域」", 30, TextAnchor.MiddleLeft);
            UIFactory.Place(_title.rectTransform, 24f, y, PanelWidth - 48f, 42f);
            Button closeTop = UIFactory.TextButton(panel, "CloseTop", "×", CloseWindow, true, 30);
            UIFactory.Place(closeTop.GetComponent<RectTransform>(), PanelWidth - 72f, y + 2f, 48f, 44f);
            y += 56f;

            // 命名：角色名——[后缀]
            Text label1 = UIFactory.Label(panel, "Label_Name", "命名", 24, TextAnchor.MiddleLeft);
            UIFactory.Place(label1.rectTransform, 24f, y, 76f, 42f);
            _namePrefix = UIFactory.Label(panel, "NamePrefix", "某某——", 22, TextAnchor.MiddleLeft);
            UIFactory.Place(_namePrefix.rectTransform, 104f, y, 240f, 42f);
            _suffix = UIFactory.TextInput(panel, "Suffix", "后缀（必填）", false);
            UIFactory.Place(_suffix.GetComponent<RectTransform>(), 352f, y, PanelWidth - 376f, 42f);
            y += 60f;

            // 战斗背景
            Text label2 = UIFactory.Label(panel, "Label_Bg", "战斗背景", 24, TextAnchor.MiddleLeft);
            UIFactory.Place(label2.rectTransform, 24f, y, 120f, 42f);
            _bgBox = UIFactory.TextButton(panel, "BgBox", "", OnPickBackground, false, 20);
            UIFactory.Place(_bgBox.GetComponent<RectTransform>(), 148f, y, 390f, 42f);
            _bgBoxText = _bgBox.transform.Find("Text").GetComponent<Text>();
            _bgBoxText.alignment = TextAnchor.MiddleLeft;
            // 文件名太长就裁掉，别顶到按钮上（横向允许换行 + 纵向截断 = 视觉上单行截断）
            _bgBoxText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bgBoxText.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Stretch(_bgBoxText.rectTransform, 10f, 10f, 2f, 2f);
            Button bgClear = UIFactory.TextButton(panel, "BgClear", "清除", OnClearBackground, true, 16);
            UIFactory.Place(bgClear.GetComponent<RectTransform>(), 710f, y + 2f, 80f, 38f);
            Button bgLib = UIFactory.TextButton(panel, "BgLib", "资源库", OnPickBgFromLibrary, false, 16);
            UIFactory.Place(bgLib.GetComponent<RectTransform>(), 634f, y + 2f, 72f, 38f);
            Button bgPick = UIFactory.TextButton(panel, "BgPick", "选择文件", OnPickBackground, false, 16);
            UIFactory.Place(bgPick.GetComponent<RectTransform>(), 544f, y + 2f, 86f, 38f);
            y += 56f;

            // 战斗BGM
            Text label3 = UIFactory.Label(panel, "Label_Bgm", "战斗BGM", 24, TextAnchor.MiddleLeft);
            UIFactory.Place(label3.rectTransform, 24f, y, 120f, 42f);
            _bgmBox = UIFactory.TextButton(panel, "BgmBox", "", OnPickBgm, false, 20);
            UIFactory.Place(_bgmBox.GetComponent<RectTransform>(), 148f, y, 390f, 42f);
            _bgmBoxText = _bgmBox.transform.Find("Text").GetComponent<Text>();
            _bgmBoxText.alignment = TextAnchor.MiddleLeft;
            _bgmBoxText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bgmBoxText.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Stretch(_bgmBoxText.rectTransform, 10f, 10f, 2f, 2f);
            Button bgmClear = UIFactory.TextButton(panel, "BgmClear", "清除", OnClearBgm, true, 16);
            UIFactory.Place(bgmClear.GetComponent<RectTransform>(), 710f, y + 2f, 80f, 38f);
            Button bgmLib = UIFactory.TextButton(panel, "BgmLib", "资源库", OnPickBgmFromLibrary, false, 16);
            UIFactory.Place(bgmLib.GetComponent<RectTransform>(), 634f, y + 2f, 72f, 38f);
            Button bgmPick = UIFactory.TextButton(panel, "BgmPick", "选择文件", OnPickBgm, false, 16);
            UIFactory.Place(bgmPick.GetComponent<RectTransform>(), 544f, y + 2f, 86f, 38f);
            y += 52f;

            // 挂载行动：战斗中用了这个行动就展开领域（选项在打开时按角色重建）
            Text label4 = UIFactory.Label(panel, "Label_Mount", "挂载行动", 24, TextAnchor.MiddleLeft);
            UIFactory.Place(label4.rectTransform, 24f, y, 120f, 42f);
            List<ActionOption> mountOptions = new List<ActionOption>();
            mountOptions.Add(new ActionOption("（不挂载）", 0, 0));
            _mountDropdown = new SearchDropdown(panel, "MountDropdown", mountOptions, 148f, y, PanelWidth - 300f, 42f);
            y += 56f;

            _loopBox = UIFactory.Checkbox(panel, "LoopBox", 148f, y, 36f, OnToggleLoop);
            Text loopLabel = UIFactory.Label(panel, "LoopLabel", "循环播放", 20, TextAnchor.MiddleLeft);
            UIFactory.Place(loopLabel.rectTransform, 192f, y, 160f, 36f);
            y += 48f;

            _halfBox = UIFactory.Checkbox(panel, "HalfBox", 148f, y, 36f, OnToggleHalf);
            Text halfLabel = UIFactory.Label(panel, "HalfLabel", "只占上半屏（仅视频生效，下方保留原来的背景）", 18, TextAnchor.MiddleLeft);
            UIFactory.Place(halfLabel.rectTransform, 192f, y, PanelWidth - 216f, 36f);
            y += 48f;

            // ---- 领域效果（数据模块，2026-10-07）----
            Text effectTitle = UIFactory.Label(panel, "EffectTitle",
                "领域效果（自定义数据，出问题后果自负）", 22, TextAnchor.MiddleLeft);
            effectTitle.color = UIFactory.HintColor;
            UIFactory.Place(effectTitle.rectTransform, 24f, y, PanelWidth - 48f, 32f);
            y += 32f;

            Text labelAlly = UIFactory.Label(panel, "Label_AllyTarget", "作用于友方", 20, TextAnchor.MiddleLeft);
            UIFactory.Place(labelAlly.rectTransform, 24f, y, 120f, 40f);
            List<ActionOption> allyTargetOptions = new List<ActionOption>();
            allyTargetOptions.Add(new ActionOption("无", 0, 0));
            allyTargetOptions.Add(new ActionOption("自身", 1, 0));
            allyTargetOptions.Add(new ActionOption("所有友方", 2, 0));
            _allyTargetDropdown = new SearchDropdown(panel, "AllyTarget", allyTargetOptions, 148f, y, 140f, 40f);
            // 作用于敌方：文字在前、勾选框在后
            Text enemyLabel = UIFactory.Label(panel, "EnemyLabel", "作用于敌方", 20, TextAnchor.MiddleLeft);
            UIFactory.Place(enemyLabel.rectTransform, 320f, y, 120f, 40f);
            _enemyBox = UIFactory.Checkbox(panel, "EnemyBox", 444f, y, 36f, OnToggleEnemyTarget);
            y += 46f;

            Text labelAllyEff = UIFactory.Label(panel, "Label_AllyEffect", "友方效果", 20, TextAnchor.MiddleLeft);
            UIFactory.Place(labelAllyEff.rectTransform, 24f, y, 120f, 40f);
            _allyEffectDropdown = new SearchDropdown(panel, "AllyEffect", new List<ActionOption>(), 148f, y, 470f, 40f);
            _allyEffectValue = UIFactory.TextInput(panel, "AllyEffectValue", "0", false);
            UIFactory.Place(_allyEffectValue.GetComponent<RectTransform>(), 628f, y, 120f, 40f);
            y += 46f;

            Text labelEnemyEff = UIFactory.Label(panel, "Label_EnemyEffect", "敌方效果", 20, TextAnchor.MiddleLeft);
            UIFactory.Place(labelEnemyEff.rectTransform, 24f, y, 120f, 40f);
            _enemyEffectDropdown = new SearchDropdown(panel, "EnemyEffect", new List<ActionOption>(), 148f, y, 470f, 40f);
            _enemyEffectValue = UIFactory.TextInput(panel, "EnemyEffectValue", "0", false);
            UIFactory.Place(_enemyEffectValue.GetComponent<RectTransform>(), 628f, y, 120f, 40f);
            y += 46f;

            Text labelCost = UIFactory.Label(panel, "Label_Cost", "支付代价", 20, TextAnchor.MiddleLeft);
            UIFactory.Place(labelCost.rectTransform, 24f, y, 120f, 40f);
            List<ActionOption> costOptions = new List<ActionOption>();
            costOptions.Add(new ActionOption("无", 0, 0));
            costOptions.Add(new ActionOption("扣除生命值", 1, 0));
            costOptions.Add(new ActionOption("扣除精神值", 2, 0));
            costOptions.Add(new ActionOption("扣除魔法值", 3, 0));
            _costDropdown = new SearchDropdown(panel, "CostType", costOptions, 148f, y, 180f, 40f);
            _costValue = UIFactory.TextInput(panel, "CostValue", "0", false);
            UIFactory.Place(_costValue.GetComponent<RectTransform>(), 338f, y, 120f, 40f);
            Text costTip = UIFactory.Label(panel, "CostTip", "（每轮开始与展开时）", 18, TextAnchor.MiddleLeft);
            costTip.color = new Color(1f, 1f, 1f, 0.5f);
            UIFactory.Place(costTip.rectTransform, 474f, y, 300f, 40f);
            y += 52f;

            _hint = UIFactory.Label(panel, "Hint", "", 22, TextAnchor.UpperLeft);
            _hint.color = UIFactory.HintColor;
            UIFactory.Place(_hint.rectTransform, 24f, y, PanelWidth - 48f, 64f);
            y += 72f;

            Button save = UIFactory.TextButton(panel, "Save", "确认", OnConfirm, false, 24);
            UIFactory.Place(save.GetComponent<RectTransform>(), 24f, y, 150f, 48f);
            Button cancel = UIFactory.TextButton(panel, "Cancel", "取消", CloseWindow, false, 24);
            UIFactory.Place(cancel.GetComponent<RectTransform>(), 186f, y, 150f, 48f);
            Button openLib = UIFactory.TextButton(panel, "OpenLibrary", "打开资源库", OnOpenLibrary, false, 20);
            UIFactory.Place(openLib.GetComponent<RectTransform>(), 348f, y, 170f, 48f);
            Button close = UIFactory.TextButton(panel, "Close", "关闭", CloseWindow, false, 24);
            UIFactory.Place(close.GetComponent<RectTransform>(), 530f, y, 126f, 48f);
        }

        private void BuildSide(Transform side)
        {
            Text title = UIFactory.Label(side, "Title", "当前已有的领域", 26, TextAnchor.MiddleLeft);
            UIFactory.Place(title.rectTransform, 20f, 18f, SideWidth - 40f, 38f);

            RectTransform list = UIFactory.Node("List", side);
            UIFactory.Place(list, 0f, 64f, SideWidth, PanelHeight - 76f);
            _sideList = list;
        }

        private void LoadFrom(HeroRoleData role)
        {
            if (role == null)
            {
                return;
            }
            _role = role;
            _effectWarningConfirmed = false;
            _def = CustomBattleBgPlugin.Store.Find(role);
            if (_def == null)
            {
                _def = new DomainProfile();
                _def.RoleKey = role.RoleLibraryKey;
                _def.RoleName = DomainStore.SafeRoleName(role);
                _def.Suffix = "";
            }
            if (string.IsNullOrEmpty(_def.RoleName))
            {
                _def.RoleName = "调查员";
            }

            _title.text = DomainText.L("编辑「领域」—— " + _def.RoleName);
            _namePrefix.text = DomainText.L(_def.RoleName + "——");
            _suffix.text = _def.Suffix;
            _bgFile = _def.BgFile;
            _bgmFile = _def.BgmFile;
            _bgmLoop = _def.BgmLoop;
            _halfScreen = _def.HalfScreen;
            RefreshBoxes();
            RefreshMountOptions(role);
            RefreshEffectControls();
            _hint.text = _def.Section == "" || string.IsNullOrEmpty(_def.Section)
                ? DomainText.L("这个角色还没有自己的领域配置，填好后点确认就会生成一条。")
                : "";
            RebuildSideList();

            try
            {
                _suffix.Select();
                _suffix.ActivateInputField();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>回显"领域效果"模块（打开编辑窗时调）。</summary>
        private void RefreshEffectControls()
        {
            if (_def == null)
            {
                return;
            }
            try
            {
                List<ActionOption> allyTargets = new List<ActionOption>();
                allyTargets.Add(new ActionOption("无", 0, 0));
                allyTargets.Add(new ActionOption("自身", 1, 0));
                allyTargets.Add(new ActionOption("所有友方", 2, 0));
                _allyTargetDropdown.SetOptions(allyTargets);
                _allyTargetDropdown.SelectByTypeAndId(_def.AllyTarget, 0);

                _enemyTarget = _def.EnemyTarget;
                UIFactory.SetCheckbox(_enemyBox, _enemyTarget);

                List<ActionOption> effects = BuildEffectOptions();
                _allyEffectDropdown.SetOptions(effects);
                _allyEffectDropdown.SelectByTypeAndId(_def.AllyEffectType, 0);
                _enemyEffectDropdown.SetOptions(new List<ActionOption>(effects));
                _enemyEffectDropdown.SelectByTypeAndId(_def.EnemyEffectType, 0);

                _allyEffectValue.text = _def.AllyEffectValue.ToString();
                _enemyEffectValue.text = _def.EnemyEffectValue.ToString();

                List<ActionOption> costs = new List<ActionOption>();
                costs.Add(new ActionOption("无", 0, 0));
                costs.Add(new ActionOption("扣除生命值", 1, 0));
                costs.Add(new ActionOption("扣除精神值", 2, 0));
                costs.Add(new ActionOption("扣除魔法值", 3, 0));
                _costDropdown.SetOptions(costs);
                _costDropdown.SelectByTypeAndId(_def.CostType, 0);
                _costValue.text = _def.CostValue.ToString();
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("回显领域效果失败：" + e.Message);
            }
        }

        /// <summary>重建"挂载行动"的选项：全部战斗技能（运行时数据，含其它 mod 加的）+ 该角色拥有的法术。</summary>
        private void RefreshMountOptions(HeroRoleData role)
        {
            if (_mountDropdown == null)
            {
                return;
            }
            List<ActionOption> list = new List<ActionOption>();
            list.Add(new ActionOption("（不挂载）", 0, 0));
            list.Add(new ActionOption("【任意法术】释放任何法术时触发", 2, 0));

            // 战斗技能：从运行时数据表读（mod 加的技能也在里面，比如自定义心的「激活「心」」）
            try
            {
                if (Singleton<ResManager>.HasInstance)
                {
                    List<BattleSkillTableData> skills = Singleton<ResManager>.Instance.CharacterBattleSkillData;
                    if (skills != null)
                    {
                        List<BattleSkillTableData> sorted = new List<BattleSkillTableData>(skills);
                        sorted.Sort(delegate(BattleSkillTableData a, BattleSkillTableData b)
                        {
                            int ia = a != null ? a.Id : 0;
                            int ib = b != null ? b.Id : 0;
                            return ia.CompareTo(ib);
                        });
                        for (int i = 0; i < sorted.Count; i++)
                        {
                            BattleSkillTableData s = sorted[i];
                            if (s == null || s.Id == DomainConstants.SkillId)
                            {
                                continue;   // 不列自己的「领域展开」
                            }
                            string name = s.Name != null && !string.IsNullOrEmpty(s.Name.InputText)
                                ? s.Name.InputText
                                : ("技能 " + s.Id);
                            list.Add(new ActionOption("【行动】" + name, 1, s.Id));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("列战斗技能失败：" + e.Message);
            }

            // 该调查员已学会的法术
            try
            {
                if (role != null && role.MagicSkills != null)
                {
                    List<MagicSkillData> magics = new List<MagicSkillData>(role.MagicSkills);
                    magics.Sort(delegate(MagicSkillData a, MagicSkillData b)
                    {
                        int ia = a != null ? a.Id : 0;
                        int ib = b != null ? b.Id : 0;
                        return ia.CompareTo(ib);
                    });
                    for (int i = 0; i < magics.Count; i++)
                    {
                        MagicSkillData m = magics[i];
                        if (m == null)
                        {
                            continue;
                        }
                        string name = m.Config != null && m.Config.Name != null &&
                                      !string.IsNullOrEmpty(m.Config.Name.InputText)
                            ? m.Config.Name.InputText
                            : ("法术 " + m.Id);
                        list.Add(new ActionOption("【法术】" + name, 3, m.Id));
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("列角色法术失败：" + e.Message);
            }

            _mountDropdown.SetOptions(list);
            _mountDropdown.SelectByTypeAndId(_def.MountType, _def.MountId);
        }

        private void RefreshBoxes()
        {
            if (_bgBoxText != null)
            {
                _bgBoxText.text = DomainText.L(string.IsNullOrEmpty(_bgFile) ? "未选择" : _bgFile);
                _bgBoxText.color = string.IsNullOrEmpty(_bgFile)
                    ? new Color(1f, 1f, 1f, 0.35f)
                    : UIFactory.TextColor;
            }
            if (_bgmBoxText != null)
            {
                _bgmBoxText.text = DomainText.L(string.IsNullOrEmpty(_bgmFile) ? "未选择" : _bgmFile);
                _bgmBoxText.color = string.IsNullOrEmpty(_bgmFile)
                    ? new Color(1f, 1f, 1f, 0.35f)
                    : UIFactory.TextColor;
            }
            UIFactory.SetCheckbox(_loopBox, _bgmLoop);
            UIFactory.SetCheckbox(_halfBox, _halfScreen);
        }

        // 窗口开着时只认 Esc：按一下就关（游戏的快捷键这时已经被屏蔽掉了）
        private void Update()
        {
            if (_canvasGo == null || !_canvasGo.activeSelf)
            {
                return;
            }
            try
            {
                SuppressNavigation();
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseWindow();
                }
            }
            catch (Exception)
            {
            }
        }

        private void OnHintMsg(string msg)
        {
            if (_hint != null)
            {
                _hint.text = msg;
            }
        }

        private void OnPickBackground()
        {
            AssetImporter.PickBackground(delegate(string fileName)
            {
                _bgFile = fileName;
                RefreshBoxes();
                OnHintMsg(DomainText.L("已选择背景：") + fileName);
            }, OnHintMsg, AskOverwriteFile);
        }

        private void OnPickBgm()
        {
            AssetImporter.PickBgm(delegate(string fileName)
            {
                _bgmFile = fileName;
                RefreshBoxes();
                OnHintMsg(DomainText.L("已选择BGM：") + fileName);
            }, OnHintMsg, AskOverwriteFile);
        }

        /// <summary>从资源库里已有的文件里挑一张背景（不发生复制，直接用库里的文件）。</summary>
        private void OnPickBgFromLibrary()
        {
            FileListPopup.Show(_canvasGo.transform, "从资源库选择战斗背景",
                CustomBattleBgPlugin.Store.BackgroundDir, DomainConstants.BackgroundExtensions,
                delegate(string fileName)
                {
                    _bgFile = fileName;
                    RefreshBoxes();
                    OnHintMsg(DomainText.L("已从资源库选择背景：") + fileName);
                });
        }

        /// <summary>从资源库里已有的文件里挑一首 BGM。</summary>
        private void OnPickBgmFromLibrary()
        {
            FileListPopup.Show(_canvasGo.transform, "从资源库选择战斗BGM",
                CustomBattleBgPlugin.Store.BgmDir, DomainConstants.BgmExtensions,
                delegate(string fileName)
                {
                    _bgmFile = fileName;
                    RefreshBoxes();
                    OnHintMsg(DomainText.L("已从资源库选择BGM：") + fileName);
                });
        }

        /// <summary>选了同名文件时弹确认：覆盖 / 取消（AssetImporter 通过回调调到这里）。</summary>
        private void AskOverwriteFile(string fileName, Action onOverwrite, Action onCancel)
        {
            ShowConfirm(
                DomainText.L("资源库里已经有同名文件：\n" + fileName +
                             "\n\n要覆盖它吗？（引用这个文件的配置会换成新内容）"),
                delegate
                {
                    if (onOverwrite != null) { onOverwrite(); }
                },
                delegate
                {
                    if (onCancel != null) { onCancel(); }
                });
        }

        /// <summary>打开资源库目录（资源管理器）。</summary>
        private void OnOpenLibrary()
        {
            try
            {
                string dir = CustomBattleBgPlugin.Store.AssetsRoot;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                FolderBrowserHelper.OpenFolder(dir);
                OnHintMsg(DomainText.L("已打开资源库：" + dir));
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("打开资源库失败：" + e);
                OnHintMsg(DomainText.L("打开资源库失败，看日志。"));
            }
        }

        private void OnClearBackground()
        {
            if (string.IsNullOrEmpty(_bgFile))
            {
                OnHintMsg(DomainText.L("没有选择背景。"));
                return;
            }
            string file = _bgFile;
            ShowConfirm(
                DomainText.L("清除会把这张背景文件一起删掉：\n" + file +
                             "\n\n（现在没有资源库，删掉后要重新导入才能再用）"),
                delegate { DoClearBackground(file); },
                null);
        }

        private void OnClearBgm()
        {
            if (string.IsNullOrEmpty(_bgmFile))
            {
                OnHintMsg(DomainText.L("没有选择BGM。"));
                return;
            }
            string file = _bgmFile;
            ShowConfirm(
                DomainText.L("清除会把这首BGM文件一起删掉：\n" + file +
                             "\n\n（现在没有资源库，删掉后要重新导入才能再用）"),
                delegate { DoClearBgm(file); },
                null);
        }

        private void DoClearBackground(string file)
        {
            _bgFile = "";
            RefreshBoxes();
            bool deleted = CustomBattleBgPlugin.Store.TryDeleteBackgroundFile(file, _def);
            // 配置已经存过盘的话，把引用也立刻清掉，免得留下一条指向空文件的记录
            if (_def != null && !string.IsNullOrEmpty(_def.Section))
            {
                _def.BgFile = "";
                CustomBattleBgPlugin.Store.Save(_def);
            }
            OnHintMsg(deleted
                ? DomainText.L("已清除并删除背景文件：" + file)
                : DomainText.L("已清除背景选择（文件不存在或被别的配置用着，没有删）。"));
        }

        private void DoClearBgm(string file)
        {
            _bgmFile = "";
            RefreshBoxes();
            bool deleted = CustomBattleBgPlugin.Store.TryDeleteBgmFile(file, _def);
            if (_def != null && !string.IsNullOrEmpty(_def.Section))
            {
                _def.BgmFile = "";
                CustomBattleBgPlugin.Store.Save(_def);
            }
            OnHintMsg(deleted
                ? DomainText.L("已清除并删除BGM文件：" + file)
                : DomainText.L("已清除BGM选择（文件不存在或被别的配置用着，没有删）。"));
        }

        private void OnToggleLoop()
        {
            _bgmLoop = !_bgmLoop;
            UIFactory.SetCheckbox(_loopBox, _bgmLoop);
        }

        private void OnToggleHalf()
        {
            _halfScreen = !_halfScreen;
            UIFactory.SetCheckbox(_halfBox, _halfScreen);
        }

        private bool _enemyTarget;
        private bool _effectWarningConfirmed;

        private void OnToggleEnemyTarget()
        {
            _enemyTarget = !_enemyTarget;
            UIFactory.SetCheckbox(_enemyBox, _enemyTarget);
        }

        /// <summary>效果下拉框的选项：预设效果在前，后面是游戏里所有状态（buff）。</summary>
        private List<ActionOption> BuildEffectOptions()
        {
            List<ActionOption> list = new List<ActionOption>();
            for (int i = 0; i < DomainEffectPreset.Presets.Length; i++)
            {
                int preset = DomainEffectPreset.Presets[i];
                list.Add(new ActionOption(DomainEffectPreset.DisplayName(preset), preset, 0));
            }
            int total = 0;
            int usable = 0;
            try
            {
                if (Singleton<ResManager>.HasInstance)
                {
                    BuffResFactory factory = Singleton<ResManager>.Instance.BuffFactory;
                    if (factory != null)
                    {
                        // 【重要】BuffTableData 是"按需加载"的：游戏启动时只把文件路径登记进
                        // LoadFileDatas，没真正读进内存。直接取 All 只能拿到各 mod 加载过的那几十条，
                        // 游戏本体的（中毒/燃烧/流血…）全在待加载清单里。
                        // 所以：先按清单逐个 GetConfig 触发加载，再统一收集。
                        long t0 = DateTime.Now.Ticks;
                        List<BuffTableData> all = CollectAllBuffs(factory);
                        total = all.Count;
                        long ms = (DateTime.Now.Ticks - t0) / TimeSpan.TicksPerMillisecond;
                        CustomBattleBgPlugin.LogInfo("状态表加载：共收集 " + all.Count + " 条（耗时 " + ms + " 毫秒）");
                        all.Sort(delegate(BuffTableData a, BuffTableData b)
                        {
                            int ia = a != null ? a.Id : 0;
                            int ib = b != null ? b.Id : 0;
                            return ia.CompareTo(ib);
                        });
                        for (int i = 0; i < all.Count; i++)
                        {
                            BuffTableData cfg = all[i];
                            if (cfg == null)
                            {
                                continue;
                            }
                            // 跳过我们自己的运行时状态，避免自我引用
                            if (cfg.Comment != null && cfg.Comment.StartsWith(DomainConstants.CommentTag))
                            {
                                continue;
                            }
                            string name = ResolveBuffName(cfg);
                            if (string.IsNullOrEmpty(name))
                            {
                                continue;
                            }
                            list.Add(new ActionOption("【状态】" + name,
                                DomainEffectPreset.BuffOffset + cfg.Id, 0));
                            usable++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("列状态列表失败：" + e.Message);
            }
            CustomBattleBgPlugin.LogInfo("效果列表构建：状态表共 " + total + " 条，可用 " + usable +
                " 条，加预设后合计 " + list.Count + " 项");
            return list;
        }

        /// <summary>取状态的名字：InputText 为空就用本地化表的值，再不行按编号显示。</summary>
        private static string ResolveBuffName(BuffTableData cfg)
        {
            try
            {
                if (cfg.Name != null)
                {
                    if (!string.IsNullOrEmpty(cfg.Name.InputText))
                    {
                        return cfg.Name.InputText;
                    }
                    string v = cfg.Name.GetValue();
                    if (!string.IsNullOrEmpty(v))
                    {
                        return v;
                    }
                }
            }
            catch (Exception)
            {
            }
            return cfg.Id > 0 ? ("状态 " + cfg.Id) : "";
        }

        /// <summary>
        /// 收集全部状态：已加载的（各 mod 的）+ 按 LoadingFileDatas 清单触发的按需加载（游戏本体的）。
        /// 不这么做的话，游戏本体的状态（中毒/燃烧/流血…）一条都列不出来。
        /// </summary>
        private static List<BuffTableData> CollectAllBuffs(BuffResFactory factory)
        {
            List<BuffTableData> result = new List<BuffTableData>();
            HashSet<int> seen = new HashSet<int>();

            List<BuffTableData> loaded = new List<BuffTableData>(factory.All);
            for (int i = 0; i < loaded.Count; i++)
            {
                AddUnique(result, seen, loaded[i]);
            }

            List<ResloadFileData> pending = factory.LoadFileDatas;
            if (pending != null)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    ResloadFileData item = pending[i];
                    if (item == null || string.IsNullOrEmpty(item.FileName))
                    {
                        continue;
                    }
                    try
                    {
                        BuffTableData cfg = factory.GetConfig(item.FileName);
                        AddUnique(result, seen, cfg);
                    }
                    catch (Exception)
                    {
                        // 单条读不出来就跳过，不影响其它
                    }
                }
            }
            return result;
        }

        private static void AddUnique(List<BuffTableData> list, HashSet<int> seen, BuffTableData cfg)
        {
            if (cfg == null || seen.Contains(cfg.Id))
            {
                return;
            }
            seen.Add(cfg.Id);
            list.Add(cfg);
        }

        /// <summary>数值规范化：不允许负数的字段，负数自动归 0（用户口径）。</summary>
        private static int NormalizeValue(string text, int effectType, bool isCost)
        {
            int v;
            if (!int.TryParse((text ?? "").Trim(), out v))
            {
                return 0;
            }
            if (isCost)
            {
                return v < 0 ? 0 : v;
            }
            if (effectType != 0 &&
                (DomainEffectPreset.MustBeNonNegative(effectType) || DomainEffectPreset.IsBuff(effectType)) &&
                v < 0)
            {
                return 0;
            }
            return v;
        }

        private static string CleanText(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "";
            }
            string text = raw.Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();
            while (text.Length > 0 && (text[0] == '—' || text[0] == '-'))
            {
                text = text.Substring(1).Trim();
            }
            if (text.Length > 12)
            {
                text = text.Substring(0, 12);
            }
            return text;
        }

        private void OnConfirm()
        {
            try
            {
                string suffix = CleanText(_suffix.text);
                if (string.IsNullOrEmpty(suffix))
                {
                    _hint.text = DomainText.L("后缀不能空着，得给自己的领域起个名字。");
                    return;
                }

                string display = DomainText.BuildDisplayName(_def.RoleName, suffix);
                if (CustomBattleBgPlugin.Store.IsNameTaken(display, _def))
                {
                    _hint.text = DomainText.L("已经有一条同名的领域了，换个后缀。");
                    return;
                }

                _def.Suffix = suffix;
                _def.BgFile = _bgFile;
                _def.BgmFile = _bgmFile;
                _def.BgmLoop = _bgmLoop;
                _def.HalfScreen = _halfScreen;
                ActionOption mount = _mountDropdown != null ? _mountDropdown.Current : null;
                _def.MountType = mount != null ? mount.Type : 0;
                _def.MountId = mount != null ? mount.Id : 0;
                _def.MountName = mount != null ? mount.Label : "";

                // 领域效果（数据相关）
                ActionOption allyTargetOpt = _allyTargetDropdown != null ? _allyTargetDropdown.Current : null;
                _def.AllyTarget = allyTargetOpt != null ? allyTargetOpt.Type : 0;
                _def.EnemyTarget = _enemyTarget;
                ActionOption allyEff = _allyEffectDropdown != null ? _allyEffectDropdown.Current : null;
                _def.AllyEffectType = allyEff != null ? allyEff.Type : 0;
                _def.AllyEffectValue = NormalizeValue(_allyEffectValue.text, _def.AllyEffectType, false);
                ActionOption enemyEff = _enemyEffectDropdown != null ? _enemyEffectDropdown.Current : null;
                _def.EnemyEffectType = enemyEff != null ? enemyEff.Type : 0;
                _def.EnemyEffectValue = NormalizeValue(_enemyEffectValue.text, _def.EnemyEffectType, false);
                ActionOption costOpt = _costDropdown != null ? _costDropdown.Current : null;
                _def.CostType = costOpt != null ? costOpt.Type : 0;
                _def.CostValue = NormalizeValue(_costValue.text, 0, true);

                if (string.IsNullOrEmpty(_def.RoleKey))
                {
                    if (_role != null && !string.IsNullOrEmpty(_role.RoleLibraryKey))
                    {
                        _def.RoleKey = _role.RoleLibraryKey;
                    }
                    else
                    {
                        _def.RoleKey = DomainStore.LookupRoleKey(_def.RoleName);
                    }
                }
                if (string.IsNullOrEmpty(_def.Section))
                {
                    _def.Section = CustomBattleBgPlugin.Store.NextSection();
                }

                // 配了"数据相关"的内容 → 先让玩家确认"后果自负"（用户口径：只定义背景和BGM则不弹）
                if (_def.HasAnyEffectData && !_effectWarningConfirmed)
                {
                    ShowConfirm(
                        DomainText.L("你自定义了领域效果（数据相关）。\n\n" +
                                     "这些数据不保证正确：如果导致游戏出错，本模组只会把效果部分自动失效" +
                                     "（背景与BGM不受影响），不会帮你修复。\n\n确定保存吗？"),
                        delegate
                        {
                            _effectWarningConfirmed = true;
                            OnConfirm();   // 确认后重新走一遍保存流程
                        },
                        delegate
                        {
                            _hint.text = DomainText.L("已取消保存（数据没有改动）。");
                        });
                    return;
                }
                _effectWarningConfirmed = false;

                DomainBuffBuilder.Rebuild(_def);   // 效果数据变了 → 重建运行时状态
                CustomBattleBgPlugin.Store.Save(_def);
                _hint.text = DomainText.L("已保存：" + display + "\n改动在下一场战斗生效。");
                RebuildSideList();
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("保存领域配置失败：" + e);
                _hint.text = DomainText.L("保存出错，看日志。");
            }
        }

        private void RebuildSideList()
        {
            try
            {
                for (int i = 0; i < _sideRows.Count; i++)
                {
                    if (_sideRows[i] != null)
                    {
                        UnityEngine.Object.Destroy(_sideRows[i]);
                    }
                }
                _sideRows.Clear();

                List<DomainProfile> profiles = CustomBattleBgPlugin.Store.Profiles;
                if (profiles.Count == 0)
                {
                    Text empty = UIFactory.Label(_sideList, "Empty", "还没有人设置过领域。", 18, TextAnchor.UpperLeft);
                    empty.color = new Color(1f, 1f, 1f, 0.5f);
                    UIFactory.Place(empty.rectTransform, 16f, 8f, SideWidth - 32f, 60f);
                    _sideRows.Add(empty.gameObject);
                    return;
                }

                int shown = Mathf.Min(profiles.Count, MaxSideRows);
                for (int i = 0; i < shown; i++)
                {
                    DomainProfile profile = profiles[i];
                    float y = i * 56f;

                    Text name = UIFactory.Label(_sideList, "Domain" + i, profile.DisplayName, 22, TextAnchor.MiddleLeft);
                    UIFactory.Place(name.rectTransform, 20f, y, SideWidth - 140f, 48f);
                    _sideRows.Add(name.gameObject);

                    DomainProfile target = profile;
                    bool[] confirm = new bool[1];
                    Button del = UIFactory.TextButton(_sideList, "Del" + i, "删除", null, true, 20);
                    UIFactory.Place(del.GetComponent<RectTransform>(), SideWidth - 116f, y + 4f, 96f, 40f);
                    Text delText = del.transform.Find("Text").GetComponent<Text>();
                    del.onClick.AddListener(delegate
                    {
                        if (!confirm[0])
                        {
                            confirm[0] = true;
                            delText.text = DomainText.L("确认删除");
                            return;
                        }
                        DeleteProfile(target);
                    });
                    _sideRows.Add(del.gameObject);
                }

                if (profiles.Count > shown)
                {
                    Text more = UIFactory.Label(_sideList, "More",
                        "（还有 " + (profiles.Count - shown) + " 条没显示）", 20, TextAnchor.MiddleLeft);
                    more.color = new Color(1f, 1f, 1f, 0.5f);
                    UIFactory.Place(more.rectTransform, 20f, shown * 56f, SideWidth - 40f, 36f);
                    _sideRows.Add(more.gameObject);
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("刷新领域列表失败：" + e);
            }
        }

        private void DeleteProfile(DomainProfile target)
        {
            try
            {
                // 删除配置时把它引用的背景/BGM 文件一起删掉（现在没有资源库，留着也没法重新导入）
                CustomBattleBgPlugin.Store.DeleteWithAssets(target);
                if (target == _def)
                {
                    _def = null;
                    if (_role != null)
                    {
                        LoadFrom(_role);
                    }
                }
                else
                {
                    RebuildSideList();
                }
                _hint.text = DomainText.L("已删除「" + target.DisplayName + "」，这个角色以后不再展开领域。");
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("删除领域配置失败：" + e);
                _hint.text = DomainText.L("删除出错，看日志。");
            }
        }

        // ==================== 询问弹窗 ====================

        private void BuildConfirmDialog(GameObject owner)
        {
            Image bg = UIFactory.Panel(owner.transform, "Confirm", new Color(0f, 0f, 0f, 0.75f));
            UIFactory.Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);
            _confirmGo = bg.gameObject;

            // 注意：这里**不要**给弹窗单独挂 Canvas。
            // GraphicRaycaster 只射线检测"挂在同一块画布上"的图形，子画布里的东西它打不到。
            Image box = UIFactory.Panel(_confirmGo.transform, "Box", UIFactory.PanelColor);
            CenterPanel(box.rectTransform, 0f, 580f, 290f);

            _confirmText = UIFactory.Label(box.transform, "Text", "", 22, TextAnchor.MiddleCenter);
            UIFactory.Place(_confirmText.rectTransform, 20f, 16f, 540f, 170f);

            Button yes = UIFactory.TextButton(box.transform, "Yes", "确认", delegate
            {
                HideConfirm();
                if (_confirmYes != null) { _confirmYes(); }
            }, false, 24);
            UIFactory.Place(yes.GetComponent<RectTransform>(), 90f, 200f, 170f, 52f);

            Button no = UIFactory.TextButton(box.transform, "No", "取消", delegate
            {
                HideConfirm();
                if (_confirmNo != null) { _confirmNo(); }
            }, false, 24);
            UIFactory.Place(no.GetComponent<RectTransform>(), 320f, 200f, 170f, 52f);

            _confirmGo.SetActive(false);
        }

        private void ShowConfirm(string text, UnityAction onYes, UnityAction onNo)
        {
            _confirmYes = onYes;
            _confirmNo = onNo;
            if (_confirmText != null) { _confirmText.text = text; }
            if (_confirmGo != null)
            {
                _confirmGo.transform.SetAsLastSibling();
                _confirmGo.SetActive(true);
            }
        }

        private void HideConfirm()
        {
            if (_confirmGo != null) { _confirmGo.SetActive(false); }
        }
    }
}
