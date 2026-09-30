// 「自定义播放战斗背景和BGM」的数据模型与常量。
//
// 一个调查员 = 一条「领域」配置：
//   · 显示名固定为「调查员名字——后缀」，后缀自己填（全局不可重名）；
//   · 背景：CustomBattleAssets\Background 里的一张图/一个视频；
//   · BGM ：CustomBattleAssets\Bgm 里的一首曲，可选是否循环。

using System.Collections.Generic;
using System.IO;

namespace CustomBattleBg
{
    public static class DomainConstants
    {
        /// <summary>【领域】特质。</summary>
        public const int TraitId = 881001;

        /// <summary>「编辑自定义战斗背景和BGM」幕间入口。</summary>
        public const int EditVacatId = 881002;

        /// <summary>「添加「领域」」幕间入口。</summary>
        public const int AddVacatId = 881003;

        /// <summary>「移除「领域」」幕间入口。</summary>
        public const int RemoveVacatId = 881004;

        /// <summary>「领域展开」技艺（战斗技能）。</summary>
        public const int SkillId = 881005;

        /// <summary>技能来源标记，摘技能时按它来。</summary>
        public const string SkillSourceKey = "custombattlebg_domain_expand";

        /// <summary>资源根目录名（在 mod 根目录下）。</summary>
        public const string AssetsFolderName = "CustomBattleAssets";
        public const string BackgroundFolderName = "Background";
        public const string BgmFolderName = "Bgm";
        public const string StoreFileName = "profiles.cfg";

        /// <summary>数据里的备注前缀，方便识别我们自己的东西。</summary>
        public const string CommentTag = "自定义播放战斗背景和BGM：";

        /// <summary>运行时 CurrentBgm 的 Key 前缀（我们自己的 BGM 用它标记，便于比较/复原）。</summary>
        public const string CustomBgmKeyPrefix = "custombgm_";

        /// <summary>视频背景接管音乐通道时，给 CurrentBgm 打的标记（表示"游戏自带 BGM 已被我们关掉"）。</summary>
        public const string VideoMuteKey = "customvideo_silence";

        public static readonly string[] BackgroundExtensions = new string[] { ".mp4", ".png", ".jpg" };
        public static readonly string[] BgmExtensions = new string[] { ".ogg", ".mp3" };

        /// <summary>背景图片/视频的推荐分辨率文案。</summary>
        public const string BackgroundResolutionTip = "图片推荐 1920×1080（16:9，png/jpg）；视频推荐 1920×1080 MP4（H.264）";

        /// <summary>背景展开动画时长（秒）。</summary>
        public const float BgRevealDuration = 1f;

        /// <summary>"只占上半屏"模式默认遮住的高度比例（0.55 = 屏幕上方 55%）。</summary>
        public const float HalfScreenRatio = 0.55f;

        /// <summary>
        /// "只占上半屏"模式下视频四周的黑边比例（相对上方区域高度，0.05 = 上下各留 5% 黑边）。
        /// </summary>
        public const float VideoHalfEdge = 0.05f;

        // ---- 图片背景的"轻微摇晃"（视频不晃）----

        /// <summary>图片比屏幕多留的余量倍数（1.06 = 多放大 6%，给摇晃留空间）。</summary>
        public const float BgSwayOverscan = 1.06f;

        /// <summary>默认摇晃幅度（uv 单位；0.012 ≈ 屏幕宽度的 1.2%）。</summary>
        public const float BgSwayAmplitude = 0.012f;

        /// <summary>默认摇晃周期（秒/个来回）。</summary>
        public const float BgSwayPeriod = 12f;
    }

    /// <summary>某个调查员的「领域」配置。</summary>
    public class DomainProfile
    {
        /// <summary>存盘用的小节名，例如 domain_001。</summary>
        public string Section = "";

        /// <summary>调查员在存档里的稳定编号（RoleLibraryKey）。</summary>
        public string RoleKey = "";

        /// <summary>调查员名字，用于显示「某某——后缀」以及按名字兜底匹配。</summary>
        public string RoleName = "";

        /// <summary>玩家自己起的后缀。</summary>
        public string Suffix = "";

        /// <summary>背景文件名（不含路径），位于 CustomBattleAssets\Background。</summary>
        public string BgFile = "";

        /// <summary>BGM 文件名（不含路径），位于 CustomBattleAssets\Bgm。</summary>
        public string BgmFile = "";

        /// <summary>BGM 是否循环。</summary>
        public bool BgmLoop = true;

        /// <summary>背景只占上半屏（下方保留原来的战斗背景，角色看起来还站在地上）。</summary>
        public bool HalfScreen = false;

        /// <summary>
        /// 挂载行动：战斗中用了这个行动就展开领域。
        /// 0 = 不挂载；1 = 具体战斗技能（MountId = 技能 id）；2 = 任意法术；3 = 具体法术（MountId = 法术 id）。
        /// </summary>
        public int MountType = 0;

        /// <summary>挂载的 id（战斗技能 id 或法术 id；"任意法术"时为 0）。</summary>
        public int MountId = 0;

        /// <summary>挂载行动的显示名（只用于界面回显和日志）。</summary>
        public string MountName = "";

        public bool HasBackground
        {
            get { return !string.IsNullOrEmpty(BgFile); }
        }

        public bool HasBgm
        {
            get { return !string.IsNullOrEmpty(BgmFile); }
        }

        public bool HasAnything
        {
            get { return HasBackground || HasBgm; }
        }

        public string DisplayName
        {
            get { return DomainText.BuildDisplayName(RoleName, Suffix); }
        }
    }

    /// <summary>「挂载行动」下拉框里的一项。</summary>
    public class ActionOption
    {
        public string Label = "";
        public int Type;   // 同 DomainProfile.MountType
        public int Id;

        public ActionOption(string label, int type, int id)
        {
            Label = label;
            Type = type;
            Id = id;
        }
    }
}
