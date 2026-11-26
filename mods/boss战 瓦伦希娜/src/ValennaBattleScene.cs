using System.IO;
using System.Reflection;
using CustomBattleBg;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValennaBattle
{
    internal static class ValennaBattleScene
    {
        private const string Owner = "ttrpg.valenna-battle";
        private const string Scene = "thumbfinger";
        private const string Action = "装填子弹、切换背景";
        private const string BackgroundFile = "battle_thumb.png";

        internal static void Register(string pluginPath)
        {
            string modRoot = Path.GetDirectoryName(Path.GetDirectoryName(pluginPath));
            string assets = Path.Combine(modRoot, "resource", "battle-scene");
            DomainApi.RegisterDomain(Owner, Scene, new DomainDefinition
            {
                DisplayName = Action,
                AssetRoot = assets,
                BackgroundPath = BackgroundFile,
                BgmPath = "Battle_Cp9_Boss_1_2.ogg",
                BgmLoop = true
            });
        }

        internal static void Play()
        {
            if (!DomainApi.PlayVisual(Owner, Scene, Action)) return;
            var layerType = typeof(DomainApi).Assembly.GetType("CustomBattleBg.BattleBgLayer");
            if (layerType == null) return;
            object layer = AccessTools.Field(layerType, "_instance")?.GetValue(null);
            FieldInfo coverUv = AccessTools.Field(layerType, "_baseUv");
            if (layer == null || AccessTools.Field(layerType, "_visual")?.GetValue(layer) is not RawImage image ||
                image == null || image.texture == null || coverUv?.FieldType != typeof(Rect)) return;
            ValennaBackgroundZoom zoom = image.GetComponent<ValennaBackgroundZoom>();
            if (zoom == null) zoom = image.gameObject.AddComponent<ValennaBackgroundZoom>();
            zoom.Attach(image, layer, coverUv);
        }
    }

    internal sealed class ValennaBackgroundZoom : MonoBehaviour
    {
        private const float BackgroundZoom = 2f;
        private const float DownwardShift = 0.1f;
        private const float SwaySpeed = 0.5f;
        private RawImage _image;
        private Texture _texture;
        private object _layer;
        private FieldInfo _coverUv;
        private float _swayAmplitude;
        private float _swayPeriod;

        internal void Attach(RawImage image, object layer, FieldInfo coverUv)
        {
            _image = image;
            _texture = image.texture;
            _layer = layer;
            _coverUv = coverUv;
            _swayAmplitude = ReadSwaySetting("SwayAmplitude", DomainConstants.BgSwayAmplitude);
            _swayPeriod = ReadSwaySetting("SwayPeriod", DomainConstants.BgSwayPeriod);
            LateUpdate();
        }

        private static float ReadSwaySetting(string name, float fallback) =>
            AccessTools.Field(typeof(CustomBattleBgPlugin), name)?.GetValue(null) is float value ? value : fallback;

        private void LateUpdate()
        {
            if (_image == null || _image.texture != _texture)
            {
                Destroy(this);
                return;
            }
            // 基于当前分辨率的静态铺图范围计算，避免叠加背景模组已写入的摇晃偏移。
            _image.uvRect = SceneUv((Rect)_coverUv.GetValue(_layer), Time.unscaledTime, _swayAmplitude, _swayPeriod);
        }

        private static Rect SceneUv(Rect background, float seconds, float amplitude, float period)
        {
            Vector2 size = background.size / BackgroundZoom;
            float maximumAmplitude = (1f - 1f / DomainConstants.BgSwayOverscan) * 0.5f;
            float phase = seconds * (2f * Mathf.PI * SwaySpeed / Mathf.Max(period, 1f));
            float sway = Mathf.Sin(phase) * Mathf.Clamp(amplitude, 0f, maximumAmplitude);
            // UV 向上取样，画面内容向下移动可见高度的 10%。
            Vector2 offset = new Vector2(sway, size.y * DownwardShift);
            return new Rect(background.center + offset - size * 0.5f, size);
        }
    }
}
