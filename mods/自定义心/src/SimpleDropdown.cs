// 自己搭的“下拉框”：一个按钮 + 点开后弹出的选项列表。
//
// 没用 Unity 自带的 Dropdown，因为它必须配一整套模板层级（Template/Viewport/Content/Item…），
// 纯代码搭起来又长又容易错；这里要的只是"从几个固定选项里挑一个"。

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

        private int _index;

        public Action<int> OnChanged;

        public int Index
        {
            get { return _index; }
        }

        public SimpleDropdown(Transform owner, string name, string[] options,
            float x, float y, float width, float height)
        {
            _options = options;
            _index = 0;

            _button = UIFactory.TextButton(owner, name, options.Length > 0 ? options[0] : "-", Toggle, false, 22);
            UIFactory.Place(_button.GetComponent<RectTransform>(), x, y, width, height);
            _buttonLabel = _button.transform.Find("Text").GetComponent<Text>();

            Image listBg = UIFactory.Panel(owner, name + "_List", UIFactory.PanelColor);
            _listRoot = listBg.gameObject;
            UIFactory.Place(listBg.rectTransform, x, y + height, width, height * options.Length + 4f);
            for (int i = 0; i < options.Length; i++)
            {
                int captured = i;
                Button item = UIFactory.TextButton(_listRoot.transform, "Item" + i, options[i],
                    delegate { Pick(captured); }, false, 20);
                UIFactory.Place(item.GetComponent<RectTransform>(), 2f, 2f + i * height, width - 4f, height - 2f);
            }
            _listRoot.SetActive(false);
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
            _listRoot.SetActive(false);
            if (_opened == this)
            {
                _opened = null;
            }
            SetIndex(index, true);
        }
    }
}
