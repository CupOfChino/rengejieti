// 拼界面用的小工具。
//
// 游戏没有给 mod 留"往界面里插控件"的口子，所以编辑窗是纯代码搭出来的：
// 摆 RectTransform、挂 Image/Text/InputField/Button，字体直接借用游戏 UI 正在用的那套
// （不然中文会变成方块）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace XinEditor
{
    internal static class UIFactory
    {
        private static Font _font;

        internal static readonly Color PanelColor = new Color(0.08f, 0.07f, 0.06f, 0.97f);
        internal static readonly Color FieldColor = new Color(0.18f, 0.15f, 0.11f, 1f);
        internal static readonly Color ButtonColor = new Color(0.26f, 0.20f, 0.12f, 1f);
        internal static readonly Color ButtonWarnColor = new Color(0.36f, 0.16f, 0.12f, 1f);
        internal static readonly Color TextColor = new Color(0.95f, 0.92f, 0.82f, 1f);
        internal static readonly Color HintColor = new Color(1f, 0.72f, 0.35f, 1f);

        // 借用游戏 UI 的字体；借不到就用系统里的中文字体兜底
        internal static Font Font
        {
            get
            {
                if (_font != null)
                {
                    return _font;
                }
                _font = FindGameFont();
                if (_font == null)
                {
                    _font = CreateOsFont();
                }
                return _font;
            }
        }

        // 游戏把各语言的字体放在 MODToolConfig.LocalizationFonts 里。
        // 不能随便拿界面上第一个 Text 的字体：别的插件、数字专用的字体都可能没有中文字形，
        // 那样中文会整片不显示（只剩数字）。
        private static Font FindGameFont()
        {
            try
            {
                MODToolConfig tool = MODToolConfig.Instance;
                if (tool != null && tool.LocalizationFonts != null && tool.LocalizationFonts.Count > 0)
                {
                    string code = "";
                    try
                    {
                        code = Singleton<LocalizationHandler>.Instance.CurrentLanguageCode;
                    }
                    catch (Exception)
                    {
                    }

                    List<Font> fonts = tool.LocalizationFonts;
                    Font chinese = null;
                    Font sameLanguage = null;
                    Font any = null;
                    for (int i = 0; i < fonts.Count; i++)
                    {
                        Font f = fonts[i];
                        if (f == null)
                        {
                            continue;
                        }
                        if (any == null)
                        {
                            any = f;
                        }
                        if (CanDrawChinese(f))
                        {
                            if (!string.IsNullOrEmpty(code) && f.name != null && f.name.Contains(code))
                            {
                                return f;
                            }
                            if (chinese == null)
                            {
                                chinese = f;
                            }
                        }
                        else if (sameLanguage == null && !string.IsNullOrEmpty(code) && f.name != null &&
                                 f.name.Contains(code))
                        {
                            sameLanguage = f;
                        }
                    }
                    if (chinese != null)
                    {
                        return chinese;
                    }
                    if (sameLanguage != null)
                    {
                        return sameLanguage;
                    }
                    if (any != null)
                    {
                        return any;
                    }
                }
            }
            catch (Exception e)
            {
                XinEditorPlugin.LogError("取游戏字体失败：" + e.Message);
            }

            // 兜底：找界面上正在显示中文的那个 Text 用的字体
            try
            {
                Text[] all = Resources.FindObjectsOfTypeAll<Text>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].font != null && HasChinese(all[i].text))
                    {
                        return all[i].font;
                    }
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static Font CreateOsFont()
        {
            try
            {
                return Font.CreateDynamicFontFromOSFont(
                    new string[] { "Microsoft YaHei", "SimHei", "SimSun", "Arial" }, 22);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool CanDrawChinese(Font f)
        {
            try
            {
                return f.HasCharacter('心') && f.HasCharacter('伤') && f.HasCharacter('速');
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasChinese(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] >= 0x4E00 && text[i] <= 0x9FFF)
                {
                    return true;
                }
            }
            return false;
        }

        // 建立一个铺满父物体的控件，位置用绝对像素摆（左上角为原点）
        internal static RectTransform Node(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        internal static Image Panel(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        internal static Text Label(Transform parent, string name, string text, int size, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Text t = go.GetComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.text = text;
            t.color = TextColor;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        internal static InputField TextInput(Transform parent, string name, string placeholder, bool numberOnly)
        {
            Image bg = Panel(parent, name, FieldColor);
            InputField field = bg.gameObject.AddComponent<InputField>();
            field.targetGraphic = bg;

            Text body = Label(bg.transform, "Text", "", 22, TextAnchor.MiddleLeft);
            Stretch(body.rectTransform, 8f, 8f, 2f, 2f);
            field.textComponent = body;

            Text hint = Label(bg.transform, "Placeholder", placeholder, 22, TextAnchor.MiddleLeft);
            hint.color = new Color(1f, 1f, 1f, 0.3f);
            Stretch(hint.rectTransform, 8f, 8f, 2f, 2f);
            field.placeholder = hint;

            field.contentType = numberOnly ? InputField.ContentType.IntegerNumber : InputField.ContentType.Standard;
            field.characterLimit = numberOnly ? 3 : 24;
            return field;
        }

        internal static Button TextButton(Transform parent, string name, string label, UnityAction onClick,
            bool warn, int fontSize)
        {
            Image bg = Panel(parent, name, warn ? ButtonWarnColor : ButtonColor);
            Button b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            ColorBlock colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            b.colors = colors;

            Text t = Label(bg.transform, "Text", label, fontSize, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 4f, 4f, 2f, 2f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (onClick != null)
            {
                b.onClick.AddListener(onClick);
            }
            return b;
        }

        // 把子控件拉满父控件（四边留 l/r/t/b 像素）
        internal static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        // 以父控件左上角为原点摆一个控件
        internal static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }
    }
}
