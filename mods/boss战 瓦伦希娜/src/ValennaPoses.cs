using System;

namespace ValennaBattle
{
    public readonly struct ValennaPoint
    {
        public readonly int X;
        public readonly int Y;

        internal ValennaPoint(int x, int y) { X = x; Y = y; }
    }

    public readonly struct ValennaPose
    {
        public readonly string Name;
        public readonly string File;
        public readonly int FootX;
        public readonly int FootY;
        public readonly string LegacyFile;
        public readonly ValennaPoint? Eye;
        public readonly ValennaPoint? Sheath;
        public readonly ValennaPoint? Gun;

        internal ValennaPose(string name, string file, int footX, int footY, string legacyFile = null,
            ValennaPoint? eye = null, ValennaPoint? sheath = null, ValennaPoint? gun = null)
        {
            Name = name;
            File = file;
            FootX = footX;
            FootY = footY;
            LegacyFile = legacyFile;
            Eye = eye;
            Sheath = sheath;
            Gun = gun;
        }

        public string Key => "valenna_pose_" + Name;
    }

    // 坐标以未镜像单图的左上角为原点。腾空帧和长刀帧仍以身体下方的落点为准。
    public static class ValennaPoses
    {
        public const int IdleBreathFrames = 24;
        public const float IdleBreathInterval = 0.125f;
        private const string IdleBreathPrefix = "idle_breath_";

        private static readonly ValennaPose Idle = new ValennaPose("idle", "idle_#596.png", 199, 473, "idle.png",
            new ValennaPoint(187, 119), new ValennaPoint(279, 249));
        internal static readonly ValennaPose ExecutionAim = new ValennaPose("finish_aim", "skill3_22_#548.png",
            695, 467, gun: new ValennaPoint(532, 161));
        internal static readonly ValennaPoint ExecutionBladeTip = new ValennaPoint(5, 161);

        public static readonly ValennaPose[] All =
        {
            Idle,
            new ValennaPose("dash1", "mov.png", 220, 399,
                eye: new ValennaPoint(86, 124), sheath: new ValennaPoint(246, 190)),
            new ValennaPose("guard", "gud_#346.png", 240, 566, "gud.png", eye: new ValennaPoint(210, 235)),
            new ValennaPose("dodge", "miss.png", 210, 477, eye: new ValennaPoint(207, 119)),
            new ValennaPose("hurt", "dmg_#100.png", 206, 470, "dmg.png"),
            new ValennaPose("fallen", "dead_#168.png", 166, 409, "dead.png"),

            new ValennaPose("draw", "skill1_2.png", 201, 474,
                eye: new ValennaPoint(189, 119), sheath: new ValennaPoint(279, 249)),
            new ValennaPose("dash2", "skill1_3.png", 253, 563,
                eye: new ValennaPoint(135, 251), sheath: new ValennaPoint(324, 343)),
            new ValennaPose("rise", "skill1_4.png", 285, 660),
            new ValennaPose("rise_end", "skill1_5_#471.png", 300, 660),
            new ValennaPose("cut_lower", "skill1_7.png", 452, 478),

            new ValennaPose("thrust_ready", "skill2_2.png", 201, 474),
            new ValennaPose("aim", "skill2_3-5.png", 572, 362, gun: new ValennaPoint(301, 134)),
            new ValennaPose("aim_double", "skill2_4-6.png", 657, 362, gun: new ValennaPoint(303, 70)),
            new ValennaPose("thrust_out1", "skill2_7.png", 388, 420, gun: new ValennaPoint(428, 214)),
            new ValennaPose("thrust_out2", "skill2_8_#418.png", 390, 391, gun: new ValennaPoint(421, 190)),
            new ValennaPose("cross", "skill2_9.png", 193, 452),
            new ValennaPose("cross_out1", "skill2_10.png", 327, 495),
            new ValennaPose("cross_out2", "skill2_11.png", 282, 495),

            new ValennaPose("finish_back1", "skill3_2.png", 423, 476, sheath: new ValennaPoint(294, 313)),
            new ValennaPose("finish_back2", "skill3_3.png", 423, 476, sheath: new ValennaPoint(294, 313)),
            new ValennaPose("finish_wind1", "skill3_4_#134.png", 304, 551),
            new ValennaPose("finish_wind2", "skill3_4_#309.png", 304, 551),
            new ValennaPose("finish_cut", "skill3_4.png", 264, 425),
            new ValennaPose("finish_turn", "skill3_5_#190.png", 430, 460),
            new ValennaPose("finish_guard", "skill3_9.png", 211, 443),
            new ValennaPose("finish_dash", "skill3_10.png", 316, 415),
            new ValennaPose("finish_spin", "skill3_11.png", 421, 473),
            new ValennaPose("finish_low", "skill3_12_#632.png", 348, 466),
            new ValennaPose("finish_retreat1", "处置补帧/skill3_16.png", 392, 489, "skill3_16.png"),
            new ValennaPose("finish_retreat2", "处置补帧/skill3_17.png", 348, 450, "skill3_17.png"),
            new ValennaPose("finish_stand1", "处置补帧/skill3_18.png", 408, 467, "skill3_18.png"),
            new ValennaPose("finish_stand2", "处置补帧/skill3_19.png", 419, 467, "skill3_19.png"),
            new ValennaPose("finish_stand3", "处置补帧/skill3_20.png", 421, 467, "skill3_20.png"),
            new ValennaPose("finish_kick", "处置补帧/skill3_21.png", 698, 467, "skill3_21.png"),
            ExecutionAim,
            new ValennaPose("finish_fire", "skill3_23_#22.png", 681, 467, gun: new ValennaPoint(532, 161))
        };

