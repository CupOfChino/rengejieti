// 带搜索的下拉框（"挂载行动"用）。
//
// 在「自定义心」那个 SimpleDropdown 的基础上改的：列表顶部多一个搜索框，
// 输入关键词就过滤选项（技能几十个，不搜索不好找）；顶部还常驻一个搜索框方便聚焦。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CustomBattleBg
{
    internal class SearchDropdown
    {
        private static SearchDropdown _opened;

        private const float ItemHeight = 36f;
        private const float SearchHeight = 42f;
        private const float ScrollbarWidth = 16f;
        private const int MaxVisibleItems = 6;

        private readonly Button _button;
        private readonly Text _buttonLabel;
        private readonly GameObject _listRoot;
        private readonly RectTransform _listRt;
        private readonly InputField _search;
        private readonly RectTransform _contentRt;
        private readonly RectTransform _viewportRt;
        private readonly RectTransform _barRt;
        private readonly ScrollRect _scroll;
        private readonly List<ActionOption> _options;
        private readonly List<Button> _items = new List<Button>();
        private readonly float _width;

        private int _index;
        private string _filter = "";

        public Action<int> OnChanged = null;

        public int Index
        {
            get { return _index; }
        }

        public ActionOption Current
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

        public SearchDropdown(Transform owner, string name, List<ActionOption> options,
            float x, float y, float width, float height)
        {
            _options = options ?? new List<ActionOption>();
            _width = width;
            _index = 0;

            _button = UIFactory.TextButton(owner, name,
                _options.Count > 0 ? _options[0].Label : "-", Toggle, false, 20);
            UIFactory.Place(_button.GetComponent<RectTransform>(), x, y, width, height);
            _buttonLabel = _button.transform.Find("Text").GetComponent<Text>();
            _buttonLabel.alignment = TextAnchor.MiddleLeft;
            _buttonLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(_buttonLabel.rectTransform, 10f, 26f, 2f, 2f);

            float visible = Mathf.Min(_options.Count, MaxVisibleItems);
            float listHeight = SearchHeight + ItemHeight * Mathf.Max(visible, 2f) + 8f;

            Image listBg = UIFactory.Panel(owner, name + "_List", UIFactory.PanelColor);
            _listRoot = listBg.gameObject;
            _listRt = listBg.rectTransform;
            UIFactory.Place(listBg.rectTransform, x, y + height, width, listHeight);

            // 顶部搜索框
            _search = UIFactory.TextInput(_listRoot.transform, "Search", "搜索关键词…", false);
            UIFactory.Place(_search.GetComponent<RectTransform>(), 4f, 4f, width - 8f, SearchHeight - 8f);
            _search.onValueChanged.AddListener(OnSearchChanged);

            // 视口 + 滚动内容
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

            BuildItems();

            // 侧边滑动条
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

        /// <summary>换一批选项（不同调查员的法术列表不一样，打开编辑窗时重建）。</summary>
        public void SetOptions(List<ActionOption> options)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null)
                {
                    UnityEngine.Object.Destroy(_items[i].gameObject);
                }
            }
            _items.Clear();
            _options.Clear();
            if (options != null)
            {
                _options.AddRange(options);
            }
            if (_options.Count == 0)
            {
                _options.Add(new ActionOption("（不挂载）", 0, 0));
            }
            BuildItems();
            RelayoutListHeight();
            _filter = "";
            if (_search != null)
            {
                _search.text = "";
            }
            LayoutItems();
            SetIndex(0, false);
        }

        /// <summary>按当前选项数量重算列表高度（换一批选项后必须重算，否则会只露一两行）。</summary>
        private void RelayoutListHeight()
        {
            float visible = Mathf.Min(_options.Count, MaxVisibleItems);
            float listHeight = SearchHeight + ItemHeight * Mathf.Max(visible, 2f) + 8f;
            if (_listRt != null)
            {
                _listRt.sizeDelta = new Vector2(_listRt.sizeDelta.x, listHeight);
            }
            if (_viewportRt != null)
            {
                _viewportRt.sizeDelta = new Vector2(_viewportRt.sizeDelta.x, listHeight - SearchHeight - 4f);
            }
            if (_barRt != null)
            {
                _barRt.sizeDelta = new Vector2(_barRt.sizeDelta.x, listHeight - SearchHeight - 8f);
            }
        }

        private void BuildItems()
        {
            for (int i = 0; i < _options.Count; i++)
            {
                int captured = i;
                Button item = UIFactory.TextButton(_contentRt, "Item" + i, _options[i].Label,
                    delegate { Pick(captured); }, false, 20);
                RectTransform rt = item.GetComponent<RectTransform>();
                UIFactory.Place(rt, 2f, 2f + i * ItemHeight, _width - ScrollbarWidth - 8f, ItemHeight - 2f);
                Text label = item.transform.Find("Text").GetComponent<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                UIFactory.Stretch(label.rectTransform, 10f, 6f, 2f, 2f);
                _items.Add(item);
            }
            if (_contentRt != null)
            {
                _contentRt.sizeDelta = new Vector2(0f, _options.Count * ItemHeight + 4f);
            }
        }

        /// <summary>按保存的类型/id 回显（打开编辑窗时用）。</summary>
        public void SelectByTypeAndId(int type, int id)
        {
            for (int i = 0; i < _options.Count; i++)
            {
                if (_options[i].Type == type && _options[i].Id == id)
                {
                    SetIndex(i, false);
                    return;
                }
            }
            SetIndex(0, false);
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

        // 按关键词显示/隐藏列表项，并把可见项重新排好
        private void LayoutItems()
        {
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

        private void Close()
        {
            _listRoot.SetActive(false);
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
