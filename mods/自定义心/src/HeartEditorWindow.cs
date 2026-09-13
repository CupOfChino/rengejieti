// 「修改【心】」的编辑窗。
//
// 左边是编辑区（名称 / 速度 / 三行加成 + 保存 / 重置），右边挂一个小窗列出现有的心，
// 每行带删除。界面是纯代码搭的，字体借用游戏 UI 的那套。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XinEditor
{
    internal class HeartEditorWindow : MonoBehaviour
    {
        private const float PanelWidth = 680f;
        private const float PanelHeight = 520f;
        private const float SideWidth = 400f;
        private const int MaxSideRows = 9;

        private static HeartEditorWindow _instance;

        private GameObject _canvasGo;
        private bool _navWasOn;
        private HeroRoleData _role;
        private HeartDefinition _def;
        private bool _isNew;

        private Text _title;
        private Text _namePrefix;
        private InputField _suffix;
        private InputField _speed;
        private SimpleDropdown[] _pick = new SimpleDropdown[3];
        private InputField[] _value = new InputField[3];
        private Text[] _unit = new Text[3];
        private Text _hint;
        private Transform _sideList;
        private readonly List<GameObject> _sideRows = new List<GameObject>();

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
                    GameObject go = new GameObject("XinEditorCanvas");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<HeartEditorWindow>();
                    _instance.Build(go);
                }
                _instance._canvasGo.SetActive(true);
                _instance.SuppressNavigation();
                _instance.LoadFrom(role);
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("打开编辑窗失败：" + e);
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

        private static string[] StatOptions()
        {
            List<string> list = new List<string>();
            list.Add("不选");
            for (int i = 0; i < HeartConstants.SelectableStats.Length; i++)
            {
                list.Add(HeartConstants.StatName(HeartConstants.SelectableStats[i]));
            }
            return list.ToArray();
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

            float mainX = -(SideWidth + 24f) * 0.5f;
            float sideX = (PanelWidth + 24f) * 0.5f;

            Image panel = UIFactory.Panel(go.transform, "Panel", UIFactory.PanelColor);
            CenterPanel(panel.rectTransform, mainX, PanelWidth, PanelHeight);
            BuildMain(panel.transform);

            Image side = UIFactory.Panel(go.transform, "Side", UIFactory.PanelColor);
            CenterPanel(side.rectTransform, sideX, SideWidth, PanelHeight);
            BuildSide(side.transform);
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

            _title = UIFactory.Label(panel, "Title", "修改【心】", 30, TextAnchor.MiddleLeft);
            UIFactory.Place(_title.rectTransform, 24f, y, PanelWidth - 48f, 42f);
            Button closeTop = UIFactory.TextButton(panel, "CloseTop", "×", CloseWindow, true, 30);
            UIFactory.Place(closeTop.GetComponent<RectTransform>(), PanelWidth - 72f, y + 2f, 48f, 44f);
            y += 56f;

            Text label1 = UIFactory.Label(panel, "Label_Name", "名称", 24, TextAnchor.MiddleLeft);
            UIFactory.Place(label1.rectTransform, 24f, y, 76f, 42f);
            _namePrefix = UIFactory.Label(panel, "NamePrefix", "心（某某）——", 22, TextAnchor.MiddleLeft);
            UIFactory.Place(_namePrefix.rectTransform, 104f, y, 280f, 42f);
            _suffix = UIFactory.TextInput(panel, "Suffix", "后缀（必填）", false);
            UIFactory.Place(_suffix.GetComponent<RectTransform>(), 392f, y, PanelWidth - 416f, 42f);
            y += 56f;

            Text label2 = UIFactory.Label(panel, "Label_Speed", "速度", 24, TextAnchor.MiddleLeft);
            UIFactory.Place(label2.rectTransform, 24f, y, 76f, 42f);
            _speed = UIFactory.TextInput(panel, "Speed", "20", true);
            UIFactory.Place(_speed.GetComponent<RectTransform>(), 104f, y, 120f, 42f);
            Text speedNote = UIFactory.Label(panel, "SpeedNote", "可填 1 ~ 100（固定项，必填）", 20, TextAnchor.MiddleLeft);
            speedNote.color = new Color(1f, 1f, 1f, 0.55f);
            UIFactory.Place(speedNote.rectTransform, 236f, y, 400f, 42f);
            y += 60f;

            string[] options = StatOptions();
            for (int i = 0; i < 3; i++)
            {
                int row = i;
                _pick[i] = new SimpleDropdown(panel, "Pick" + i, options, 24f, y, 220f, 42f);
                _pick[i].OnChanged = delegate(int index) { OnStatChanged(row); };
                _value[i] = UIFactory.TextInput(panel, "Value" + i, "0", true);
                UIFactory.Place(_value[i].GetComponent<RectTransform>(), 268f, y, 120f, 42f);
                _unit[i] = UIFactory.Label(panel, "Unit" + i, "", 20, TextAnchor.MiddleLeft);
                _unit[i].color = new Color(1f, 1f, 1f, 0.55f);
                UIFactory.Place(_unit[i].rectTransform, 400f, y, 270f, 42f);
                y += 52f;
            }
            y += 8f;

            _hint = UIFactory.Label(panel, "Hint", "", 22, TextAnchor.UpperLeft);
            _hint.color = UIFactory.HintColor;
            UIFactory.Place(_hint.rectTransform, 24f, y, PanelWidth - 48f, 56f);
            y += 64f;

            Button save = UIFactory.TextButton(panel, "Save", "保存", OnSave, false, 24);
            UIFactory.Place(save.GetComponent<RectTransform>(), 24f, y, 160f, 48f);
            Button reset = UIFactory.TextButton(panel, "Reset", "重置回默认", OnReset, false, 24);
            UIFactory.Place(reset.GetComponent<RectTransform>(), 196f, y, 210f, 48f);
            Button close = UIFactory.TextButton(panel, "Close", "关闭", CloseWindow, false, 24);
            UIFactory.Place(close.GetComponent<RectTransform>(), PanelWidth - 184f, y, 160f, 48f);
        }

        private void BuildSide(Transform side)
        {
            Text title = UIFactory.Label(side, "Title", "当前已有的心", 26, TextAnchor.MiddleLeft);
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
            _def = XinEditorPlugin.Store.Find(role);
            _isNew = _def == null;
            if (_isNew)
            {
                _def = new HeartDefinition();
                _def.RoleKey = role.RoleLibraryKey;
                _def.RoleName = HeartStore.SafeRoleName(role);
                _def.Suffix = "";
                _def.Speed = 20;
                _def.Stats = new List<HeartStat>();
            }
            if (string.IsNullOrEmpty(_def.RoleName))
            {
                _def.RoleName = "调查员";
            }

            _title.text = "修改【心】 —— " + _def.RoleName;
            _namePrefix.text = "心（" + _def.RoleName + "）——";
            _suffix.text = _def.Suffix;
            _speed.text = _def.Speed.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < 3; i++)
            {
                HeartStat stat = i < _def.Stats.Count ? _def.Stats[i] : null;
                _pick[i].SetIndex(stat == null ? 0 : (int)stat.Type + 1, false);
                _value[i].text = stat == null ? "" : stat.Value.ToString(CultureInfo.InvariantCulture);
                RefreshUnit(i);
            }
            _hint.text = _isNew ? "这个角色还没有自定义心，填好后点保存就会生成一颗。" : "";
            RebuildSideList();

            // 直接把光标放进"后缀"输入框，省得还要点一下
            try
            {
                _suffix.Select();
                _suffix.ActivateInputField();
            }
            catch (Exception)
            {
            }
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

        private void OnStatChanged(int row)
        {
            if (_pick[row].Index <= 0)
            {
                _value[row].text = "";
            }
            RefreshUnit(row);
        }

        private void RefreshUnit(int row)
        {
            int index = _pick[row].Index;
            if (index <= 0)
            {
                _unit[row].text = "";
                return;
            }
            HeartStatType type = HeartConstants.SelectableStats[index - 1];
            _unit[row].text = "可填 0 ~ " + HeartConstants.MaxValue(type);
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

        private void OnSave()
        {
            try
            {
                int speed;
                if (!int.TryParse((_speed.text ?? "").Trim(), out speed) || speed < 1 || speed > 100)
                {
                    _hint.text = "速度要填 1 到 100 之间的整数（速度是固定项，必填）。";
                    return;
                }
                string suffix = CleanText(_suffix.text);
                if (string.IsNullOrEmpty(suffix))
                {
                    _hint.text = "后缀不能空着，得给这颗心起个名字。";
                    return;
                }

                List<HeartStat> stats = new List<HeartStat>();
                for (int i = 0; i < 3; i++)
                {
                    int index = _pick[i].Index;
                    if (index <= 0)
                    {
                        continue;
                    }
                    HeartStatType type = HeartConstants.SelectableStats[index - 1];
                    int value;
                    if (!int.TryParse((_value[i].text ?? "").Trim(), out value) || value <= 0)
                    {
                        _hint.text = HeartConstants.StatName(type) + " 要填一个大于 0 的数；不想加就把左边改回「不选」。";
                        return;
                    }
                    int max = HeartConstants.MaxValue(type);
                    if (value > max)
                    {
                        _hint.text = HeartConstants.StatName(type) + " 最多 " + max + "。";
                        return;
                    }
                    bool repeat = false;
                    for (int j = 0; j < stats.Count; j++)
                    {
                        if (stats[j].Type == type)
                        {
                            repeat = true;
                            break;
                        }
                    }
                    if (repeat)
                    {
                        _hint.text = "同一项不能选两次。";
                        return;
                    }
                    stats.Add(new HeartStat(type, value));
                }

                string display = "心（" + _def.RoleName + "）——" + suffix;
                if (XinEditorPlugin.Store.IsNameTaken(display, _def))
                {
                    _hint.text = "已经有一颗同名的心了，换个后缀。";
                    return;
                }

                _def.Suffix = suffix;
                _def.Speed = speed;
                _def.Stats = stats;
                // 编号空着的话（旧数据或没取到），保存时按名字补上，保证下次进游戏能认出来
                if (string.IsNullOrEmpty(_def.RoleKey))
                {
                    if (_role != null && !string.IsNullOrEmpty(_role.RoleLibraryKey))
                    {
                        _def.RoleKey = _role.RoleLibraryKey;
                    }
                    else
                    {
                        _def.RoleKey = HeartStore.LookupRoleKey(_def.RoleName);
                    }
                }
                if (string.IsNullOrEmpty(_def.Section))
                {
                    _def.Section = XinEditorPlugin.Store.NextSection();
                }
                if (_def.BuffId <= 0)
                {
                    _def.BuffId = XinEditorPlugin.Store.AllocateBuffId();
                }
                XinEditorPlugin.Store.Save(_def);
                HeartBuffBuilder.Rebuild(_def);
                _isNew = false;
                _hint.text = "已保存：" + display + "\n改动在下一场战斗生效。";
                RebuildSideList();
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("保存自定义心失败：" + e);
                _hint.text = "保存出错，看日志。";
            }
        }

        private void OnReset()
        {
            _speed.text = "20";
            _pick[0].SetIndex((int)HeartStatType.DamageBonus + 1, false);
            _value[0].text = "2";
            _pick[1].SetIndex((int)HeartStatType.DamageReduce + 1, false);
            _value[1].text = "1";
            _pick[2].SetIndex(0, false);
            _value[2].text = "";
            for (int i = 0; i < 3; i++)
            {
                RefreshUnit(i);
            }
            _hint.text = "已填回默认心的数值（伤害+2、速度+20、减伤1）。名字不动，点保存才生效。";
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

                List<HeartDefinition> hearts = XinEditorPlugin.Store.Hearts;
                if (hearts.Count == 0)
                {
                    Text empty = UIFactory.Label(_sideList, "Empty", "还没有人给自己的心做过设定。", 18, TextAnchor.UpperLeft);
                    empty.color = new Color(1f, 1f, 1f, 0.5f);
                    UIFactory.Place(empty.rectTransform, 16f, 8f, SideWidth - 32f, 60f);
                    _sideRows.Add(empty.gameObject);
                    return;
                }

                int shown = Mathf.Min(hearts.Count, MaxSideRows);
                for (int i = 0; i < shown; i++)
                {
                    HeartDefinition heart = hearts[i];
                    float y = i * 56f;

                    Text name = UIFactory.Label(_sideList, "Heart" + i, heart.DisplayName, 22, TextAnchor.MiddleLeft);
                    UIFactory.Place(name.rectTransform, 20f, y, SideWidth - 140f, 48f);
                    _sideRows.Add(name.gameObject);

                    HeartDefinition target = heart;
                    bool[] confirm = new bool[1];
                    Button del = UIFactory.TextButton(_sideList, "Del" + i, "删除", null, true, 20);
                    UIFactory.Place(del.GetComponent<RectTransform>(), SideWidth - 116f, y + 4f, 96f, 40f);
                    Text delText = del.transform.Find("Text").GetComponent<Text>();
                    del.onClick.AddListener(delegate
                    {
                        if (!confirm[0])
                        {
                            confirm[0] = true;
                            delText.text = "确认删除";
                            return;
                        }
                        DeleteHeart(target);
                    });
                    _sideRows.Add(del.gameObject);
                }

                if (hearts.Count > shown)
                {
                    Text more = UIFactory.Label(_sideList, "More",
                        "（还有 " + (hearts.Count - shown) + " 颗没显示）", 20, TextAnchor.MiddleLeft);
                    more.color = new Color(1f, 1f, 1f, 0.5f);
                    UIFactory.Place(more.rectTransform, 20f, shown * 56f, SideWidth - 40f, 36f);
                    _sideRows.Add(more.gameObject);
                }
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("刷新心列表失败：" + e);
            }
        }

        private void DeleteHeart(HeartDefinition target)
        {
            try
            {
                XinEditorPlugin.Store.Delete(target);
                HeartBuffBuilder.Unregister(target);
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
                _hint.text = "已删掉「" + target.DisplayName + "」，这个角色以后走默认心。";
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("删除自定义心失败：" + e);
                _hint.text = "删除出错，看日志。";
            }
        }
    }
}
