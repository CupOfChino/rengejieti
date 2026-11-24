namespace ValennaBattle
{
    public enum StrikeBeat
    {
        Crosscut,
        Rising,
        Orbit,
        Thrust,
        Muzzle,
        Volley
    }

    public sealed class StrikeCue
    {
        public readonly StrikeBeat Beat;
        public readonly float Windup;
        public readonly float Hold;
        public readonly float Scale;
        public readonly bool Damage;
        public readonly bool Ring;
        public readonly string Pose;
        public readonly float Preparation;
        public readonly bool Sweep;
        public readonly bool SplitThrust;
        public readonly bool RaisedBlade;
        public readonly SweepShape? SweepStyle;
        public readonly float ThrustTravel;
        public readonly float SweepLead;
        public readonly SweepShape? CompanionSweep;
        public readonly float CompanionDelay;
        public readonly bool TwinThrust;
        public readonly bool LongThrust;
        public readonly float PassDistance;
        public readonly float PassMoveTime;
        public readonly float RecoilTime;
        public readonly string PauseBeforePose;
        public readonly bool PauseAfter;
        public readonly int MotionFlashFrames;

        internal StrikeCue(StrikeBeat beat, float windup, float hold, float scale = 1f,
            bool damage = true, bool ring = false, string pose = null, float preparation = 0f,
            bool sweep = true, bool splitThrust = false, bool raisedBlade = false,
            SweepShape? sweepStyle = null, float thrustTravel = 0.44f,
            float sweepLead = StrikeProfiles.SweepLead, SweepShape? companionSweep = null,
            float companionDelay = 0f, bool twinThrust = false, bool longThrust = false,
            float passDistance = 0f, string pauseBeforePose = null, bool pauseAfter = false,
            int motionFlashFrames = 0, float passMoveTime = 0f, float recoilTime = 0f)
        {
            Beat = beat;
            Windup = windup;
            Hold = hold;
            Scale = scale;
            Damage = damage;
            Ring = ring;
            Pose = pose;
            Preparation = preparation;
            Sweep = sweep;
            SplitThrust = splitThrust;
            RaisedBlade = raisedBlade;
            SweepStyle = sweepStyle;
            ThrustTravel = thrustTravel;
            SweepLead = sweepLead;
            CompanionSweep = companionSweep;
            CompanionDelay = companionDelay;
            TwinThrust = twinThrust;
            LongThrust = longThrust;
            PassDistance = passDistance;
            PassMoveTime = passMoveTime;
            RecoilTime = recoilTime;
            PauseBeforePose = pauseBeforePose;
            PauseAfter = pauseAfter;
            MotionFlashFrames = motionFlashFrames;
        }
    }

    public sealed class StrikeShow
    {
        public readonly int Id;
        public readonly string Name;
        public readonly string Description;
        public readonly StrikeCue[] Cues;
        public readonly bool Charged;

        internal StrikeShow(int id, string name, string description, StrikeCue[] cues, bool charged = false)
        {
            Id = id;
            Name = name;
            Description = description;
            Cues = cues;
            Charged = charged;
        }
    }

    public readonly struct SweepShape
    {
        public readonly float Angle;
        public readonly float Width;
        public readonly float Height;
        public readonly bool Reverse;
        public readonly float Elevation;
        public readonly float Speed;
        public readonly bool MirrorX;
        public readonly float Forward;
        public readonly float Travel;
        public readonly float Lift;
        public readonly float Turn;
        public readonly float MotionTime;

        internal SweepShape(float angle, float width, float height, bool reverse = false,
            float elevation = 0.08f, float speed = 1f, bool mirrorX = false,
            float forward = 0.15f, float travel = 0f, float lift = 0f, float turn = 0f,
            float motionTime = 0.16f)
        {
            Angle = angle;
            Width = width;
            Height = height;
            Reverse = reverse;
            Elevation = elevation;
            Speed = speed;
            MirrorX = mirrorX;
            Forward = forward;
            Travel = travel;
            Lift = lift;
            Turn = turn;
            MotionTime = motionTime;
        }
    }

    // 各段的准备和停顿用秒表示。小连闪和散落的余焰可以作为纯演出段。
    public static class StrikeProfiles
    {
        public const int TraitId = 885141;
        public const float PlaybackRate = 5f / 3f;
        public const int PrescienceTraitId = 885142;
        public const int EyeItemId = 885112;
        public const int GunbladeId = 885111;
        public const int CutlassId = 885113;
        public const int ExecutionId = 885129;
        public const int RabbitId = 885125;
        public const int BreathId = 885131;
        public const int TremorId = 885132;
        public const int BurnId = 104;
        public const int ScorchTremorId = 885134;
        public const int EyeChargeId = 885135;
        public const int EyeOverheatId = 885136;
        public const int AccelBulletId = 885137;
        public const int MaxAccelBullets = 10;
        public const int AcceleratedFutureId = 885138;
        public const int AcceleratedFutureMax = 5;
        public const int AcceleratedFuturePercent = 5;
        public const int HuntingPreyId = 885139;
        public const int HuntTraitId = 885143;
        public const int DisgraceTraitId = 885144;
        public const int VeteranTraitId = 885145;
        public const int DrinkerTraitId = 885146;
        public const int GoldenTicketTraitId = 885147;
        public const int HeartTraitId = 885155;
        public const int CigarId = 885114;
        public const int VodkaId = 885115;
        public const int HatId = 885116;
        public const int DrunkId = 885150;
        public const int TremorBurstId = 885148;
        public const int FocusId = 885154;
        public const int StunnedId = 101;
        public const float ForeseenMarkSize = 0.30f;
        public const int MaxEyeCharge = 30;
        public const int LowEyeCharge = 10;
        public const int EyeUseCost = 5;
        public const int ChargeRecovery = 5;
        public const int OverheatRecovery = 10;
        public const int SweepPixels = 360;
        public const int SweepFrames = 26;
        public const int CompanionFrames = 15;
        public const int ExecutionBrightFrames = 14;
        public const int AttackBrightFrames = 12;
        public const int ExtraBladeLightFrames = 2;
        public const int TwinThrustFrames = 10;
        public const float SweepInterval = 1f / 30f;
        public const float SweepLead = 0.050f;
        public const float SweepPoseLead = 2f * SweepInterval;
        public const float PassingThrustLead = 0.12f;
        public const float TwinWidth = 3.86f;
        public const float TwinHeight = 0.97f;
        public const float SweepSpan = 4.35f;
        public const float DrawTime = 0.24f * PlaybackRate;
        public const float ApproachTime = 0.26f;
        public const float CutApproachTime = 0.08f;
        public const float BeatPause = 0.20f * PlaybackRate;
        public const float ExecutionOpeningTime = 0.80f * PlaybackRate;
        public const float ExecutionOpeningTail = 0.30f * PlaybackRate;
        public const float ExecutionOpeningWindLead = 0.48f;
        public const float ExecutionOpeningWindDuration = 0.83f;
        public const int ExecutionOpeningFrames = 28;
        public const float ExecutionWaveDelay = ExecutionOpeningTime * (10f / 38f) - 0.08f;
        public const float ExecutionWaveAngle = 21f;
        public const float ExecutionInsertTime = 0.42f;
        public const float ExecutionInsertPoseTime = (ExecutionInsertTime + BeatPause) * (4f / 39f);
        public const float ExecutionStandTime = 0.50f * PlaybackRate;
        public const float ExecutionKnockbackTime = 0.27f;
        public const float ElephantKnockback = 0.64f;
        public const int ElephantWindFrames = 34;
        public const float ElephantWindDelay = 0.06f;
        public const float ElephantWindDuration = 1.13f;
        public const int CuttingWindFrames = 22;
        public const float ReturnTime = 0.33f;
        public const float ReturnAfterglow = 0.10f;
        public const int EyeFrames = 11;
        public const int SheathFrames = 10;
        public const float DrawGripTime = 0.09f;
        public const float EyeStart = 0.052f;
        public const float SheathStart = DrawGripTime + 0.01f;
        public const float EyeWidth = 2.88f;
        public const float EyeHeight = 0.35f;
        public const float SheathWidth = 2.07f;
        public const float SheathHeight = 0.37f;
        public const int AimFrames = 12;
        public const float MuzzleRingWidth = 0.75f;
        public const float MuzzleRingHeight = 0.49f;
        public const int WakeFrames = 16;
        public const int ShotFrames = 9;
        public const float WakeWidth = 3.72f;
        public const float WakeHeight = 1.19f;
        public const int PierceFrames = 11;
        public const int LongThrustFrames = 9;
        public const float LongThrustWidth = 4.24f;
        public const float LongThrustHeight = 0.83f;
        public const int GroundFlameFrames = 20;
        public const float GroundFlameWidth = 0.51f;
        public const float GroundFlameHeight = 1.01f;
        public const int ImpactFrames = 18;
        public const int ParryFrames = 10;
        public const float MuzzleHeight = 0.12f;
        public const float RaisedMuzzleHeight = 0.25f;
        public const float FinishMuzzleHeight = 0.285f;
        public const float WakeDelay = 0.018f;

        public static string OpeningPose(StrikeShow show) => show.Charged ? "valenna_finish_draw" : "valenna_draw_" + show.Id;

        public static string ApproachPose(StrikeShow show) =>
            show.Id == 885121 ? "valenna_cut_approach" : "valenna_dash";

        public static string ReturnPose(StrikeShow show) => show.Id switch
        {
            885121 => "valenna_cut_recover",
            885124 or RabbitId => "valenna_thrust_recover",
            885126 => "valenna_deer_recover",
            885127 => "valenna_dive_recover",
            885128 => "valenna_elephant_hold",
            ExecutionId => "valenna_finish_recover",
            _ => "valenna_recover"
        };

        public static string ActionPose(StrikeCue cue) => cue.Pose ?? (cue.Beat switch
        {
            StrikeBeat.Crosscut => "valenna_crosscut",
            StrikeBeat.Rising => "valenna_rise",
            StrikeBeat.Orbit => "valenna_orbit",
            StrikeBeat.Thrust => "valenna_thrust",
            StrikeBeat.Muzzle => "valenna_muzzle",
            StrikeBeat.Volley => "valenna_volley",
            _ => "valenna_guard"
        });

        public static SweepShape Shape(StrikeBeat beat) => beat switch
        {
            StrikeBeat.Crosscut => new SweepShape(-19f, 1.13f, 0.68f, true),
            StrikeBeat.Rising => new SweepShape(31f, 0.91f, 1.12f),
            StrikeBeat.Orbit => new SweepShape(57f, 0.95f, 0.92f),
            StrikeBeat.Thrust => new SweepShape(-8f, 1.15f, 0.47f),
            _ => new SweepShape(0f, 1f, 1f)
        };

        public static readonly StrikeShow[] Shows =
        {
            new StrikeShow(885121, "切割攻击", "首斩分开扫出高低两道刀光，余弧接到挑斩。",
                new[] {
                    new StrikeCue(StrikeBeat.Crosscut, 0.09f, 0.07f, pose: "valenna_cut_crosscut", motionFlashFrames: 1,
                        sweepStyle: new SweepShape(-28f, 1.16f, 0.94f, true, elevation: 0.14f,
                            mirrorX: true, forward: 0.04f, travel: 0.39f, turn: 24f, motionTime: 0.135f),
                        sweepLead: 0.067f, companionDelay: 0.028f,
                        companionSweep: new SweepShape(-28f, 1.28f, 0.48f, elevation: -0.22f, speed: 1.19f,
                            mirrorX: true, forward: 0.10f, travel: 0.40f, lift: -0.08f, turn: 69f, motionTime: 0.17f)),
                    new StrikeCue(StrikeBeat.Rising, 0.11f, 0.38f, 1.10f, pose: "valenna_cut_rise", pauseAfter: true, motionFlashFrames: 2,
                        sweepLead: SweepPoseLead,
                        sweepStyle: new SweepShape(17f, 1.01f, 1.03f, elevation: 0.03f, speed: 1.12f,
                            mirrorX: true, forward: -0.03f, travel: -0.12f, lift: 0.05f, turn: -14f))
                }),
            new StrikeShow(885124, "尖端打击", "首段突刺后后仰蓄势，再接两段直刺。",
                new[] {
                    new StrikeCue(StrikeBeat.Thrust, 0.14f, 0.40f, 1.04f, ring: true, twinThrust: true, motionFlashFrames: 3,
                        sweepLead: 0.100f,
                        sweepStyle: new SweepShape(-27f, 1.29f, 0.81f, true, elevation: 0.15f, speed: 1.20f,
                            mirrorX: true, forward: 0.08f, travel: 0.36f, turn: 17f)),
                    new StrikeCue(StrikeBeat.Thrust, 0.17f, 0.06f, 0.91f, damage: false,
                        pose: "valenna_point_recoil", preparation: 0.12f, sweep: false, raisedBlade: true,
                        thrustTravel: -0.24f),
                    new StrikeCue(StrikeBeat.Thrust, 0.15f, 0.34f, 1.20f, ring: true, sweep: false, raisedBlade: true,
                        pose: "valenna_point_pass", longThrust: true, passDistance: 0.89f, passMoveTime: 0.49f * (10f / 24f),
                        pauseBeforePose: "valenna_thrust_brace", pauseAfter: true, motionFlashFrames: 2)
                }),
            new StrikeShow(885127, "我很乐意用这颗子弹把你贯穿", "首刺和两次枪刃直闪结算伤害，最后冲出一圈红金余光。",
                new[] {
                    new StrikeCue(StrikeBeat.Thrust, 0.09f, 0.08f, pose: "valenna_bullet_thrust", motionFlashFrames: 3,
                        sweepStyle: new SweepShape(-24f, 1.24f, 0.89f, true, elevation: 0.17f, speed: 1.27f,
                            mirrorX: true, forward: 0.06f, travel: 0.38f, lift: 0.07f, turn: 32f, motionTime: 0.14f),
                        sweepLead: 0.116f, companionDelay: 0.047f,
                        companionSweep: new SweepShape(9f, 1.31f, 0.45f, elevation: -0.25f, speed: 1.20f,
                            forward: 0.16f, travel: 0.31f, lift: -0.04f, turn: -12f)),
                    new StrikeCue(StrikeBeat.Muzzle, 0.09f, 0.32f, 1.09f, pose: "valenna_bullet_double", motionFlashFrames: 1),
                    new StrikeCue(StrikeBeat.Crosscut, 0.08f, 0.09f, 0.88f, damage: false,
                        pose: "valenna_bullet_recoil", sweepLead: 0.11f),
                    new StrikeCue(StrikeBeat.Muzzle, 0.08f, 0.33f, 1.11f, pose: "valenna_bullet_shot", motionFlashFrames: 1),
                    new StrikeCue(StrikeBeat.Rising, 0.08f, 0.09f, 0.86f, damage: false,
                        pose: "valenna_bullet_recoil", sweepLead: 0.11f),
                    new StrikeCue(StrikeBeat.Thrust, 0.15f, 0.70f, 1.31f, damage: false, ring: true, raisedBlade: true,
                        pose: "valenna_bullet_pass", pauseBeforePose: "valenna_thrust_brace", pauseAfter: true,
                        passDistance: 1.14f, passMoveTime: 0.16f, sweepLead: SweepPoseLead, motionFlashFrames: 2,
                        sweepStyle: new SweepShape(26f, 1.31f, 0.62f, elevation: -0.11f, speed: 1.72f,
                            forward: 0.20f, travel: 0.46f, lift: -0.10f, turn: -24f, motionTime: 0.105f))
                }),
            new StrikeShow(RabbitId, "解兔", "起手环斩，压低扫过，接窄长的穿刺。",
                new[] {
                    new StrikeCue(StrikeBeat.Orbit, 0.14f, 0.10f, 0.86f, damage: false, pose: "valenna_rabbit_open",
                        sweepStyle: new SweepShape(-36f, 1.32f, 1.02f, true, elevation: 0.13f, speed: 1.72f,
                            mirrorX: true, forward: 0.01f, travel: 0.24f, turn: 34f, motionTime: 0.115f),
                        sweepLead: 0.113f, companionDelay: 0.029f,
                        companionSweep: new SweepShape(13f, 1.37f, 0.49f, elevation: -0.22f, speed: 1.24f,
                            forward: 0.18f, travel: 0.34f, lift: -0.05f, turn: -9f, motionTime: 0.13f)),
                    new StrikeCue(StrikeBeat.Crosscut, 0.16f, 0.26f, 1.16f, pose: "valenna_rabbit_sweep", motionFlashFrames: 3,
                        sweepLead: SweepPoseLead,
                        sweepStyle: new SweepShape(24f, 1.38f, 0.53f, elevation: -0.20f, speed: 1.18f,
                            mirrorX: true, forward: 0.09f, travel: 0.38f, lift: -0.13f, turn: -21f, motionTime: 0.115f)),
                    new StrikeCue(StrikeBeat.Thrust, 0.18f, 0.41f, 1.16f, ring: true, passDistance: 0.85f,
                        passMoveTime: 0.59f * (18f / 27f),
                        pose: "valenna_rabbit_pass", pauseBeforePose: "valenna_rabbit_brace", pauseAfter: true, motionFlashFrames: 4,
                        sweepLead: SweepPoseLead,
                        sweepStyle: new SweepShape(22f, 1.19f, 0.85f, true, elevation: 0.19f,
                            mirrorX: true, forward: 0.10f, travel: 0.29f, lift: 0.10f, turn: 14f))
                }),
            new StrikeShow(885126, "解鹿", "交替换向连斩，金环落在靶点，最后扩成一轮大弧。",
                new[] {
                    new StrikeCue(StrikeBeat.Crosscut, 0.05f, 0.13f, pose: "valenna_deer_open",
                        sweepStyle: new SweepShape(-54f, 1.28f, 1.03f, true, elevation: 0.15f, speed: 1.18f,
                            mirrorX: true, forward: 0.02f, travel: 0.31f, turn: 38f, motionTime: 0.135f),
                        sweepLead: 0.05f + SweepPoseLead, companionDelay: 0.030f, motionFlashFrames: 2,
                        companionSweep: new SweepShape(17f, 1.24f, 0.48f, elevation: -0.25f, speed: 1.16f,
                            mirrorX: true, travel: 0.34f, lift: -0.09f, turn: -19f)),
                    new StrikeCue(StrikeBeat.Rising, 0.09f, 0.11f, 0.84f, damage: false,
                        pose: "valenna_deer_first_sweep", sweepLead: SweepPoseLead),
                    new StrikeCue(StrikeBeat.Crosscut, 0.09f, 0.15f, 0.83f, pose: "valenna_deer_raise", motionFlashFrames: 3,
                        sweepLead: SweepPoseLead, companionDelay: 0.031f,
                        sweepStyle: new SweepShape(21f, 1.19f, 1.08f, elevation: 0.07f, speed: 1.10f,
                            mirrorX: true, travel: 0.19f, turn: -13f, motionTime: 0.14f),
                        companionSweep: new SweepShape(9f, 1.31f, 0.56f, elevation: -0.21f, speed: 1.07f,
                            forward: 0.10f, travel: 0.33f, lift: 0.03f, turn: 13f, motionTime: 0.15f)),
                    new StrikeCue(StrikeBeat.Orbit, 0.07f, 0.09f, 0.92f, damage: false, ring: true,
                        pose: "valenna_deer_spin", sweepLead: SweepPoseLead,
                        sweepStyle: new SweepShape(57f, 0.95f, 0.92f, speed: 1.48f)),
                    new StrikeCue(StrikeBeat.Rising, 0.12f, 0.71f, 1.29f, ring: true, pose: "valenna_deer_sweep", motionFlashFrames: 1,
                        recoilTime: 0.39f,
                        sweepLead: SweepPoseLead,
                        sweepStyle: new SweepShape(-37f, 1.55f, 0.68f, true, elevation: -0.24f, speed: 1.27f,
                            mirrorX: true, forward: 0.16f, travel: 0.48f, lift: -0.14f, turn: 28f, motionTime: 0.105f))
                }),
            new StrikeShow(885128, "解象", "突刺连闪后两次转身横斩，中段竖剑回旋，最后一刀推开目标。",
                new[] {
                    new StrikeCue(StrikeBeat.Crosscut, 0.05f, 0.16f, 0.89f, pose: "valenna_elephant_prepare"),
                    new StrikeCue(StrikeBeat.Thrust, 0.07f, 0.19f, 1.07f, pose: "valenna_elephant_thrust", motionFlashFrames: 2),
                    new StrikeCue(StrikeBeat.Volley, 0.07f, 0.40f, 0.96f, pose: "valenna_elephant_volley", motionFlashFrames: 4),
                    new StrikeCue(StrikeBeat.Rising, 0.09f, 0.23f, 0.91f, damage: false,
                        pose: "valenna_elephant_first_sweep", sweepLead: SweepPoseLead, motionFlashFrames: 3),
                    new StrikeCue(StrikeBeat.Orbit, 0.13f, 0.04f, 1.02f, damage: false, ring: true,
                        pose: "valenna_elephant_spin", sweepLead: SweepPoseLead),
                    new StrikeCue(StrikeBeat.Crosscut, 0.23f, 0.51f, 1.28f, ring: true, pose: "valenna_elephant_sweep",
                        pauseAfter: true, motionFlashFrames: 1, recoilTime: 0.30f,
                        sweepLead: SweepPoseLead,
                        sweepStyle: new SweepShape(69f, 1.41f, 0.59f, elevation: -0.04f, speed: 1.38f,
                            forward: 0.06f, travel: 0.45f, lift: -0.25f, turn: -61f, motionTime: 0.112f))
                }),
            new StrikeShow(885129, "处置", "原地预闪后切到目标身前，旋斩时穿到身后，转身举铳连闪。",
                new[] {
                    new StrikeCue(StrikeBeat.Orbit, 2 * SweepInterval, 19 * SweepInterval, 1.03f, damage: false, pose: "valenna_finish_turn",
                        sweepLead: 2 * SweepInterval + SweepPoseLead, companionDelay: 0.30f,
                        companionSweep: new SweepShape(25f, 1.04f, 1.14f, elevation: 0.04f, speed: 1.38f,
                            forward: 0.04f, travel: 0.14f, turn: -13f)),
                    new StrikeCue(StrikeBeat.Rising, 5 * SweepInterval, 2 * SweepInterval, 1.15f,
                        pose: "valenna_finish_turn_slash", sweepLead: SweepPoseLead),
                    new StrikeCue(StrikeBeat.Orbit, 3 * SweepInterval, 4 * SweepInterval, 1.27f, damage: false,
                        pose: "valenna_finish_sweep", sweepLead: SweepPoseLead,
                        sweepStyle: new SweepShape(72f, 1.34f, 0.61f, elevation: 0.03f, speed: 1.43f,
                            forward: 0.07f, travel: 0.47f, lift: -0.21f, turn: -61f, motionTime: 0.108f)),
                    new StrikeCue(StrikeBeat.Crosscut, 4 * SweepInterval, 5 * SweepInterval, 1.05f,
                        pose: "valenna_finish_raise", sweepLead: SweepPoseLead),
                    new StrikeCue(StrikeBeat.Rising, 5 * SweepInterval, 3 * SweepInterval, 1.03f,
                        pose: "valenna_finish_spin", sweepLead: SweepPoseLead,
                        sweepStyle: new SweepShape(11f, 1.16f, 0.63f, elevation: 0.01f, speed: 1.10f)),
                    new StrikeCue(StrikeBeat.Thrust, 4 * SweepInterval, 12 * SweepInterval, 1.30f, ring: true,
                        splitThrust: true, pose: "valenna_finish_thrust", passDistance: 1.82f,
                        sweepLead: 3f * SweepInterval, motionFlashFrames: 3, passMoveTime: 4 * SweepInterval,
                        sweepStyle: new SweepShape(21f, 1.30f, 0.81f, true, elevation: 0.05f, speed: 1.48f,
                            mirrorX: true, forward: 0.11f, travel: 0.61f, lift: -0.10f, turn: 27f, motionTime: 0.18f)),
                    new StrikeCue(StrikeBeat.Volley, ExecutionInsertTime, ExecutionKnockbackTime + ExecutionStandTime,
                        1.36f, ring: true, pose: "valenna_finish_volley", motionFlashFrames: 2)
                }, charged: true)
        };
    }
}
