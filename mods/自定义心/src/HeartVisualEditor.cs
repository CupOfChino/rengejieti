using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XinEditor
{
    internal sealed class HeartVisualEditor : MonoBehaviour
    {
        private InputField _x, _y, _sx, _sy;
        private Text _title, _hint;
        private Button _save;
        private RectTransform _panel;
        private HeartVisualPreview _preview;
        private HeroRoleData _role;
        private string _roleKey;
        private GameObject _savePrompt;
        private Action<bool> _closed;
        internal bool IsOpen { get; private set; }

        internal static HeartVisualEditor Create(Transform parent, Action<bool> closed)
        {
            // 同一块画布，最后一个兄弟节点；遮罩既变暗也拦截射线。
            Image mask = UIFactory.Panel(parent, "HeartVisualModal", new Color(0f, 0f, 0f, 0.70f));
            UIFactory.Stretch(mask.rectTransform, 0f, 0f, 0f, 0f);
            HeartVisualEditor editor = mask.gameObject.AddComponent<HeartVisualEditor>();
            editor._closed = closed;
            editor.Build();
            editor.gameObject.SetActive(false);
            return editor;
        }

        private void Build()
        {
            Image panel = UIFactory.Panel(transform, "VisualEditorPanel", UIFactory.PanelColor);
            RectTransform root = panel.rectTransform;
            _panel = root;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(900f, 810f);
            FitToScreen();
            _title = UIFactory.Label(root, "Title", XinText.T("修改心特效大小和位置", "Shin size and position"), 28, TextAnchor.MiddleLeft);
            UIFactory.Place(_title.rectTransform, 24f, 18f, 770f, 40f);
            Button close = UIFactory.TextButton(root, "CloseWithoutSaving", "×", () => Close(false), true, 30);
            UIFactory.Place(close.GetComponent<RectTransform>(), 824f, 16f, 52f, 44f);
            Text origin = UIFactory.Label(root, "OriginNote",
                XinText.T("坐标原点 = 特效位置为 (0,0) 时的播放挂点；调整特效不会移动坐标轴。\n网格间距 0.25；X 正向右，Y 正向上。",
                    "Origin = the original effect anchor at (0,0). Axes remain fixed.\nGrid: 0.25 units. +X right, +Y up."), 19, TextAnchor.MiddleLeft);
            UIFactory.Place(origin.rectTransform, 24f, 66f, 852f, 46f);
            _preview = HeartVisualPreview.Create(root, 852f, 330f);
            UIFactory.Place(_preview.GetComponent<RectTransform>(), 24f, 118f, 852f, 330f);
            Text note = UIFactory.Label(root, "ParameterNote",
                XinText.T("宽、高填相同数值可等比缩放；只改其中一项可单独拉伸。", "Use equal width and height scales to preserve proportions; edit either independently."),
                20, TextAnchor.MiddleLeft);
            UIFactory.Place(note.rectTransform, 24f, 462f, 852f, 32f);
            _x = Field(root, "PositionX", XinText.T("水平位置 X", "Position X"), 24f, 508f);
            _y = Field(root, "PositionY", XinText.T("垂直位置 Y", "Position Y"), 458f, 508f);
            _sx = Field(root, "ScaleX", XinText.T("宽度缩放", "Width scale"), 24f, 564f);
            _sy = Field(root, "ScaleY", XinText.T("高度缩放", "Height scale"), 458f, 564f);
            Button apply = UIFactory.TextButton(root, "ApplyPreview", XinText.T("应用（更新预览）", "Apply to preview"), ApplyPreview, false, 23);
            UIFactory.Place(apply.GetComponent<RectTransform>(), 203f, 628f, 248f, 48f);
            Button reset = UIFactory.TextButton(root, "ResetVisual", XinText.T("重置为默认", "Reset to default"), ResetDraft, false, 23);
            UIFactory.Place(reset.GetComponent<RectTransform>(), 467f, 628f, 230f, 48f);
            _hint = UIFactory.Label(root, "VisualHint", "", 20, TextAnchor.UpperLeft);
            _hint.color = UIFactory.HintColor;
            UIFactory.Place(_hint.rectTransform, 24f, 688f, 852f, 42f);
            _save = UIFactory.TextButton(root, "SaveVisual", XinText.T("保存", "Save"), Save, false, 25);
            UIFactory.Place(_save.GetComponent<RectTransform>(), 322f, 742f, 256f, 48f);
            BuildSavePrompt();
        }

        private void BuildSavePrompt()
        {
            Image mask = UIFactory.Panel(transform, "SaveSuccessPrompt", new Color(0f, 0f, 0f, 0.45f));
            UIFactory.Stretch(mask.rectTransform, 0f, 0f, 0f, 0f);
            _savePrompt = mask.gameObject;
            Image box = UIFactory.Panel(mask.transform, "Box", UIFactory.PanelColor);
            RectTransform rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(480f, 230f);
            Text message = UIFactory.Label(rt, "Message", XinText.T("保存成功。", "Saved successfully."), 28, TextAnchor.MiddleCenter);
            UIFactory.Place(message.rectTransform, 24f, 28f, 432f, 84f);
            Button confirm = UIFactory.TextButton(rt, "Confirm", XinText.T("确认", "OK"), () => Close(true), false, 24);
            UIFactory.Place(confirm.GetComponent<RectTransform>(), 140f, 144f, 200f, 52f);
            _savePrompt.SetActive(false);
        }

        private static InputField Field(Transform parent, string name, string label, float x, float y)
        {
            Text title = UIFactory.Label(parent, name + "Label", label, 22, TextAnchor.MiddleLeft);
            UIFactory.Place(title.rectTransform, x, y, 168f, 42f);
            InputField field = UIFactory.TextInput(parent, name, "0.00", false);
            field.characterLimit = 16;
            UIFactory.Place(field.GetComponent<RectTransform>(), x + 170f, y, 224f, 42f);
            return field;
        }

        internal void Open(HeroRoleData role)
        {
            _role = role;
            _roleKey = HeartVisualRoleKey.Resolve(role);
            IsOpen = true;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            _title.text = XinText.T("修改心特效大小和位置 — ", "Shin appearance — ") + HeartStore.SafeRoleName(role);
            HeartVisualSettings saved = XinEditorPlugin.VisualStore != null
                ? XinEditorPlugin.VisualStore.Get(_roleKey) : new HeartVisualSettings();
            Fill(saved);
            _preview.Apply(saved);
            _preview.Load(role);
            _hint.text = XinText.T("应用只更新预览；保存成功后点确认返回。叉号或 Esc 关闭未保存的草稿。",
                "Apply updates preview only. Confirm a successful save to return. X or Esc discards unsaved changes.");
            _savePrompt.SetActive(false);
            SetPanelInteractable(true);
            _save.interactable = true;
            XinEditorPlugin.LogInfo("打开心外观编辑器：" + HeartStore.SafeRoleName(role) +
                "，对象编号=" + (role.RoleLibraryKey ?? "空") + "，解析编号=" + _roleKey);
        }

        private void Fill(HeartVisualSettings value)
        {
            _x.text = value.X.ToString("0.####", CultureInfo.InvariantCulture);
            _y.text = value.Y.ToString("0.####", CultureInfo.InvariantCulture);
            _sx.text = value.ScaleX.ToString("0.####", CultureInfo.InvariantCulture);
            _sy.text = value.ScaleY.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private bool Read(out HeartVisualSettings value)
        {
            if (HeartVisualSettings.TryParse(_x.text, _y.text, _sx.text, _sy.text, out value)) return true;
            _hint.text = XinText.T("请输入有效小数：位置 -10～10，宽度和高度缩放 0.01～10。", "Enter valid numbers: position -10 to 10; width and height scales 0.01 to 10.");
            return false;
        }

        private void ApplyPreview()
        {
            HeartVisualSettings value;
            if (!Read(out value)) return;
            _preview.Apply(value);
            _hint.text = XinText.T("预览已更新，尚未保存。", "Preview updated. Changes are not saved yet.");
        }

        private void ResetDraft()
        {
            HeartVisualSettings value = new HeartVisualSettings();
            Fill(value);
            _preview.Apply(value);
            _hint.text = XinText.T("已恢复预设参数，尚未保存。", "Default appearance restored in preview; not saved yet.");
        }

        private void Save()
        {
            HeartVisualSettings value;
            if (!Read(out value)) return;
            try
            {
                if (XinEditorPlugin.VisualStore == null) throw new InvalidOperationException("外观配置不可用");
                _roleKey = HeartVisualRoleKey.Resolve(_role);
                if (string.IsNullOrEmpty(_roleKey))
                {
                    _hint.text = XinText.T("未找到当前调查员的稳定编号，请返回幕间重新选择该调查员。",
                        "Investigator ID unavailable. Return to Interlude and select this investigator again.");
                    return;
                }
                XinEditorPlugin.VisualStore.Save(_roleKey, value);
                XinEditorPlugin.LogInfo(string.Format("已保存心外观：调查员={0}，位置=({1},{2})，缩放=({3},{4})",
                    _roleKey, value.X, value.Y, value.ScaleX, value.ScaleY));
                SetPanelInteractable(false);
                _savePrompt.transform.SetAsLastSibling();
                _savePrompt.SetActive(true);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
            catch (Exception e)
            {
                _hint.text = XinText.T("保存失败，请查看日志后重试。", "Save failed. Check the log and retry.");
                XinEditorPlugin.LogError("保存心外观失败：" + e);
            }
        }

        internal void Close(bool saved)
        {
            if (!IsOpen) return;
            IsOpen = false;
            _savePrompt.SetActive(false);
            SetPanelInteractable(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _preview.Release();
            gameObject.SetActive(false);
            if (_closed != null) _closed(saved);
        }

        private void Update()
        {
            FitToScreen();
        }

        internal void HandleEscape()
        {
            Close(_savePrompt != null && _savePrompt.activeSelf);
        }

        private void SetPanelInteractable(bool value)
        {
            CanvasGroup group = _panel.GetComponent<CanvasGroup>() ?? _panel.gameObject.AddComponent<CanvasGroup>();
            group.interactable = value;
        }

        private void FitToScreen()
        {
            if (_panel == null) return;
            float scale = Mathf.Min(1f, (Screen.width - 32f) / 900f, (Screen.height - 32f) / 810f);
            _panel.localScale = Vector3.one * Mathf.Max(0.25f, scale);
        }
    }
}
