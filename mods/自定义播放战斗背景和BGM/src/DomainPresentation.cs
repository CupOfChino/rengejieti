// 领域展开时的表现层：
//   · 可选角色头顶文字 + 边缘暗角（DomainTextBoxOverlay；黑底实验已放弃，尝试记录见协作文档）；
//   · 可选一次性音效（走 AudioManager 的 SFX 通道）；
//   · 文字框里的领域后缀单独做交错上下浮动（BaseMeshEffect，不依赖 Text 富文本动画标签）。
//
// 这一层只负责播放表现，任何异常都只写日志；背景/BGM/领域效果逻辑照常继续。

using System;
using System.Collections;
using Game;
using UnityEngine;
using UnityEngine.UI;

namespace CustomBattleBg
{
    internal static class DomainPresentation
    {
        internal static void PlayOnOpen(BattleRole caster, DomainProfile profile, string reason)
        {
            try
            {
                if (caster == null || profile == null)
                {
                    return;
                }
                if (profile.ShowOpenDialog)
                {
                    ShowTextBox(caster, profile, reason);
                }
                if (profile.PlayOpenSound)
                {
                    PlaySound(profile, reason);
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("领域展开表现播放失败（" + reason + "）：" + e.Message);
            }
        }

        private static void ShowTextBox(BattleRole caster, DomainProfile profile, string reason)
        {
            try
            {
                string suffix = GetDialogSuffix(profile);
                if (string.IsNullOrEmpty(suffix))
                {
                    suffix = "领域";
                }
                string prefix = DomainText.T("领域展开", "Domain Expansion");
                // 黑屏文字框的默认文字色是浅色，前缀不要再压成黑色；用加粗表示强调。
                string content = "<b>" + EscapeRichText(prefix) + "</b>——" +
                                 "<b><color=#FF0000>" + EscapeRichText(suffix) + "</color></b>";
                CustomBattleBgPlugin.LogInfo("（" + reason + "）显示领域文字框：「" + prefix + "——" + suffix + "」");
                DomainTextBoxOverlay.Show(content, prefix, prefix.Length + 2,
                    VisibleLength(suffix), caster, DomainConstants.OpenDialogDuration);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("显示领域文字框失败：" + e);
            }
        }

        private static void PlaySound(DomainProfile profile, string reason)
        {
            try
            {
                string path = CustomBattleBgPlugin.EnsureStore().ResolveSfxPath(profile);
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    CustomBattleBgPlugin.LogError("（" + reason + "）展开音效文件不存在，跳过：" + path);
                    return;
                }
                DomainPresentationRunner runner = DomainPresentationRunner.Ensure();
                if (runner == null)
                {
                    return;
                }
                CustomBattleBgPlugin.LogInfo("（" + reason + "）播放领域展开音效：" + profile.SfxFile);
                runner.StartCoroutine(runner.LoadAndPlaySfx(path));
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("播放展开音效失败：" + e.Message);
            }
        }

        private static string GetDialogSuffix(DomainProfile profile)
        {
            if (!string.IsNullOrEmpty(profile.RuntimeDialogSuffix))
            {
                return profile.RuntimeDialogSuffix.Trim();
            }
            if (!string.IsNullOrEmpty(profile.Suffix))
            {
                return profile.Suffix.Trim();
            }
            return string.IsNullOrEmpty(profile.DisplayName) ? "" : profile.DisplayName.Trim();
        }

        private static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            return value.Replace("<", "＜").Replace(">", "＞").Replace("|", "｜");
        }

        private static int VisibleLength(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }
            int count = 0;
            bool inTag = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '<')
                {
                    inTag = true;
                }
                else if (c == '>')
                {
                    inTag = false;
                }
                else if (!inTag)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>给文字框文本的指定字符区间做交错上下浮动。</summary>
    internal class DomainDialogFloatEffect : BaseMeshEffect
    {
        private Text _text;
        private string _expectedPrefix;
        private int _startChar;
        private int _charCount;
        private float _expireAt;

        internal void Configure(Text text, string expectedPrefix, int startChar, int charCount, float lifetime)
        {
            _text = text;
            _expectedPrefix = expectedPrefix ?? "";
            _startChar = Mathf.Max(0, startChar);
            _charCount = Mathf.Max(0, charCount);
            _expireAt = Time.unscaledTime + Mathf.Max(0.5f, lifetime);
        }

        private void Update()
        {
            try
            {
                if (Time.unscaledTime >= _expireAt)
                {
                    UnityEngine.Object.Destroy(this);
                    return;
                }
                if (_text != null)
                {
                    _text.SetVerticesDirty();
                }
            }
            catch (Exception)
            {
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            try
            {
                if (_text == null || _charCount <= 0 || vh == null || vh.currentVertCount <= 0 ||
                    string.IsNullOrEmpty(_text.text) ||
                    (string.IsNullOrEmpty(_expectedPrefix) || !_text.text.Contains(_expectedPrefix)))
                {
                    return;
                }
                int visible = CountVisible(_text.text);
                if (visible < _startChar + _charCount)
                {
                    return;   // 还在打字，等完整文本出现
                }
                int expectedVerts = visible * 4;
                if (vh.currentVertCount < expectedVerts)
                {
                    return;
                }

                float t = Time.unscaledTime;
                float period = Mathf.Max(0.2f, DomainConstants.OpenDialogFloatPeriod);
                float amplitude = DomainConstants.OpenDialogFloatAmplitude;
                for (int i = 0; i < _charCount; i++)
                {
                    // 相邻字符相位相差 π：一个向上时另一个向下，形成"交错"浮动。
                    float phase = i * Mathf.PI;
                    float offset = Mathf.Sin(t * (2f * Mathf.PI / period) + phase) * amplitude;
                    int firstVert = (_startChar + i) * 4;
                    for (int k = 0; k < 4; k++)
                    {
                        int index = firstVert + k;
                        if (index >= vh.currentVertCount)
                        {
                            break;
                        }
                        UIVertex vertex = new UIVertex();
                        vh.PopulateUIVertex(ref vertex, index);
                        vertex.position.y += offset;
                        vh.SetUIVertex(vertex, index);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static int CountVisible(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }
            int count = 0;
            bool inTag = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) count++;
            }
            return count;
        }
    }

    /// <summary>协程宿主（音效加载/播放、文字框浮动挂载）。</summary>
    internal class DomainPresentationRunner : MonoBehaviour
    {
        private static DomainPresentationRunner _instance;

        internal static DomainPresentationRunner Ensure()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("CustomBattleBgPresentation");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<DomainPresentationRunner>();
            }
            return _instance;
        }

        internal IEnumerator LoadAndPlaySfx(string path)
        {
            AudioClip clip = null;
            yield return MediaLoader.LoadAudioClip(path, delegate(AudioClip c) { clip = c; });
            if (clip == null)
            {
                yield break;
            }
            bool played = false;
            float wait = 1f;
            try
            {
                if (!PrefabSingleton<AudioManager>.HasInstance)
                {
                    CustomBattleBgPlugin.LogError("AudioManager 不可用，跳过展开音效");
                }
                else
                {
                    AudioManager manager = PrefabSingleton<AudioManager>.Instance;
                    manager.PlaySound(clip, 1f, false);
                    played = true;
                    wait = Mathf.Max(clip.length + 0.5f, 1f);
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("播放展开音效失败：" + e.Message);
            }
            if (played)
            {
                yield return new WaitForSecondsRealtime(wait);
            }
            if (clip != null)
            {
                UnityEngine.Object.Destroy(clip);
            }
        }
    }
}
