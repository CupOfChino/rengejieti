// 自己搭的"下拉框"：一个按钮 + 点开后弹出的选项列表。
//
// 没用 Unity 自带的 Dropdown，因为它必须配一整套模板层级（Template/Viewport/Content/Item…），
// 纯代码搭起来又长又容易错；这里要的只是"从几个固定选项里挑一个"。
//
// 2026-09-17 改版：候选属性变多了，列表改成**一屏 6 项 + 侧边滑动条**；
// 并支持**禁用某一项**（已经被其它行选走的属性要变暗、点不动）。

using System;
using UnityEngine;
using UnityEngine.UI;

namespace XinEditor
{
    internal class SimpleDropdown
    {
        private static SimpleDropdown _opened;

        private readonly Button _button;
        private readonly Text _buttonLabel;
        private readonly GameObject _listRoot;
        private readonly string[] _options;
        private readonly Button[] _items;
        private readonly Text[] _itemLabels;
        private readonly bool[] _disabled;

        private int _index;

        public Action<int> OnChanged;

        /// <summary>列表一屏最多放几项，超出就走滑动条。</summary>
        private const int VisibleItems = 6;

        private static readonly Color ItemNormalColor = Color.white;
        private static readonly Color ItemDisabledColor = new Color(1f, 1f, 1f, 0.3f);

        public int Index
        {
            get { return _index; }
        }

        public SimpleDropdown(Transform owner, string name, string[] options,
            float x, float y, float width, float height)
        {
            _options = options;
            _disabled = new bool[options.Length];
            _items = new Button[options.Length];
            _itemLabels = new Text[options.Length];
            _index = 0;

            _button = UIFactory.TextButton(owner, name, options.Length > 0 ? options[0] : "-", Toggle, false, 22);
            UIFactory.Place(_button.GetComponent<RectTransform>(), x, y, width, height);
            _buttonLabel = _button.transform.Find("Text").GetComponent<Text>();

            int visible = Math.Min(options.Length, VisibleItems);
            float listHeight = height * visible + 4f;
            const float scrollbarWidth = 16f;

            Image listBg = UIFactory.Panel(owner, name + "_List", UIFactory.PanelColor);
            _listRoot = listBg.gameObject;
            UIFactory.Place(listBg.rectTransform, x, y + height, width, listHeight);

            // 视口（负责裁剪）+ 滚动内容
            Image viewport = UIFactory.Panel(_listRoot.transform, "Viewport", new Color(0f, 0f, 0f, 0f));
            UIFactory.Place(viewport.rectTransform, 0f, 0f, width - scrollbarWidth, listHeight);
            viewport.gameObject.AddComponent<RectMask2D>();

            Image content = UIFactory.Panel(viewport.transform, "Content", new Color(0f, 0f, 0f, 0f));
            RectTransform contentRt = content.rectTransform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, options.Length * height + 4f);

            for (int i = 0; i < options.Length; i++)
            {
                int captured = i;
                Button item = UIFactory.TextButton(contentRt, "Item" + i, options[i],
                    delegate { Pick(captured); }, false, 20);
                UIFactory.Place(item.GetComponent<RectTransform>(), 2f, 2f + i * height,
                    width - scrollbarWidth - 4f, height - 2f);
                _items[i] = item;
                _itemLabels[i] = item.transform.Find("Text").GetComponent<Text>();
            }

            // 侧边滑动条
            Image barBg = UIFactory.Panel(_listRoot.transform, "Scrollbar", new Color(1f, 1f, 1f, 0.12f));
            UIFactory.Place(barBg.rectTransform, width - scrollbarWidth + 2f, 2f,
                scrollbarWidth - 4f, listHeight - 4f);
            Image slidingArea = UIFactory.Panel(barBg.transform, "SlidingArea", new Color(0f, 0f, 0f, 0f));
            UIFactory.Stretch(slidingArea.rectTransform, 0f, 0f, 0f, 0f);
            Image handle = UIFactory.Panel(slidingArea.transform, "Handle", new Color(1f, 1f, 1f, 0.45f));
            UIFactory.Stretch(handle.rectTransform, 0f, 0f, 0f, 0f);

            Scrollbar scrollbar = barBg.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            ScrollRect scroll = _listRoot.AddComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = viewport.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalNormalizedPosition = 1f;

            _listRoot.SetActive(false);
        }

        /// <summary>禁用/启用某一项（被别的行选走的属性要变暗、点不动）。</summary>
        public void SetDisabled(int index, bool disabled)
        {
            if (index < 0 || index >= _items.Length)
            {
                return;
            }
            _disabled[index] = disabled;
            if (_items[index] != null)
            {
                _items[index].interactable = !disabled;
            }
            if (_itemLabels[index] != null)
            {
                _itemLabels[index].color = disabled ? ItemDisabledColor : ItemNormalColor;
            }
        }

        public bool IsDisabled(int index)
        {
            return index >= 0 && index < _disabled.Length && _disabled[index];
        }

        public void SetIndex(int index, bool notify)
        {
            if (_options.Length == 0)
            {
                return;
            }
            if (index < 0 || index >= _options.Length)
            {
                index = 0;
            }
            _index = index;
            _buttonLabel.text = _options[index];
            if (notify && OnChanged != null)
            {
                OnChanged(index);
            }
        }

        private void Toggle()
        {
            if (_listRoot.activeSelf)
            {
                _listRoot.SetActive(false);
                if (_opened == this)
                {
                    _opened = null;
                }
                return;
            }
            if (_opened != null && _opened != this)
            {
                _opened._listRoot.SetActive(false);
            }
            _opened = this;
            _listRoot.transform.SetAsLastSibling();
            _listRoot.SetActive(true);
        }

        private void Pick(int index)
        {
            if (IsDisabled(index))
            {
                return;   // 已被别的行选走，点不动
            }
            _listRoot.SetActive(false);
            if (_opened == this)
            {
                _opened = null;
            }
            SetIndex(index, true);
        }
    }
}
