// 多行动槽 · 常量
//
// 数据侧编号（与 Project_Depersonal 下的一致，改一处必须两处一起改）：
//   Trait 882001 = 【速战速决】
//   InterludeVacat 882002 = 添加「速战速决」；882003 = 移除「速战速决」

namespace DuoActionSlot
{
    internal static class SlotConstants
    {
        internal const int TraitId = 882001;
        internal const int AddVacatId = 882002;
        internal const int RemoveVacatId = 882003;

        /// <summary>速度门槛：≥ 该值获得 1 个额外行动槽。</summary>
        internal const int SpeedTier1 = 100;

        /// <summary>速度门槛：≥ 该值再获得 1 个额外行动槽（叠加）。</summary>
        internal const int SpeedTier2 = 300;

        /// <summary>克隆槽时用的兜底槽宽（原节点量不到时）。单位：UI 像素。</summary>
        internal const float DefaultSlotWidth = 100f;

        /// <summary>相邻行动槽之间的间隙。单位：UI 像素。</summary>
        internal const float SlotGap = 8f;

        /// <summary>
        /// 行动槽中心距相对槽宽的倍率：1.5 = 槽与槽之间留"半个槽宽"的空档（用户口径）。
        /// 实机要更紧/更松就改这一个数。
        /// </summary>
        internal const float SlotStepRatio = 1.5f;

        /// <summary>取槽图标回调的超时（毫秒）；超时按"没有图标"处理，避免 async 悬挂。</summary>
        internal const int IconTimeoutMs = 1500;
    }
}
