using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game;
using HarmonyLib;
using MOD;
using UnityEngine;

namespace ValennaBattle
{
    internal static class ValennaAppearance
    {
        private const string ModelKey = "valenna_test_model";
        private const string PortraitKey = "valenna_portrait";
        private const float PosePixelsPerUnit = 550f;

        private static readonly Dictionary<string, ValennaPose> Poses = ValennaPoses.All
            .Concat(ValennaPoses.IdleCycle.Skip(1)).ToDictionary(pose => pose.Key);

        internal static Vector3? LightPoint(SpriteRenderer body, bool eye) =>
            PosePoint(body, eye ? (pose => pose.Eye) : (pose => pose.Sheath));

        internal static Vector3? GunPoint(SpriteRenderer body) => PosePoint(body, pose => pose.Gun);

        // 先用平剑切图的剑尖算好落点，再连续移动到该位置。
        internal static Vector3 ExecutionBladeTip(SpriteRenderer body)
        {
            ValennaPose pose = ValennaPoses.ExecutionAim;
            ValennaPoint tip = ValennaPoses.ExecutionBladeTip;
            return WorldPoint(body, new Vector2(tip.X - pose.FootX, pose.FootY - tip.Y) / PosePixelsPerUnit);
        }

        private static Vector3? PosePoint(SpriteRenderer body, System.Func<ValennaPose, ValennaPoint?> select)
        {
            Sprite sprite = body.sprite;
            if (sprite == null || !Poses.TryGetValue(sprite.name, out ValennaPose pose)) return null;
            ValennaPoint? point = select(pose);
            if (!point.HasValue) return null;
            Vector2 position = new Vector2(point.Value.X, sprite.rect.height - point.Value.Y) - sprite.pivot;
            return WorldPoint(body, position / sprite.pixelsPerUnit);
        }

        private static Vector3 WorldPoint(SpriteRenderer body, Vector2 position)
        {
            if (body.flipX) position.x = -position.x;
            if (body.flipY) position.y = -position.y;
            return body.transform.TransformPoint(position);
        }

        [HarmonyPatch(typeof(ResourceLibraryData), nameof(ResourceLibraryData._LoadResByExtra))]
        private static class SpriteImport
        {
            private static void Postfix(ResourceData __0, int __1, ref Task<object> __result)
            {
                if (__0 == null || string.IsNullOrEmpty(__0.Key)) return;
                if (__1 == (int)ETextureReferenceType.RoleModelAnim && Poses.TryGetValue(__0.Key, out ValennaPose pose))
                    __result = ApplyLayout(__result, pose);
            }

            private static async Task<object> ApplyLayout(Task<object> load, ValennaPose pose)
            {
                object result = await load;
                if (!(result is Sprite sprite) || sprite == null) return result;

                Rect rect = sprite.rect;
                var pivot = new Vector2(pose.FootX / rect.width, 1f - pose.FootY / rect.height);
                Sprite laidOut = Sprite.Create(sprite.texture, rect, pivot, PosePixelsPerUnit, 0, SpriteMeshType.FullRect);
                if (laidOut == null) return result;
                laidOut.name = pose.Key;
                laidOut.texture.filterMode = FilterMode.Bilinear;
                Object.Destroy(sprite);
                return laidOut;
            }
        }

        [HarmonyPatch(typeof(PlaySpriteAnimation), nameof(PlaySpriteAnimation.Play),
            new[] { typeof(string), typeof(float) })]
        private static class KeepPoseInterval
        {
            private static void Prefix(PlaySpriteAnimation __instance, string __0, out float __state)
            {
                __state = 0f;
                if (__instance.CurrentPlayAnimKey == __0) return;
                SpriteAnimationData clip = __instance.AnimationList?.Find(entry => entry?.Key == __0);
                if (clip == null || clip.IsUseOldMode || clip.SpriteDatas == null || clip.SpriteDatas.Count == 0) return;
                Sprite first = clip.SpriteDatas[0]?.Sprite;
                if (first != null && Poses.ContainsKey(first.name))
                    __state = clip.IntervalTime;
            }

            private static void Postfix(PlaySpriteAnimation __instance, float __state, ref float __result)
            {
                if (__state <= 0f || __result == 0f) return;
                SpriteAnimationData clip = __instance.CurrentAnimation;
                if (clip == null || clip.IntervalTime <= 0f) return;

                // 原生 Play 会把新格式序列的帧间隔重置为 0.1 秒。
                float ratio = __state / clip.IntervalTime;
                clip.IntervalTime = __state;
                __instance.CurDuration *= ratio;
                __result *= ratio;
            }
        }

        [HarmonyPatch(typeof(RTERoleModelConfig), nameof(RTERoleModelConfig.LoadRoleModel))]
        private static class FaceDirection
        {
            private static void Postfix(RoleModelData __0, GameObject __1)
            {
                if (__0.ModelName != ModelKey) return;
                SpriteRenderer body = __1.GetComponent<RoleModel>()?.SpriteRender;
                if (body != null) body.flipX = true;
            }
        }

        internal static void ApplyAllyFacing(BattleRole role, bool isAlly)
        {
            if (!isAlly || role?.Data == null) return;
            SpriteRenderer body = role.Model?.SpriteRender;
            if (body == null) return;
            if (role.Data.Custom_RoleModelName == ModelKey ||
                (body.sprite != null && Poses.ContainsKey(body.sprite.name)))
                body.flipX = true;
        }

        [HarmonyPatch(typeof(MOD_Entity_BattleRoom), nameof(MOD_Entity_BattleRoom.SetRole),
            new[] { typeof(BattleRole), typeof(bool), typeof(int), typeof(int) })]
        private static class PlaceAlly
        {
            private static void Postfix(BattleRole __0, bool __1, ref Task __result)
            {
                if (__1 && __0 != null)
                    __result = FaceAfterPlacement(__result, __0);
            }

            private static async Task FaceAfterPlacement(Task original, BattleRole role)
            {
                await original;
                ApplyAllyFacing(role, true);
            }
        }

        [HarmonyPatch(typeof(RoleData), nameof(RoleData.ReloadConfigData))]
        private static class ReloadPortrait
        {
            private static void Postfix(RoleData __instance)
            {
                if (__instance.ConfigId == ValennaPlugin.PartnerId &&
                    __instance.Custom_IconReference?.Key == "icon_head_hailinna_default" &&
                    __instance.ConfigData is PartnerData partner && partner.HeadCoverIcon?.Key == PortraitKey)
                    __instance.Custom_IconReference = partner.HeadCoverIcon.Clone();
            }
        }

        [HarmonyPatch(typeof(RoleData), nameof(RoleData.ProfessionName), MethodType.Getter)]
        private static class PartnerProfessionName
        {
            private static readonly LocalizationKeyData Profession = new LocalizationKeyData
            {
                InputText = "蜘蛛巢 拇指父辈"
            };

            private static void Postfix(RoleData __instance, ref LocalizationKeyData __result)
            {
                if (__instance is PartnerRoleData partner && partner.PartnerId == ValennaPlugin.PartnerId)
                    __result = Profession;
            }
        }
    }
}
