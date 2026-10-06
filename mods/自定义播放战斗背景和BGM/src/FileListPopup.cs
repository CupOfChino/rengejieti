// 「从资源库选择」的弹层：列出资源库目录（Background / Bgm）里已经有的文件，点一个就选中。
//
// 为什么要有它：资源库是长驻的（在游戏存档目录下，不会随 mod 更新清掉），
// 同一份素材可能被不同调查员复用，或者换角色时想重新挑一份——不用每次都从电脑里再导入一遍。
// 弹层里还附了「打开文件夹」按钮，想手动管理（改名/删文件）可以直接打开资源库。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace CustomBattleBg
{
    internal class FileListPopup
    {
        private static FileListPopup _instance;

        private const float PanelWidth = 620f;
        private const float PanelHeight = 470f;
        private const float RowHeight = 36f;
        private const float ScrollbarWidth = 16f;

        private Transform _canvas;
        private GameObject _root;
        private Text _title;
        private Text _emptyHint;
        private RectTransform _contentRt;
        private ScrollRect _scroll;
        private readonly List<GameObject> _rows = new List<GameObject>();

        private string _dir = "";
        private Action<string> _onPick;

        internal static void Show(Transform canvas, string title, string dir, string[] exts, Action<string> onPick)
        {
            try
            {
                if (canvas == null)
                {
                    return;
                }
                if (_instance == null || _instance._canvas != canvas)
                {
                    _instance = new FileListPopup();
                    _instance.Build(canvas);
                }
                _instance._dir = dir;
                _instance._onPick = onPick;
                _instance._title.text = DomainText.L(title);
                _instance._root.transform.SetAsLastSibling();
                _instance._root.SetActive(true);
                _instance.Rebuild(exts);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("打开资源库列表失败：" + e);
            }
        }

        internal static void Close()
        {
            if (_instance != null && _instance._root != null)
            {
                _instance._root.SetActive(false);
            }
        }

        private void Build(Transform canvas)
        {
            _canvas = canvas;

            // 遮罩：挡住后面的编辑窗，点一下关闭
            Image mask = UIFactory.Panel(canvas, "FileListPopup", new Color(0f, 0f, 0f, 0.6f));
            UIFactory.Stretch(mask.rectTransform, 0f, 0f, 0f, 0f);
            _root = mask.gameObject;
            Button maskBtn = _root.AddComponent<Button>();
            maskBtn.transition = Selectable.Transition.None;
            maskBtn.onClick.AddListener(Close);

            Image panel = UIFactory.Panel(_root.transform, "Panel", UIFactory.PanelColor);
            panel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.rectTransform.anchoredPosition = Vector2.zero;
            panel.rectTransform.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            // 面板自己吃掉点击，别让点击穿透到遮罩（否则一点面板就关掉了）
            Button panelBtn = panel.gameObject.AddComponent<Button>();
            panelBtn.transition = Selectable.Transition.None;

            _title = UIFactory.Label(panel.transform, "Title", "从资源库选择", 26, TextAnchor.MiddleLeft);
            UIFactory.Place(_title.rectTransform, 24f, 14f, PanelWidth - 120f, 42f);
            Button closeTop = UIFactory.TextButton(panel.transform, "CloseTop", "×", Close, true, 28);
            UIFactory.Place(closeTop.GetComponent<RectTransform>(), PanelWidth - 66f, 14f, 46f, 42f);

            // 列表（带滚动条）
            float listHeight = 300f;
            Image viewport = UIFactory.Panel(panel.transform, "Viewport", new Color(0f, 0f, 0f, 0.25f));
            UIFactory.Place(viewport.rectTransform, 20f, 66f, PanelWidth - 40f - ScrollbarWidth, listHeight);
            viewport.gameObject.AddComponent<RectMask2D>();

            Image content = UIFactory.Panel(viewport.transform, "Content", new Color(0f, 0f, 0f, 0f));
            _contentRt = content.rectTransform;
            _contentRt.anchorMin = new Vector2(0f, 1f);
            _contentRt.anchorMax = new Vector2(1f, 1f);
            _contentRt.pivot = new Vector2(0.5f, 1f);
            _contentRt.anchoredPosition = Vector2.zero;
            _contentRt.sizeDelta = new Vector2(0f, 8f);

            Image barBg = UIFactory.Panel(panel.transform, "Scrollbar", new Color(1f, 1f, 1f, 0.12f));
            UIFactory.Place(barBg.rectTransform, PanelWidth - 20f - ScrollbarWidth + 2f, 68f,
                ScrollbarWidth - 4f, listHeight - 4f);
            Image slidingArea = UIFactory.Panel(barBg.transform, "SlidingArea", new Color(0f, 0f, 0f, 0f));
            UIFactory.Stretch(slidingArea.rectTransform, 0f, 0f, 0f, 0f);
            Image handle = UIFactory.Panel(slidingArea.transform, "Handle", new Color(1f, 1f, 1f, 0.45f));
            UIFactory.Stretch(handle.rectTransform, 0f, 0f, 0f, 0f);
            Scrollbar scrollbar = barBg.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            // ScrollRect 挂在列表视口自己身上：拖拽/滚轮只在列表区域生效，
            // 不会和遮罩的"点击关闭"打架（遮罩在上面那层负责关闭）。
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _contentRt;
            _scroll.viewport = viewport.rectTransform;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scroll.verticalNormalizedPosition = 1f;

            _emptyHint = UIFactory.Label(viewport.transform, "Empty", "", 20, TextAnchor.MiddleCenter);
            _emptyHint.color = new Color(1f, 1f, 1f, 0.55f);
            _emptyHint.rectTransform.anchorMin = new Vector2(0f, 1f);
            _emptyHint.rectTransform.anchorMax = new Vector2(1f, 1f);
            _emptyHint.rectTransform.pivot = new Vector2(0.5f, 1f);
            _emptyHint.rectTransform.anchoredPosition = new Vector2(0f, -20f);
            _emptyHint.rectTransform.sizeDelta = new Vector2(0f, 60f);
            _emptyHint.gameObject.SetActive(false);

            // 底部按钮
            Button open = UIFactory.TextButton(panel.transform, "OpenFolder", "打开资源库文件夹",
                delegate { OpenFolder(); }, false, 20);
            UIFactory.Place(open.GetComponent<RectTransform>(), 20f, PanelHeight - 66f, 220f, 46f);
            Button close = UIFactory.TextButton(panel.transform, "Close", "关闭", Close, false, 22);
            UIFactory.Place(close.GetComponent<RectTransform>(), PanelWidth - 180f, PanelHeight - 66f, 160f, 46f);

            _root.SetActive(false);
        }

        private void OpenFolder()
        {
            try
            {
                if (!string.IsNullOrEmpty(_dir) && !Directory.Exists(_dir))
                {
                    Directory.CreateDirectory(_dir);
                }
                FolderBrowserHelper.OpenFolder(_dir);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("打开资源库文件夹失败：" + e.Message);
            }
        }

        private void Rebuild(string[] exts)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    UnityEngine.Object.Destroy(_rows[i]);
                }
            }
            _rows.Clear();

            List<string> files = new List<string>();
            try
            {
                if (Directory.Exists(_dir))
                {
                    string[] all = Directory.GetFiles(_dir);
                    for (int i = 0; i < all.Length; i++)
                    {
                        string ext = (Path.GetExtension(all[i]) ?? "").ToLowerInvariant();
                        bool ok = false;
                        for (int j = 0; j < exts.Length; j++)
                        {
                            if (ext == exts[j])
                            {
                                ok = true;
                                break;
                            }
                        }
                        if (ok)
                        {
                            files.Add(Path.GetFileName(all[i]));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("列资源库文件失败：" + e.Message);
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            _emptyHint.text = DomainText.L("资源库里还没有这个类型的文件。\n先用「选择文件」从电脑导入一个吧。");
            _emptyHint.gameObject.SetActive(files.Count == 0);

            for (int i = 0; i < files.Count; i++)
            {
                string fileName = files[i];
                Button item = UIFactory.TextButton(_contentRt, "File" + i, fileName,
                    delegate { Pick(fileName); }, false, 20);
                RectTransform rt = item.GetComponent<RectTransform>();
                UIFactory.Place(rt, 2f, 2f + i * RowHeight, PanelWidth - 40f - ScrollbarWidth - 8f, RowHeight - 2f);
                Text label = item.transform.Find("Text").GetComponent<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.Stretch(label.rectTransform, 12f, 6f, 2f, 2f);
                _rows.Add(item.gameObject);
            }

            _contentRt.sizeDelta = new Vector2(0f, Mathf.Max(files.Count * RowHeight + 4f, 8f));
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }
        }

        private void Pick(string fileName)
        {
            Action<string> cb = _onPick;
            Close();
            if (cb != null)
            {
                try
                {
                    cb(fileName);
                }
                catch (Exception e)
                {
                    CustomBattleBgPlugin.LogError("选择资源库文件后出错：" + e);
                }
            }
        }
    }
}
