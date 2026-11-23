using System;
using UnityEngine;

namespace ValennaBattle
{
    // 原生动画在 Update 换切图。LateUpdate 在同一次绘制前落脚并取残影。
    internal sealed class StrikeFootwork : MonoBehaviour
    {
        internal Action BeforeRender;

        private void LateUpdate() => BeforeRender?.Invoke();

        // 单位为人物身高，正数朝向对手。仅在真正显示了另一张切图时取一步。
        internal static float Step(string action, string previous, string current)
        {
            if (current == previous) return 0f;
            return current switch
            {
                "valenna_pose_aim_double" => 0.15f,
                "valenna_pose_aim" when previous == "valenna_pose_aim_double" => -0.055f,
                "valenna_pose_aim" => 0.11f,
                "valenna_pose_thrust_out1" => -0.06f,
                "valenna_pose_thrust_out2" when action == "valenna_point_recoil" => -0.20f,
                "valenna_pose_thrust_out2" => -0.12f,
                "valenna_pose_finish_guard" => -0.065f,
                "valenna_pose_finish_turn" => -0.075f,
                "valenna_pose_finish_low" => -0.045f,
                "valenna_pose_finish_dash" => 0.07f,
                "valenna_pose_finish_cut" => 0.15f,
                "valenna_pose_finish_spin" => 0.13f,
                "valenna_pose_dash2" => -0.035f,
                "valenna_pose_rise" when action == "valenna_elephant_spin" => -0.06f,
                "valenna_pose_rise" => 0.14f,
                "valenna_pose_cross" when action == "valenna_elephant_spin" => 0.055f,
                "valenna_pose_cross" => -0.045f,
                "valenna_pose_cross_out1" => 0.16f,
                "valenna_pose_cross_out2" => 0.025f,
                _ => 0f
            };
        }
    }
}
