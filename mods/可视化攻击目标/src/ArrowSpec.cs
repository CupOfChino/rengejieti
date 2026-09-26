using UnityEngine;

namespace AttackTargetVisualizer
{
    /// <summary>
    /// 一条箭头的绘制参数。坐标是箭头画布的局部坐标（屏幕点已经换算过）。
    /// </summary>
    internal struct ArrowSpec
    {
        /// <summary>曲线起点（出手方的行动槽）。</summary>
        public Vector2 From;

        /// <summary>曲线终点（目标的行动槽）。</summary>
        public Vector2 To;

        public Color Color;

        /// <summary>虚线＝范围攻击；实线＝单体指向。</summary>
        public bool Dashed;

        /// <summary>
        /// 只画曲线的一段（按弧长比例）。争锋相对用：两条黄箭头共用同一条弧线，
        /// 各画一半，箭头头在中点相撞。
        /// 普通箭头：0 → 1。
        /// </summary>
        public float PS0;
        public float PS1;

        /// <summary>是否在 PS1 那一端画箭头头。</summary>
        public bool Head;
    }

    /// <summary>箭头外观。想调粗细、弧度、虚线节奏，改这里就行。</summary>
    internal static class ArrowStyle
    {
        /// <summary>整条箭头的不透明度（含箭头头部）。0.8＝略透，1＝完全不透明。</summary>
        public const float LineAlpha = 0.8f;

        // 敌对＝红，友好＝蓝，争锋相对＝黄
        public static readonly Color Hostile = new Color(1.00f, 0.13f, 0.13f, LineAlpha);
        public static readonly Color Friendly = new Color(0.35f, 0.85f, 0.30f, LineAlpha);
        public static readonly Color Interference = new Color(0.25f, 0.62f, 1.00f, LineAlpha);
        public static readonly Color Clash = new Color(1.00f, 0.85f, 0.10f, LineAlpha);

        /// <summary>箭杆粗细（画布单位，≈ 1920×1080 下的像素）。</summary>
        public const float LineWidth = 6f;

        /// <summary>杆子在箭头那一端收到原来的多少（1＝不收）。</summary>
        public const float TaperRatio = 0.7f;

        /// <summary>箭头头长度 / 半宽。</summary>
        public const float HeadLength = 34f;
        public const float HeadHalfWidth = 17f;

        /// <summary>
        /// 落点额外往上抬这么高（画布单位）。
        /// 我们的箭头层排在行动槽牌子下面，如果正好钉在牌子中心，箭头头会被牌子挡住，
        /// 所以落点取在元素上边框、再往上留一点空隙。
        /// </summary>
        public const float TargetGap = 10f;

        /// <summary>虚线：实段长度、空段长度、流动速度（单位/秒）。</summary>
        public const float DashLength = 20f;
        public const float DashGap = 15f;
        public const float DashSpeed = 110f;

        /// <summary>弧线高度＝两点距离 × 系数，再夹到 [min, max]；一律往上鼓。</summary>
        public const float ArcFactor = 0.30f;
        public const float ArcMin = 48f;
        public const float ArcMax = 300f;

        /// <summary>
        /// 虚线（范围／随机攻击）的弧线额外抬高倍数，和上限。
        /// 抬得比单体高，是为了让 AOE 一眼能认出来、也不会几条线糊成一团。
        /// </summary>
        public const float DashArcBoost = 1.6f;
        public const float DashArcMax = 460f;
    }
}
