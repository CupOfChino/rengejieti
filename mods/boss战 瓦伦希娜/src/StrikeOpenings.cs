using System;

namespace ValennaBattle
{
    public readonly struct StrikeOpenings
    {
        public readonly int Frames;
        public readonly int GripFrames;
        public readonly int BackFrame;
        public readonly float Duration;
        public readonly float FrameTime;
        public readonly float SheathAt;

        public float BackAt => BackFrame * FrameTime;
        public float EyeDuration => BackFrame > 0 ? BackAt : Duration;

        private StrikeOpenings(StrikeShow show, int eye, int sheath, int release, int end, int back = 0)
        {
            Frames = release - eye;
            BackFrame = back > 0 ? back - eye : 0;
            GripFrames = BackFrame > 0 ? 4 : sheath > eye ? sheath - eye : Frames / 2;
            // 首次近身或起斩之后的演出沿用现有时序，按原片前后之比反推起手时长。
            FrameTime = RemainingTime(show) / (end - release);
            Duration = Frames * FrameTime;
            SheathAt = sheath >= eye ? (sheath - eye) * FrameTime : -1f;
        }

        public static StrikeOpenings For(StrikeShow show) => show.Id switch
        {
            // 首次眼光、首次可辨鞘光、第一刀、归位或淡切首帧。原片为每秒 30 帧。
            885121 => new StrikeOpenings(show, 719, 724, 734, 768),
            885124 => new StrikeOpenings(show, 775, 780, 792, 861),
            StrikeProfiles.RabbitId => new StrikeOpenings(show, 867, -1, 871, 932),
            885126 => new StrikeOpenings(show, 941, 945, 955, 1039),
            885127 => new StrikeOpenings(show, 1047, 1051, 1064, 1188),
            885128 => new StrikeOpenings(show, 1194, 1198, 1208, 1341),
            // 处置在 1386 先转背并起弧，1414 才切到对手前。
            StrikeProfiles.ExecutionId => new StrikeOpenings(show, 1377, 1386, 1414, 1593, 1386),
            _ => throw new ArgumentOutOfRangeException(nameof(show))
        };

        private static float RemainingTime(StrikeShow show)
        {
            float duration = StrikeProfiles.ReturnTime + StrikeProfiles.ReturnAfterglow;
            if (!show.Charged)
                duration += show.Id == 885121 ? StrikeProfiles.CutApproachTime : StrikeProfiles.ApproachTime;

            foreach (StrikeCue cue in show.Cues)
            {
                duration += cue.Windup + cue.Hold;
                if (show.Charged && cue.Beat == StrikeBeat.Volley)
                    duration += StrikeProfiles.SweepPoseLead + 2f * StrikeProfiles.BeatPause;
                else
                {
                    if (cue.PauseBeforePose != null) duration += StrikeProfiles.BeatPause;
                    if (cue.PauseAfter) duration += StrikeProfiles.BeatPause;
                }
            }
            return duration;
        }
    }
}
