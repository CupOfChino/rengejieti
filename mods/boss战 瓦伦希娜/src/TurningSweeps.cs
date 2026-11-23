using System;
using UnityEngine;

namespace ValennaBattle
{
    internal sealed partial class StrikeEffects
    {
        internal void TurningSweeps(StrikeCue cue, Func<Vector3> center)
        {
            if (!HasTextured) return;

            switch (cue.Pose)
            {
                // 890 帧，压低扫过后另一侧刀锋转入低位。
                case "valenna_rabbit_sweep":
                    TurnSweep(center, cue.Scale * 0.90f, 0.168f, 0.91f,
                        new SweepShape(-54f, 1.14f, 0.64f, elevation: -0.18f, speed: 2.08f,
                            mirrorX: true, forward: 0.02f, travel: 0.22f, lift: -0.05f, turn: 36f, motionTime: 0.12f));
                    break;
                // 996–997 帧，低身回转后从另一侧翻起，接末段的横刃。
                case "valenna_deer_spin":
                    TurnSweep(center, cue.Scale * 0.91f, 0.120f, 0.87f,
                        new SweepShape(20f, 1.09f, 0.85f, true, elevation: -0.10f, speed: 2.17f,
                            forward: 0.06f, travel: 0.19f, lift: 0.11f, turn: 46f, motionTime: 0.10f));
                    break;
                // 1267–1268 帧，两侧亮边接在第一次转身及压低横斩上。
                case "valenna_elephant_first_sweep":
                    TurnSweep(center, cue.Scale * 0.89f, 0.170f, 0.87f,
                        new SweepShape(-35f, 1.19f, 0.63f, true, elevation: -0.14f, speed: 2.18f,
                            mirrorX: true, forward: 0.08f, travel: 0.23f, lift: -0.09f, turn: 45f, motionTime: 0.11f));
                    break;
                // 1279–1280 帧，竖剑外的亮弧随身体下压翻向身前。
                case "valenna_elephant_spin":
                    TurnSweep(center, cue.Scale * 0.86f, 0.078f, 0.83f,
                        new SweepShape(-19f, 1.11f, 0.74f, true, elevation: -0.16f, speed: 2.15f,
                            forward: 0.04f, travel: 0.13f, lift: -0.04f, turn: 48f, motionTime: 0.11f));
                    break;
                // 1288–1293 帧，第二次转身先绕出一轮，从斜弧立起再接终刀。
                case "valenna_elephant_sweep":
                    TurnSweep(center, cue.Scale * 0.78f, 0.078f, 0.94f,
                        new SweepShape(-28f, 1.09f, 0.95f, true, elevation: 0.02f, speed: 2.34f,
                            mirrorX: true, forward: -0.03f, travel: 0.15f, lift: 0.08f, turn: 86f, motionTime: 0.12f));
                    break;
                // 1418–1422 帧，立剑亮环出现前，低处还扫过一圈。
                case "valenna_finish_turn":
                    TurnSweep(center, cue.Scale * 0.86f, 0.146f, 0.85f,
                        new SweepShape(-35f, 1.12f, 0.68f, true, elevation: -0.18f, speed: 2.12f,
                            mirrorX: true, forward: 0.07f, travel: 0.14f, lift: -0.06f, turn: 48f, motionTime: 0.12f),
                        execution: true);
                    break;
                // 1440–1441 帧，转身贴近的一轮由角色侧面收向脚前。
                case "valenna_finish_turn_slash":
                    TurnSweep(center, cue.Scale * 0.87f, 0.151f, 0.87f,
                        new SweepShape(23f, 1.10f, 0.74f, elevation: -0.11f, speed: 2.20f,
                            mirrorX: true, forward: -0.02f, travel: 0.22f, lift: -0.09f, turn: -54f, motionTime: 0.10f),
                        execution: true);
                    break;
                // 1446 帧，竖刃向下收时，低处亮刃反向旋到脚前。
                case "valenna_finish_sweep":
                    TurnSweep(center, cue.Scale * 0.84f, 0.132f, 0.88f,
                        new SweepShape(-47f, 1.15f, 0.59f, true, elevation: -0.20f, speed: 2.27f,
                            forward: 0.11f, travel: 0.23f, lift: -0.04f, turn: 35f, motionTime: 0.11f),
                        execution: true);
                    break;
                // 1462–1465 帧，身体压低旋转时，剑刃沿脚前回卷。
                case "valenna_finish_spin":
                    TurnSweep(center, cue.Scale * 0.89f, 0.139f, 0.85f,
                        new SweepShape(25f, 1.11f, 0.61f, true, elevation: -0.20f, speed: 2.24f,
                            mirrorX: true, forward: 0.05f, travel: 0.22f, lift: 0.02f, turn: -46f, motionTime: 0.11f),
                        execution: true);
                    break;
            }
        }

        private void TurnSweep(Func<Vector3> center, float scale, float delay, float opacity, SweepShape shape,
            bool execution = false)
        {
            Sprite[] frames = execution ? StrikeArt.ExecutionCompanion : StrikeArt.AttackCompanion;
            SweepSprite(SweepOrigin(center(), shape), shape, scale, new Color(1f, 0.95f, 0.88f, opacity),
                delay, companion: true, frames: frames, startAt: () => SweepOrigin(center(), shape));
        }
    }
}
