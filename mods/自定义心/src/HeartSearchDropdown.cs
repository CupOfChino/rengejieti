// 带搜索的状态下拉框（心的附加效果用）。
// 结构照搬「自定义播放战斗背景和BGM」的 SearchDropdown；状态有几百条，没有搜索很难找。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace XinEditor
{
    internal class HeartSearchDropdown
    {
        private static HeartSearchDropdown _opened;

        private const float ItemHeight = 36f;
        private const float SearchHeight = 42f;
        private const float ScrollbarWidth = 16f;
        private const int MaxVisibleItems = 4;

        private readonly Button _button;
        private readonly Text _buttonLabel;
        private readonly GameObject _listRoot;
        private readonly RectTransform _listRt;
        private readonly InputField _search;
        private readonly RectTransform _contentRt;
        private readonly RectTransform _viewportRt;
        private readonly RectTransform _barRt;
        private readonly ScrollRect _scroll;
        private readonly List<HeartBuffChoice> _options;
        private readonly List<Button> _items = new List<Button>();
        private readonly float _width;

        private int _index;
        private string _filter = "";
        private bool _built;
        private int _builtCount;

        public Action<int> OnChanged;

        public int Index
        {
            get { return _index; }
        }

        public HeartBuffChoice Current
        {
            get
            {
                if (_options.Count == 0)
                {
                    return null;
                }
                return _options[_index];
            }
        }

        public HeartSearchDropdown(Transform owner, string name, List<HeartBuffChoice> options,
            float x, float y, float width, float height)
        {
            _options = options ?? new List<HeartBuffChoice>();
            if (_options.Count == 0)
            {
                _options.Add(new HeartBuffChoice(XinText.T("（无）", "(None)"), 0));
            }
            _width = width;
            _index = 0;

            _button = UIFactory.TextButton(owner, name,
                _options[0].Label, Toggle, false, 18);
            UIFactory.Place(_button.GetComponent<RectTransform>(), x, y, width, height);
            _buttonLabel = _button.transform.Find("Text").GetComponent<Text>();
            _buttonLabel.alignment = TextAnchor.MiddleLeft;
            // 状态名可能很长：按钮上截断，列表里靠搜索/横向溢出找全名。
            _buttonLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            _buttonLabel.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Stretch(_buttonLabel.rectTransform, 10f, 8f, 2f, 2f);

            float visible = Mathf.Min(_options.Count, MaxVisibleItems);
            float listHeight = SearchHeight + ItemHeight * Mathf.Max(visible, 2f) + 8f;

            Image listBg = UIFactory.Panel(owner, name + "_List", UIFactory.PanelColor);
            _listRoot = listBg.gameObject;
            _listRt = listBg.rectTransform;
            UIFactory.Place(listBg.rectTransform, x, y + height, width, listHeight);

            _search = UIFactory.TextInput(_listRoot.transform, "Search", "搜索关键词…", false);
            UIFactory.Place(_search.GetComponent<RectTransform>(), 4f, 4f, width - 8f, SearchHeight - 8f);
            _search.onValueChanged.AddListener(OnSearchChanged);

            Image viewport = UIFactory.Panel(_listRoot.transform, "Viewport", new Color(0f, 0f, 0f, 0f));
            UIFactory.Place(viewport.rectTransform, 0f, SearchHeight, width - ScrollbarWidth,
                listHeight - SearchHeight - 4f);
            _viewportRt = viewport.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();

            Image content = UIFactory.Panel(viewport.transform, "Content", new Color(0f, 0f, 0f, 0f));
            _contentRt = content.rectTransform;
            _contentRt.anchorMin = new Vector2(0f, 1f);
            _contentRt.anchorMax = new Vector2(1f, 1f);
            _contentRt.pivot = new Vector2(0.5f, 1f);
            _contentRt.anchoredPosition = Vector2.zero;
            _contentRt.sizeDelta = new Vector2(0f, _options.Count * ItemHeight + 4f);

            Image barBg = UIFactory.Panel(_listRoot.transform, "Scrollbar", new Color(1f, 1f, 1f, 0.12f));
            UIFactory.Place(barBg.rectTransform, width - ScrollbarWidth + 2f, SearchHeight + 2f,
                ScrollbarWidth - 4f, listHeight - SearchHeight - 8f);
            _barRt = barBg.rectTransform;
            Image slidingArea = UIFactory.Panel(barBg.transform, "SlidingArea", new Color(0f, 0f, 0f, 0f));
            UIFactory.Stretch(slidingArea.rectTransform, 0f, 0f, 0f, 0f);
            Image handle = UIFactory.Panel(slidingArea.transform, "Handle", new Color(1f, 1f, 1f, 0.45f));
            UIFactory.Stretch(handle.rectTransform, 0f, 0f, 0f, 0f);

            Scrollbar scrollbar = barBg.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            _scroll = _listRoot.AddComponent<ScrollRect>();
            _scroll.content = _contentRt;
            _scroll.viewport = viewport.rectTransform;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scroll.verticalNormalizedPosition = 1f;

            _listRoot.SetActive(false);
        }

        /// <summary>按保存的 EffectType 回显。</summary>
        public void SelectByValue(int value)
        {
            for (int i = 0; i < _options.Count; i++)
            {
                if (_options[i].Value == value)
                {
                    SetIndex(i, false);
                    return;
                }
            }
            SetIndex(0, false);
        }

        /// <summary>
        /// 保存的数据里如果是当前列表没有的项（例如取消订阅自定义BGM 后仍有预设），
        /// 补一条显示出来，避免打开编辑窗就被静默重置成“无”。
        /// </summary>
        public void EnsureOption(string label, int value)
        {
            for (int i = 0; i < _options.Count; i++)
            {
                if (_options[i].Value == value)
                {
                    SetIndex(i, false);
                    return;
                }
            }
            _options.Add(new HeartBuffChoice(label, value));
            if (_builtCount >= _options.Count - 1)
            {
                AppendItem(_options.Count - 1);
                _builtCount = _options.Count;
                _built = true;
            }
            SetIndex(_options.Count - 1, false);
        }

        public void SetIndex(int index, bool notify)
        {
            if (_options.Count == 0)
            {
                return;
            }
            if (index < 0 || index >= _options.Count)
            {
                index = 0;
            }
            _index = index;
            _buttonLabel.text = _options[index].Label;
            if (notify && OnChanged != null)
            {
                OnChanged(index);
            }
        }

        private void OnSearchChanged(string text)
        {
            _filter = string.IsNullOrEmpty(text) ? "" : text.Trim();
            LayoutItems();
        }

        /// <summary>分帧预建列表项；count = int.MaxValue 表示一次建完。</summary>
        internal void BuildChunk(int count)
        {
            if (_built)
            {
                return;
            }
            long target = (long)_builtCount + Mathf.Max(1, count);
            int end = (int)Math.Min((long)_options.Count, target);
            for (int i = _builtCount; i < end; i++)
            {
                AppendItem(i);
            }
            _builtCount = end;
            _built = _builtCount >= _options.Count;
            if (_contentRt != null)
            {
                _contentRt.sizeDelta = new Vector2(0f, _options.Count * ItemHeight + 4f);
            }
        }

        internal bool IsFullyBuilt
        {
            get { return _built; }
        }

        private void AppendItem(int index)
        {
            int captured = index;
            Button item = UIFactory.TextButton(_contentRt, "Item" + index, _options[index].Label,
                delegate { Pick(captured); }, false, 18);
            RectTransform rt = item.GetComponent<RectTransform>();
            UIFactory.Place(rt, 2f, 2f + index * ItemHeight, _width - ScrollbarWidth - 8f, ItemHeight - 2f);
            Text label = item.transform.Find("Text").GetComponent<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(label.rectTransform, 10f, 6f, 2f, 2f);
            _items.Add(item);
            if (_contentRt != null)
            {
                _contentRt.sizeDelta = new Vector2(0f, _options.Count * ItemHeight + 4f);
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }
            BuildChunk(int.MaxValue);
        }

        private void LayoutItems()
        {
            if (!_built)
            {
                return;
            }
            float y = 2f;
            for (int i = 0; i < _items.Count; i++)
            {
                string label = _options[i].Label ?? "";
                bool show = _filter.Length == 0 ||
                            label.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
                GameObject go = _items[i].gameObject;
                if (go.activeSelf != show)
                {
                    go.SetActive(show);
                }
                if (show)
                {
                    UIFactory.Place(_items[i].GetComponent<RectTransform>(), 2f, y,
                        _width - ScrollbarWidth - 8f, ItemHeight - 2f);
                    y += ItemHeight;
                }
            }
            if (_contentRt != null)
            {
                _contentRt.sizeDelta = new Vector2(0f, Mathf.Max(y + 2f, 8f));
            }
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }
        }

        private void Toggle()
        {
            SimpleDropdown.CloseOpened();
            if (_listRoot.activeSelf)
            {
                Close();
                return;
            }
            if (_opened != null && _opened != this)
            {
                _opened._listRoot.SetActive(false);
            }
            _opened = this;
            _listRoot.transform.SetAsLastSibling();
            _listRoot.SetActive(true);
            EnsureBuilt();
            LayoutItems();
            try
            {
                _search.text = "";
                _search.Select();
                _search.ActivateInputField();
            }
            catch (Exception)
            {
            }
        }

        internal static void CloseOpened()
        {
            if (_opened != null)
            {
                _opened.Close();
            }
        }

        private void Close()
        {
            _listRoot.SetActive(false);
            try
            {
                _search.DeactivateInputField();
            }
            catch (Exception)
            {
            }
            if (_opened == this)
            {
                _opened = null;
            }
        }

        private void Pick(int index)
        {
            SetIndex(index, true);
            Close();
        }
    }
}