        public static readonly ValennaPose[] IdleCycle = BuildIdleCycle();

        internal static bool IsIdleFrame(string key) => key == Idle.Key ||
            key.StartsWith("valenna_pose_" + IdleBreathPrefix, StringComparison.Ordinal);

        internal static (float X, float Y) IdleBreathShift(int index)
        {
            double phase = 2.0 * Math.PI * index / IdleBreathFrames;
            return ((float)(Math.Sin(phase) * Idle.FootY * 0.0018),
                (float)((1.0 - Math.Cos(phase)) * Idle.FootY * 0.0065));
        }

        internal static float IdleBreathWeight(float x, float y)
        {
            float height = Idle.FootY;
            float upperBody = 1f - Smooth(0.30f * height, 0.58f * height, y);
            float weapon = Smooth(0.30f * height, 0.39f * height, y)
                * Smooth(Idle.FootX + 0.11f * height, Idle.FootX + 0.165f * height, x);
            return upperBody * (1f - weapon);
        }

        private static ValennaPose[] BuildIdleCycle()
        {
            // 用原站姿作循环起点，所有派生帧保持同一画布和脚点。
            var poses = new ValennaPose[IdleBreathFrames];
            poses[0] = Idle;
            for (int index = 1; index < poses.Length; index++)
            {
                var shift = IdleBreathShift(index);
                poses[index] = new ValennaPose(IdleBreathPrefix + index.ToString("D2"), Idle.File, Idle.FootX, Idle.FootY,
                    eye: MoveIdlePoint(Idle.Eye.Value, shift), sheath: MoveIdlePoint(Idle.Sheath.Value, shift));
            }
            return poses;
        }

        private static ValennaPoint MoveIdlePoint(ValennaPoint point, (float X, float Y) shift)
        {
            float weight = IdleBreathWeight(point.X, point.Y);
            return new ValennaPoint((int)Math.Round(point.X + shift.X * weight),
                (int)Math.Round(point.Y + shift.Y * weight));
        }

        private static float Smooth(float left, float right, float value)
        {
            float t = Math.Max(0f, Math.Min(1f, (value - left) / (right - left)));
            return t * t * (3f - 2f * t);
        }
    }
}
