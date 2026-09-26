using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AttackTargetVisualizer
{
    /// <summary>
    /// 「可视化攻击目标」插件入口。
    ///
    /// 干两件事：
    ///   · 在战斗界面上多铺一层画布，把"谁要打谁"画成箭头（ArrowLayer 那套）；
    ///   · 私货特质（百合花 / 白毛少女 / 灰暗孤影 / 记忆的双剑 + 茉莉专属武器「葬花」）的逻辑，
    ///     2026-09-22 从「自定义心」的 XinEditor.dll 拆到这里，见 SecretTraits.cs / ZangHua.cs。
    ///
    /// 箭头那部分不改游戏原有界面、不改数据、不碰存档，拔掉插件就等于没装；
    /// 私货那部分只对自己那几个编号（88xxxx）动手。
    /// </summary>
    [BepInPlugin(Guid, "可视化攻击目标", "0.2.0")]
    public class AttackTargetPlugin : BaseUnityPlugin
    {
        public const string Guid = "codex.depersonal.attacktargetvisualizer";

        /// <summary>
        /// 箭头画布的排序值。游戏自己的行动槽牌子在 "UI" 层用 0，
        /// 我们取 -1 → 箭头压在战斗画面之上、牌子之下（跟边狱巴士一个观感）。
        /// </summary>
        private const int SortingOrder = -1;

        internal static ManualLogSource Log;

        private ArrowLayer _layer;
        private bool _createFailed;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("插件已加载（" + Guid + "）");

            try
            {
                Harmony harmony = new Harmony(Guid);
                SecretRuntime.Install(harmony);
                LogInfo("私货特质钩子挂载结束");
            }
            catch (Exception e)
            {
                LogError("挂私货特质钩子失败：" + e);
            }
        }

        private void Update()
        {
            SecretRuntime.Tick();   // 私货：每帧推进排队的回击 / 段间缓冲，定时补监听与英文文案

            if (_layer != null)
            {
                return;
            }
            if (_createFailed)
            {
                return;
            }
            TryCreateLayer();
        }

        private void TryCreateLayer()
        {
            if (!PrefabSingleton<UIRoot>.HasInstance)
            {
                return;   // 界面还没起来，等下一帧
            }

            UIRoot uiRoot = PrefabSingleton<UIRoot>.Instance;
            if (uiRoot == null || uiRoot.Trans_ChildElement == null)
            {
                return;
            }

            try
            {
                // 注意：UI 节点必须带 RectTransform。用 new GameObject() 建出来的是普通 Transform，
                // 后面设锚点会直接空引用崩掉（踩过一次）。所以两个节点都在建的时候就把 RectTransform 带上。
                GameObject go = new GameObject("AttackTargetArrowLayer", typeof(RectTransform));
                go.transform.SetParent(uiRoot.Trans_ChildElement, false);

                Canvas canvas = go.AddComponent<Canvas>();
                go.AddComponent<CanvasScaler>();
                // 跟游戏的行动槽牌子用同一套画布设置（Screen Space - Camera + 同一个相机）
                UIRoot.AddCanvas(go, SortingOrder);
                canvas.sortingOrder = SortingOrder;

                GameObject child = new GameObject("Arrows", typeof(RectTransform));
                child.transform.SetParent(go.transform, false);
                RectTransform rt = (RectTransform)child.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;

                ArrowGraphic graphic = child.AddComponent<ArrowGraphic>();
                graphic.raycastTarget = false;
                graphic.color = Color.white;

                _layer = go.AddComponent<ArrowLayer>();
                _layer.Graphic = graphic;
                _layer.Canvas = canvas;

                Log.LogInfo("箭头画布已建立（sortingOrder=" + SortingOrder + "）");
            }
            catch (Exception e)
            {
            _createFailed = true;
                Log.LogError("建立箭头画布失败：" + e);
            }
        }

        internal static void LogInfo(string msg)
        {
            if (Log != null)
            {
                Log.LogInfo(msg);
            }
        }

        internal static void LogError(string msg)
        {
            if (Log != null)
            {
                Log.LogError(msg);
            }
        }
    }
}
