// 固定外观参数或按调查员保存的参数；原点始终是游戏原本的特效挂点。
using System;
using HarmonyLib;
using MOD;
using UnityEngine;

namespace XinEditor
{
    [HarmonyPatch(typeof(RoleModel), "PlayFx", new Type[] {
        typeof(string), typeof(string), typeof(bool), typeof(ERolePointType) })]
    internal static class Patch_RoleModel_PlayHeartFx
    {
        private static void Postfix(RoleModel __instance, string effectName, bool isLoop)
        {
            if (!XinEditorPlugin.EnableHeartVisuals || !isLoop || __instance == null ||
                (effectName != "xin_glow" && effectName != XinEditorPlugin.FrameEffectName)) return;
            try
            {
                if (__instance.FxList == null) return;
                foreach (GameObject fx in __instance.FxList)
                {
                    if (fx == null || fx.GetComponent<HeartVisualPlacement>() != null) continue;
                    RTECustomAnimConfig anim = fx.GetComponent<RTECustomAnimConfig>();
                    if (anim == null || anim.ConfigData == null || anim.ConfigData.EffectName != effectName) continue;
                    fx.AddComponent<HeartVisualPlacement>().Install(__instance);
                }
            }
            catch (Exception e) { XinEditorPlugin.LogError("挂载心外观参数失败：" + e.Message); }
        }
    }

    internal sealed class HeartVisualPlacement : MonoBehaviour
    {
        private RoleModel _model;
        private HeartVisualSettings _settings;
        private string _roleKey;
        private int _revision = -1;

        internal void Install(RoleModel model) { _model = model; }

        private void LateUpdate()
        {
            if (_model == null) { enabled = false; return; }
            try
            {
                HeroRoleData hero = _model.Role != null ? _model.Role.Data as HeroRoleData : null;
                string key = HeartVisualRoleKey.Resolve(hero);
                HeartVisualStore store = XinEditorPlugin.VisualStore;
                int revision = store != null ? store.Revision : 0;
                if (_settings == null || key != _roleKey || revision != _revision)
                {
                    _roleKey = key;
                    _revision = revision;
                    _settings = store != null ? store.Get(key) : new HeartVisualSettings();
                    XinEditorPlugin.LogInfo(string.Format(
                        "心外观参数：模型={0}，调查员={1}，位置=({2:F3},{3:F3})，缩放=({4:F3},{5:F3})",
                        _model.gameObject.name, key ?? "默认", _settings.X, _settings.Y,
                        _settings.ScaleX, _settings.ScaleY));
                }
                // 在初始化后设置实例，纠正游戏Start/重载配置造成的覆盖。
                // 预览与战斗均使用挂点局部坐标，不再自动拟合体型或移动坐标原点。
                transform.localPosition = new Vector3(_settings.X, _settings.Y, 0f);
                transform.localScale = new Vector3(_settings.ScaleX, _settings.ScaleY, 1f);
            }
            catch (Exception e)
            {
                enabled = false;
                XinEditorPlugin.LogError("心外观参数应用已停止：" + e.Message);
            }
        }
    }
}
