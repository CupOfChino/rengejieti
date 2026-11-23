using System;
using UnityEngine;

namespace ValennaBattle
{
    internal sealed partial class StrikeEffects
    {
        internal void RepeatThrust(StrikeCue cue, Vector3 origin, Vector3 hit, Vector3 direction, float age,
            Func<Vector3> sourcePosition)
        {
            if (!HasTextured) return;

            bool reverse = Vector3.Dot(direction, _right) < 0f;
            Vector3 center = Vector3.Lerp(origin, hit, 0.58f);
            Vector3 sourceAtSchedule = sourcePosition();
            float scale = cue.Scale;
            // 亮起时采一次人物的实际位置。靶点和已经扫出的光迹都留在世界坐标。
            float originWeight = cue.SplitThrust || cue.Pose == "valenna_elephant_volley" ? 0f : 0.42f;

            Func<Vector3> AtLaunch(Vector3 point, float weight)
            {
                Vector3? anchored = null;
                return () =>
                {
                    anchored ??= point + (sourcePosition() - sourceAtSchedule) * weight;
                    return anchored.Value;
                };
            }

            void Pulse(string name, Sprite[] frames, float delay, float forward, float lift,
                float width, float height, float angle, float speed, float opacity)
            {
                var point = AtLaunch(center + (direction * forward + _up * lift) * _height, originWeight);
                PlayFrames(name, frames, () => point(), width * scale, height * scale, reverse ? -angle : angle,
                    delay - age, 7, new Color(1f, 0.94f, 0.85f, opacity), speed, reverse: reverse);
            }

            void Finish(string name, float delay, float forward, float lift, float width, float angle)
            {
                var point = AtLaunch(center + (direction * forward + _up * lift) * _height, originWeight);
                PlayFrames(name, StrikeArt.LongThrust, () => point(), width * scale, 0.86f * scale,
                    reverse ? -angle : angle, delay - age, 8, Color.white, 1.58f, reverse: reverse);
                ThrustWhiteStreak(point, scale, width * 0.95f, angle, delay - age + 0.013f, reverse);
            }

            // 短续刺和再次出手交替，最后沿中线伸出较长的白金刃芯。
            if (cue.TwinThrust)
            {
                Pulse("尖端打击下路快刺", StrikeArt.Shot, delay: 0.102f, forward: 0.21f, lift: -0.22f,
                    width: 3.32f, height: 0.42f, angle: 4.3f, speed: 1.48f, opacity: 0.91f);
                Pulse("尖端打击再次双刺", StrikeArt.TwinThrust, delay: 0.193f, forward: 0.08f, lift: 0.08f,
                    width: 3.74f, height: 0.80f, angle: -3.3f, speed: 1.48f, opacity: 0.91f);
                Finish("尖端打击连刺长光", delay: 0.310f, forward: -0.22f, lift: 0.06f, width: 4.58f, angle: -0.9f);
                return;
            }

            if (cue.SplitThrust)
            {
                Pulse("处置上段续刺", StrikeArt.Shot, delay: 0.084f, forward: 0.22f, lift: 0.29f,
                    width: 3.22f, height: 0.38f, angle: -4.7f, speed: 1.63f, opacity: 0.84f);
                Pulse("处置下段续刺", StrikeArt.LongThrust, delay: 0.166f, forward: 0.48f, lift: -0.25f,
                    width: 3.56f, height: 0.53f, angle: 4.1f, speed: 1.66f, opacity: 0.79f);
                return;
            }

            switch (cue.Pose)
            {
                case "valenna_bullet_thrust":
                    Pulse("贯穿起手续刺", StrikeArt.Shot, delay: 0.084f, forward: 0.18f, lift: -0.21f,
                        width: 3.21f, height: 0.46f, angle: 3.7f, speed: 1.56f, opacity: 0.90f);
                    break;
                case "valenna_bullet_double":
                    Pulse("贯穿上段双刺", StrikeArt.TwinThrust, delay: 0.105f, forward: 0.13f, lift: 0.13f,
                        width: 3.53f, height: 0.81f, angle: -2.8f, speed: 1.63f, opacity: 0.87f);
                    Pulse("贯穿下段续刺", StrikeArt.Shot, delay: 0.208f, forward: 0.24f, lift: -0.21f,
                        width: 3.62f, height: 0.47f, angle: 3.6f, speed: 1.68f, opacity: 0.92f);
                    break;
                case "valenna_bullet_shot":
                    Pulse("贯穿回斩后续刺", StrikeArt.Shot, delay: 0.086f, forward: 0.12f, lift: 0.19f,
                        width: 3.54f, height: 0.47f, angle: -3.2f, speed: 1.43f, opacity: 0.91f);
                    Finish("贯穿连刺长光", delay: 0.185f, forward: -0.22f, lift: 0.025f, width: 4.48f, angle: 0.85f);
                    break;
                case "valenna_point_pass":
                    var point = AtLaunch(Vector3.Lerp(origin, hit, 0.50f) + direction * (_height * 0.36f), 0.50f);
                    ThrustWhiteStreak(point, scale, 4.50f, -3f, 0.020f - age, reverse);
                    break;
                case "valenna_elephant_volley":
                    const float upperDelay = 0.101f;
                    const float lowerDelay = 0.205f;
                    const float upperAngle = -4.1f;
                    const float lowerAngle = 3.7f;
                    Pulse("解象上段续刺", StrikeArt.Shot, delay: upperDelay, forward: 0.21f, lift: 0.22f,
                        width: 3.48f, height: 0.50f, angle: upperAngle, speed: 1.42f, opacity: 0.94f);
                    Pulse("解象下段续刺", StrikeArt.Shot, delay: lowerDelay, forward: 0.48f, lift: -0.27f,
                        width: 3.90f, height: 0.45f, angle: lowerAngle, speed: 1.38f, opacity: 0.90f);
                    Flash("解象上段续刺枪芒", StrikeArt.Flare, origin + _up * (_height * 0.07f), Vector3.zero,
                        Tint(Core, 0.83f), 0.35f * scale, 0.29f * scale, reverse ? -upperAngle : upperAngle, 0.088f, upperDelay - age, 7);
                    Flash("解象下段续刺枪芒", StrikeArt.Flare, origin - _up * (_height * 0.06f), Vector3.zero,
                        Tint(Core, 0.78f), 0.33f * scale, 0.27f * scale, reverse ? -lowerAngle : lowerAngle, 0.085f, lowerDelay - age, 7);
                    break;
            }
        }

        private void ThrustWhiteStreak(Func<Vector3> point, float scale, float width, float angle, float delay,
            bool reverse)
        {
            SpriteRenderer streak = NewSprite("末刺白金亮芯", StrikeArt.Streak, 9);
            if (reverse) streak.flipX = !streak.flipX;
            Add(streak.gameObject, 0.170f, t =>
            {
                float reach = Mathf.Lerp(0.81f, 1f, Mathf.SmoothStep(0f, 1f, t / 0.25f));
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.46f) / 0.54f));
                Vector3 center = point() + _up * (_height * (0.037f * scale));
                streak.color = new Color(1f, 0.98f, 0.91f, 0.98f * fade);
                Place(streak, center, width * scale * reach, 0.142f * scale * Mathf.Lerp(1f, 0.52f, t),
                    reverse ? -angle : angle);
            }, delay);
        }
    }
}
