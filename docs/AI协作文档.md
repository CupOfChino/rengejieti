# 人格解体 Mod 开发 · 协作文档

> 只记重点和"去哪找"。细节看活样例，不要在这份文档里找实现。
> 存放位置总览见 `..\README.md`，各 mod 的分工见 `模组清单.md`。

## 0. 用语约定（用户的黑话对照，2026-09-27 记）

> 用户提需求时常用自己的简称。**照这里的对应关系理解，别自己另起一套叫法**，
> 也别把"关键词"和"术语"混为一谈（这是两个不同的东西）。

| 用户的说法 | 实际指什么 | 落在数据/代码的哪里 |
| --- | --- | --- |
| **关键词** | 显示在 UI 上的**效果说明文本**（一行一条的清单） | 特质/状态：`WakeEvent`（或 `HideEvent`）里的 `Localization_Functin.InputText`；装备：`EquipmentConfigData.OverrideDesc.InputText` |
| 「像孤影一样旁边展示」 | 把效果做成**多行清单**（一条一行，`\n` 分隔），显示在特质详情里 | 见上（样例：Trait 880023 灰暗孤影的四行效果） |
| **术语 / 右栏关键词** | 装备面板**右栏**那一列词条（**不是**上面的"关键词"） | 数据 `Terms` 字段 → `Game\TermData\*.txt`（样例：880002 先发、880005 荆棘） |
| **面板** | 显示出来的数值/描述，通常指要改的数据字段 | `Item\*.txt`、`Buff\*.txt`、`Trait\*.txt` |
| **私货** | 茉莉专属的自定义内容（4 特质 + 6 装备 + 状态） | `mods\可视化攻击目标` 的私货部分，细节见 `temp\私货特质_进度备忘.md` |
| **进副本 / 出副本** | 进入 / 离开一个模组（module） | 游戏事件 `EGameEvent_GamePlay.EnterModule` / `FinishModule` |
| **读档** | 从存档进入游戏（不是"启动游戏"） | "数值迁移"那套机制的触发场景（备忘第十五节） |
| **限伤** | 给某次伤害加"单次不超过 N 点"的上限 | 例：旧版先发 / 反击的 10 点上限（已撤） |
| **速攻 / 压制 / 破甲 / 主输出** | 用户描述武器**定位**的词 | 见备忘第十三节（葬花＝破甲主输出、荆棘＝速攻压制） |

## 1. 仓库结构与约定

```
人格解体本地存档\
├─ docs\         本文档 + 模组清单
├─ mods\<模组名>\ 一个文件夹 = 一个可独立上传的 mod
│   ├─ Project_Depersonal\   本体，上传工坊只需要这个
│   ├─ Uploader              上传元数据
│   ├─ README.md             这个 mod 装了什么
│   └─ tools\                这个 mod 自己的内容生成脚本
├─ tools\        通用脚本（挂载测试/卸载/新建模组）
├─ tools-external\ 第三方工具，不进 git
└─ temp\         临时文件与备份，不进 git
```

**约定**

1. 一个 mod 一个文件夹，按"内容范围"分，不按文件类型分。将来若要一卡一 mod，就每卡一个文件夹。
2. **ID 全局唯一**，跨 mod 也不能撞。新增 mod 前先到 `模组清单.md` 查空闲段并登记。
3. 生成类内容一律用脚本（`mods\<模组名>\tools\*.ps1`），不要手改生成物——手改下次重跑就没了。
4. 改任何已存在的东西之前先备份，备份丢 `temp\`。

## 2. 三类 mod，先选对方向

| 类型 | 目录特征 | 用途 | 样例（在 `workshop\content\1477070\` 里） |
| --- | --- | --- | --- |
| BepInEx 插件 | `plugins\*.dll` | 改代码逻辑 | `2925170762` / `3031614456` |
| XLua 脚本 | `plugins\XluaMod\main.lua` | Lua 挂事件 | `3130216968` / `3253405563` |
| **UGC 项目** | `Project_Depersonal\Assets\Resources\Config\` | **加数据：特质/道具/职业/法术/模型/文字模组** | `3490801895` / `3252047536` |

杂项：`ResourcesFramework\Input\` 是贴图音频替换目录，与数据表无关。
别再把文件直接塞进 `StreamingAssets\Game\`（老写法，会和游戏更新打架）。

## 3. 数据文件怎么写

路径：`<模组根>\Project_Depersonal\Assets\Resources\Config\Game\<类别>\<Id>.txt`

类别：`Trait` 特质、`Buff` 状态、`Item` 道具、`Career` 职业、`Magic` 法术、
`CharacterCreation` 建卡形象、`InterludeVacat` 幕间、`Charator` 角色模板、`RoleModel`/`Speaker` 模型语音。
文字模组放在同级的 `GameModules\<模块名>\`。

**格式**：UTF-8 无 BOM + Tab 缩进的 JSON，外层套 `BaseSheetData`（照新版官方文件写，不要学老文件）。

### 三个事件槽（最容易搞错）

| 槽 | 时机 | 放什么 |
| --- | --- | --- |
| `CreateEvent` | 获得特质时一次性 | 永久数值增减（属性/技能/幸运/护甲） |
| `WakeEvent` | 特质激活期间 | 奖惩骰、系统属性修正 |
| `SleepyEvent` | 特质沉睡时 | `WakeEvent` 的反向补偿（+1 ↔ -1） |

建卡选到的特质默认激活，所以 `WakeEvent` 会立刻生效。

### 效果怎么写

```json
{ "__type": "MOD.TraitEvent.ChangeRoleValueDataOption,Assembly-CSharp",
  "TargetType": 1,
  "Datas": [ { "IsTrackSource": true,
    "RoleAttr":       { "Type": 1,   "Value": "5" },
    "RoleExAttr":     { "Type": 108, "Value": "10" },
    "RoleSkill":      { "Type": 102, "Value": "-10" },
    "RoleSystemAttr": { "Type": 4,   "Value": "10" },
    "ExDiceData": {
      "ExploreSkill":   { "Type": 403, "ChangeAll": false, "IsTemp": false, "Value": 1 },
      "HeroAttribute":  { "Type": 1,   "ChangeAll": false, "IsTemp": false, "Value": -1 },
      "ExtraAttribute": { "Type": 118, "ChangeAll": false, "IsTemp": false, "Value": 2 } } } ],
  "IsRemove": false }
```

- 用不到的容器写 `Type: 0` + `Value: null`（**不要写空字符串**）。
- 数值容器的 `Value` 是**字符串**，`ExDiceData` 的 `Value` 是**数字**。
- `ExDiceData.Value`：`+1` = 1 颗奖励骰，`-1` = 1 颗惩罚骰；`Type:0` + `ChangeAll:true` = 全部生效。

### ID 对照表

| 类别 | 对照 |
| --- | --- |
| `RoleAttr` | 1 力量、2 敏捷、3 智力、4 意志、6 体质 |
| `RoleSkill` | 101 斗殴、102 射击、201 运动、203 隐匿、301 感知、302 观察、403 交涉、405 心理学、501 博学、502 神秘学、601 能工巧匠、603 医术 |
| `RoleExAttr` | 101 生命值、102 精神值、103 魔法值、104 伤害加成、106 金钱、107 弹药、108 幸运、112 护甲、117 材料、118 闪避、119 速度、120 星石 |
| `RoleSystemAttr` | 2 信念、3 精神值上限、4 大成功区间、5 大成功与大失败区间、7 精神衰弱临界值、9 人格限制消耗、10 幸运重掷消耗（其余未摸清） |

> 卡面的"侦察"= 观察 302，"潜行"= 隐匿 203。
> 游戏**没有**"普通成功区间+N"字段，卡面的"成功区间+10"目前用**技能值+10**近似。

## 4. 必踩的坑

1. **游戏只加载"已订阅且启用"的 mod 内容，往创意工坊目录丢新文件夹没用。**
   源码依据（`ConfigManager.GetConfigDatas<T>` / `UGCManager`）：mod 数据目录取自
   `UGCManager.AllItems[i].SaveFolderPath`，而这个值来自 `SteamUGC.GetItemInstallInfo(...)`
   —— **只有 Steam 认账的已订阅物品才会被枚举**，目录扫不到就是扫不到。
   日志旁证：`Player.log` 的"无效文件"只报启用的 mod；`UGCRecord` 里 `IsActive:false` 的 mod 有同样文件却没被扫到。

   数据表加载顺序（优先级从高到低）：
   1. 当前"编辑模组"工程（`CreatorHelper.ConfigPath`，即 `%LocalLow%\MeowNature\Depersonalization-Release\Project_Depersonal\Assets\Resources\Config`）—— 仅当 `CreatorProjectManager` 存在时
   2. 已订阅且启用的 UGC mod（列表**倒序**，后加载的覆盖先加载的）
   3. 游戏本体 `StreamingAssets`

   所以本地 mod 的测试办法，按推荐度：

   | 办法 | 说明 | 状态 |
   | --- | --- | --- |
   | 临时挂到已启用的 mod 上 | `tools\install_test.ps1`，零风险、立刻见效 | ✅ 已验证 |
   | 上传工坊后订阅自己（可设不公开） | 最干净，每个 mod 能独立启停 | 未做 |
   | 放进"编辑模组"工程目录 | 理论上会被第 1 条命中，需开编辑器 | 待实测 |
   | 丢进 `StreamingAssets\ExtraUGCProject\` | 开关 `EnableLoadUGCItem` 是**内嵌 ScriptableObject**，改不了 | ❌ 走不通 |
2. `DontLoadModsList.txt` 与 `UGCRecord` 的 `IsActive` 同步，只影响 BepInEx 插件加载。
3. `StreamingAssets\ExtraUGCProject\<id>\`、`DLCUGCProject\<名>\` 是游戏自己下发的 UGC 工程，别手塞。
4. 数据 txt 不需要 `.rtmeta` / `.rtview`（那是图片音频才配的）。`Project.rtmeta` 固定 9 字节，
   内容 `10 01 22 05 "3.5.3"`，拷一份即可。
5. `Localization_*` 的 `TarKey` 可以留空 `""`，游戏回落用 `InputText`。
6. 改数据后**必须重启游戏**才生效。
7. 挂到别人的 mod 上做测试，Steam 更新那 mod 会把内容一起清掉 → 正式发布要单独上传。
8. **用 PowerShell 生成数据文件时，`ConvertTo-Json` 会丢掉 `__type`**（2026-09-22 踩，症状是"游戏读不进去"）。
   数据文件里的 `__type`（如 `"MOD.ItemData,Assembly-CSharp"`）是游戏认类型的唯一依据，
   少了它 ES3 反序列化直接抛 `ArgumentNullException: Value cannot be null. Parameter name: key`，
   日志（`Player.log`）里表现为 **`读取道具失败： 880002`**，游戏里则完全看不到这件道具。

   **`ConvertFrom-Json` → 改字段 → `ConvertTo-Json` 这一趟就会丢**（PS 5.1 实测：原文件 8 个 `__type`，
   出来只剩 2 个甚至 0 个）。所以：生成脚本**序列化完必须把必需的 `__type` 补回去**
   （正则插回去即可，参考 `mods\可视化攻击目标\tools\make_baihehua_item.ps1` 末尾那两段：
   `Data` 一个、`EquipmentConfigData` 一个），或者干脆**别走 JSON 往返，直接对原文件做文本级替换**。

## 5. 改 / 测 / 发

```powershell
# 1. 生成内容（跑某個 mod 自己的脚本）
powershell -File "mods\跑团卡特质包\tools\build_traits.ps1"

# 2. 挂到已启用的宿主 mod 上做本地验证
powershell -File "tools\install_test.ps1" -ModName 跑团卡特质包
#    进游戏 → 新建调查员 → 看特质列表

# 3. 验完卸载，恢复宿主 mod 原样
powershell -File "tools\uninstall_test.ps1" -ModName 跑团卡特质包
```

上传：游戏内「众创/编辑模组」导入 `mods\<模组名>\Project_Depersonal` 走上传流程，
成功后 `Uploader` 的 `FileId` 会被写成真实工坊 ID。

## 6. 资料优先级

1. 游戏本体数据表 `Depersonalization-Release_Data\StreamingAssets\Game\`（最权威）
2. 能跑的同类 mod：`3490801895`（纯特质最小）、`3252047536`（全类型）、`3308220884`/`3070928504`（模型）、`3253405563`（Lua）
3. 运行日志 `%USERPROFILE%\AppData\LocalLow\MeowNature\Depersonalization-Release\Player.log`
4. 插件日志 `workshop\...\2925170762\BepInEx\LogOutput.log`
5. 反编译参考 `workshop\...\2925170762\BepInEx\DumpedAssemblies\Depersonalization-Release\Assembly-CSharp.dll`
   （字符串里能挖路径常量，如 `\Project_Depersonal/Assets\`、`Game/Trait`）

## 7. 工具

装在 `tools-external\`（不进 git，可重装）：

| 工具 | 位置 | 用途 |
| --- | --- | --- |
| .NET 8 SDK | `tools-external\dotnet\dotnet.exe` | 跑 `ilspycmd`；将来写 BepInEx 插件也用它 |
| ilspycmd 9.1.0 | `tools-external\ilspycmd\ilspycmd.exe` | 反编译读游戏代码 |

反编译产物：`temp\decompiled\Assembly-CSharp.decompiled.cs`（约 54 万行，18MB，单一文件，直接搜就行）

```powershell
$exe = "tools-external\ilspycmd\ilspycmd.exe"
$dll = "E:\SteamLibrary\steamapps\workshop\content\1477070\2925170762\BepInEx\DumpedAssemblies\Depersonalization-Release\Assembly-CSharp.dll"
& $exe -t MOD.TraitData $dll                      # 看单个类，最常用
& $exe -o temp\decompiled $dll                    # 全量导出（约 45 秒）
```

**这套工具最值钱的地方**：游戏自己的数据类上带 `[LabelText("中文说明")]`，等于官方注释。
想知道某个字段是什么意思，直接 `-t` 看那个类，比猜快得多。

注意：`ilspycmd` 最新版（11.x）在当前 SDK 上装不上（缺 `DotnetToolSettings.xml`），用 9.1.0.7988。

### 7.1 仓库自带的两个小工具（2026-09-22 加）

| 脚本 | 用途 |
| --- | --- |
| `tools\qwen_image.ps1` | 调阿里云百炼的 **qwen-image** 生成图片（mod 素材：图标、贴图）。支持两种接口：`-Mode Compat`（OpenAI 兼容，默认）和 `-Mode DashScope`（原生）。示例见脚本头部注释 |
| `tools\normalize_item_icon.ps1` | 把 AI 出的大图（1024/2048、带透明留白）规整成游戏图标要的 **128×128 透明底、内容居中带边距** |

**API Key 怎么放**（脚本按这个顺序找）：`-ApiKey` 参数 → 环境变量 `DASHSCOPE_API_KEY` →
`tools\qwen_api_key.txt`（一行纯文本，**已在 .gitignore 里，不会上传**）。

**素材约定（2026-09-22）**：**要给用户导入游戏编辑器的那张 PNG，统一放仓库根的
`temp\icon_work\`**（用户自己认这个目录，别散在各 mod 的 temp 里）。
脚本默认输出、以及 AI 出图后规整完的成品，都往那儿放。

**另一条同样重要（2026-09-22 踩）**：用户用游戏编辑器导入图片后，文件落在
**本地 mod 目录**（`mods\<模组>\Project_Depersonal\...\Texture\...`），**游戏运行时读的却是工坊目录**
（`workshop\content\1477070\<id>\...`）。所以**每次导入完、或每次改完素材/注册表，都要把整个
`Texture\` 目录（含 `.png` / `.png.rtmeta` / `.png.rtview` 三件套）和 `InternalConfigure.txt`
一起同步到工坊**，否则就是"本地有图、游戏里空白"。
这次（百合花图标）就漏了这一步，症状是"素材丢了"。

**踩点记录**：

- 百炼的 key 是 `sk-` 开头的一串；这次遇到过的 `sk-ws-…` 长串里如果**有一段 `MEYCIQC…`**，
  那是**签名**的样子——换一个真的 API-KEY 才能过（2026-09-22 换了新 key 后两种接口一次就通）；
- 出图 20~60 秒，**偶尔会超过 5 分钟**（服务端忙），脚本 `-TimeoutSec` 默认 300，嫌短可以调大；
- 要透明底就在提示词里明写"**纯透明背景**"，qwen-image 能出真透明 PNG（棋盘格那种）；
- 出图 URL 是**带时效**的，脚本会顺手下载到 `-OutFile`，别只留 URL。

## 8. 待办

- [ ] 条件类效果（"数值≥50 时 +5、否则 -5"）需要靠 Buff 实现，待做
- [ ] **自定义心第二步**：幕间加「修改当前角色【心】」入口 + 编辑窗口（含"当前已有的心"列表与删除按钮），
      做法见第 10 节
- [ ] 角色卡的**装备**（护符/药剂/特殊武器）搬到 `Game\Item\`
- [ ] 想做"副本结束才获得的特质"要配 `Game\InterludeVacat\`，参考 `3490801895`
- [ ] 实测"放进编辑模组工程目录"这条本地测试路径
- [ ] 把跑团卡特质包正式上传工坊，摆脱临时挂载

## 9. 边狱巴士素材提取（另一条线，和做 mod 无关，但同一套工程习惯）

角色卡灵感来源是《Limbus Company》，要建自己的素材库。

**结论：能提，而且是标准 Unity 包，没加密。**

| 项 | 值 |
| --- | --- |
| 游戏位置 | `E:\SteamLibrary\steamapps\common\Limbus Company` |
| Unity 版本 | `6000.3.12f1`（**必须手动指定**，游戏把版本号抹成 0.0.0 了） |
| 资源缓存 | `%USERPROFILE%\AppData\LocalLow\Unity\ProjectMoon_LimbusCompany`（**是个 Junction**，真身在 `D:\limbus_Data\ProjectMoon_LimbusCompany`） |
| 规模 | 1460 个 `__data` 包，约 14.7 GB |
| 包格式 | 标准 `UnityFS`，未加密；单个包内是 `<hash>\<hash>\__data` + `__info` |
| 资源清单 | `%USERPROFILE%\AppData\LocalLow\ProjectMoon\LimbusCompany\com.unity.addressables\catalog_S1.json`（8MB，**里面直接有 1.5 万条 png 路径，不用解包就能查分类**） |

**工具**：`tools-external\AssetStudioCLI\AssetStudioModCLI_net472_win32_64\AssetStudioModCLI.exe`
（v0.19.0，从 aelurum/AssetStudio 下载，是少数支持 Unity 6 的版本）

**两条铁律（踩过才知道）**

1. `--filter-by-container` 是**精确匹配**，要按路径片段筛选得用 **`--filter-by-text`**，多个条件用 `|` 连成正则再加 `--filter-with-regex`。
2. 不加 `--unity-version 6000.3.12f1` 会直接报 "The asset's Unity version has been stripped"，一个资源都读不出来。

**内存**：这台机器只有 16GB。一次性跑大批量时峰值到过 5.2GB，很危险。
有效组合是：**小批量（每批 10 个包）+ `--decompress-to-disk` + `--max-export-tasks 1`**，
峰值直接降到 **240MB**。批量脚本里还加了内存闸门（可用内存低于 1.5GB 就等）。

**资源分类（前端目录名）**

| 前缀 | 内容 |
| --- | --- |
| `Prefab/SD/...` | SD 小人（**角色文件夹里同时含该角色的 `FX_Tex_*` 特效**） |
| `Prefab/Battle/...` | 战斗特效、技能特效（`SkillViewEGO_<7位ID>`） |
| `Buf/<buff名>/` | 状态（buff）图标 |
| `Sprite/SkillIcon` `Sprite/EgoGiftIcon` `Sprite/UI` `DUI/` | 各类小图标 / UI 图集 |
| `Story/` `Sprite/Unit/Portrait|CG` `Gacha/` `Notice/` | 立绘、背景、抽卡图 —— **不要** |

**角色编号规则**（用来把特效归到人）

`10409_Ryoshu_Yuro` → 身份 ID = `1` + 角色(2位) + 身份(2位)；
`SkillViewEGO_2110721` / `20606_Honglu_CryToad` → EGO ID = `2` + 角色(2位) + …
角色(2位)：01 李箱、02 浮士德、03 堂吉诃德、04 良秀、05 默尔索、06 鸿路、
07 希斯克利夫、08 以实玛利、09 罗佳、10 辛克莱、11 奥提斯、12 格里高尔。
`8105` `90046` `400038` 这类是敌人/NPC，别当角色。

**脚本**

```powershell
powershell -File "tools\extract_limbus.ps1" -BatchSize 10   # 分批提取，可断点续跑
powershell -File "tools\organize_limbus.ps1" -DryRun        # 先看分类报表
powershell -File "tools\organize_limbus.ps1"                # 按角色归组到 素材库\
```

输出：原始提取 `E:\lim\_save\_raw\`，整理后 `E:\lim\_save\素材库\<角色>\SD|特效\`。

**注意**：这些图是 Project Moon 的美术资源，自用参考没问题；
如果要放进要公开发布的 mod 里，版权上是另一码事，得自己权衡。

## 10. 写 BepInEx 插件（改逻辑、加界面）

数据表改不了的事——加界面、运行时造数据、按存档记东西——只能写插件。
本仓库第一个插件是 `mods\自定义心\`，照着它抄骨架就行。

**加载方式**：插件放 `<mod 根>\plugins\*.dll`，由框架 mod 里的 `BepInEx.Workshop.dll` 这个 patcher 扫描加载。
哪个 mod 不加载看游戏根目录的 `DontLoadModsList.txt`（里面是 mod id）。

**编译**：不用 csproj，直接拿 SDK 里的 Roslyn 编译器 + 显式引用游戏程序集，不联网、不用拉包。
现成脚本 `mods\自定义心\tools\build.ps1`，`-InstallHost <mod id>` 会顺带挂到宿主 mod 上做本地测试。

```powershell
$csc  = "tools-external\dotnet\sdk\8.0.425\Roslyn\bincore\csc.dll"
$game = "E:\SteamLibrary\steamapps\common\Depersonalization\Depersonalization-Release_Data\Managed"
$bep  = "E:\SteamLibrary\steamapps\workshop\content\1477070\2925170762\BepInEx\core"
# 用 dotnet exec 跑 $csc，-target:library -nostdlib+ -noconfig，再把上面两个目录里的 dll 逐个 -r: 进去
```

**坑**

1. `.ps1` 里有中文必须存成 **UTF-8 带 BOM**，否则 Windows PowerShell 按 GBK 读，直接语法报错
   （apply_patch 写出来的是无 BOM，改完记得转一次）。
2. 编译时 `0Harmony20.dll` 和 `0Harmony.dll` 类型完全重名，两个都引用会报 CS0433，排掉旧的。
3. 插件 `Config.Bind` 落在 `BepInEx\config\<插件GUID>.cfg`——那是宿主 mod 的目录，不是自己 mod 的。
4. **自己搭界面要用对字体**：别随手取界面上第一个 `Text` 的字体——可能是别的插件或某个数字专用字体，
   没有中文字形，结果就是**数字能显示、中文整片空白**（这个坑已经踩过一次）。
   正解是 `MODToolConfig.Instance.LocalizationFonts`（游戏各语言字体表）里挑能画中文的，
   或者退一步挑界面上正在显示中文的 `Text` 的字体；都没有再用
   `Font.CreateDynamicFontFromOSFont("Microsoft YaHei", …)` 兜底。
5. **Harmony 补 `async` 方法：Postfix 不等于"方法跑完"**（2026-09-19 挖出来的）。
   游戏里大量逻辑是 `async Task`（`BuffData.AddBuff`、`BattleRole.TriggerBuffs`、`SetLiftState`、
   `ActionEffectProcess`…）。Harmony 补的是**编译器生成的桩方法**，桩跑到**第一次 `await`**
   就把 `Task` 返回了 —— **Postfix 就是在这一刻执行**，不是等方法真正结束。
   Prefix 仍然是"方法体开始之前"，稳的。所以：
   - 改完还要被后面代码用到的，一律用 **Prefix**，或者换一个**同步**方法下手
     （例：`BattleHelper.CalculationDamage`、`BattleActiveBehaviorData._GetAttackAdditionalEffect` 都是同步的）；
   - 只有当目标方法"声明是 async、但方法体里一个 `await` 都没有"时，Postfix 才等于"跑完"
   （反编译文件里能一眼看出来，例如本次用到的 `BattleActiveBehaviorData._UpdateRollDiceTmpDiceCount`）。
6. **Prefix 返回 false 跳过"返回 `Task` 的方法"时，必须自己塞 `__result`**（2026-09-22 踩，症状很吓人）。
   被跳过的方法是 `async Task` 时，方法会返回 `default` —— **对引用类型就是 `null`**。
   调用方写的是 `await unit.RemoveBuff(...)`，于是**对着 null 抛 NullReferenceException**，
   而且异常栈看起来在"调用它的那个人"身上（栈里只有 `ClearBuffAfterDeath`，看不到 `RemoveBuff`）。
   本次表现：葬花的"剑痕不可驱散"补丁拦下 `BuffHelper.RemoveBuff`（返回 `Task`）后，
   死亡清理 `ClearBuffAfterDeath` 一跑就 NRE → **敌人 HP 归零却死不了、战斗永远结束不了**
   （`Player.log` 里一串 `at BuffHelper.ClearBuffAfterDeath` 的 NRE 就是它）。正确写法：

   ```csharp
   private static readonly Task CompletedTask = Task.FromResult<object>(null);
   private static bool Prefix(BattleRole unit, int id, ref Task __result)
   {
       if (/* 该拦 */) { __result = CompletedTask; return false; }
       return true;
   }
   ```

   参考实现：`mods\可视化攻击目标\src\ZangHua.cs` 的 `Patch_ZangHua_MarkCantBeRemoved`。

**几处关键 API**（都在 `temp\decompiled\Assembly-CSharp.decompiled.cs` 里搜得到）

| 想干的事 | 位置 |
| --- | --- |
| 数据表在内存里的存放 | `Singleton<ResManager>.Instance.BuffFactory` 等，都是 `BaseFactory<T>`：公开的 `RTE/UGC/CloudStorage/BuildIn` 列表 + 私有 `_cacheList` 缓存 |
| 按编号取一条数据 | `BaseFactory<T>.GetConfig(id)`，比的是 `GetConfigName()`（Buff 就是 `Id.ToString()`） |
| 运行时新增数据 | 往 `UGC` 里 Add 自己 new 出来的对象，再反射清 `_cacheList` |
| 特质挂/摘状态 | `MOD.TraitEvent.ActiveBuffOption.Active/UnActive(RoleData role, MOD_Dynamic_Trait, …)`，Prefix 返回 false 可整段替掉 |
| 调查员身份（存数据用） | `RoleData.RoleLibraryKey`，建角色时生成的 GUID，跨存档稳定 |
| 当前存档的调查员名单 | `Singleton<HallWorld>.Instance.HallData.HallLibrary.LibraryRoles`（每项 `.Key` + `.BaseData`） |
| 幕间列表怎么来的 | `UIInterludePanel.UpdateVacationInfo()` 遍历 `InterludeVacationFactory.Datas` 建元素；点确认走 `_onClickVacationMode(vacatConfig, tarRole)` |
| 角色有没有某个特质 | `RoleData.GetTraitData(id)`，`CurrentState == ETraitState.Wake` 表示激活中 |
| 屏蔽游戏快捷键（自己开窗时必做） | **正解：把自建 InputField 登记给 `KeyboardEventManager.AddFiled(field)`**（游戏自己的输入框 `InputFieldControl` 就是这么做的）。框一聚焦 `IsEntryInput` 就是 true，`KeyboardEventManager.Update` 在 `_UpdateRoleMoveDir()` 之后直接 `return`，四条按键路径——`ResponseList` 面板响应 / `listSwitchs` 列表切换 / `keys` 里 `RegisterKeyDownEvent` 注册的全局键（U、空格、数字传送键等）/ AnyKeyDown——会一起停。**只给 `InputResponseData.Run()` 挂 Prefix 不够**：2026-09-23 线上反馈就是打字时按到 U、数字键照样跳界面（接着撞出游戏自身的 `OnClickEnterProject` 空引用）。参考实现：`mods\自定义心\src\UIFactory.cs` 的 `RegisterInputField()` |
| 自己搭界面的字体 | 用 `MODToolConfig.Instance.LocalizationFonts` 里能画中文的那套（见上面第 4 条坑） |

**命名空间坑**（写代码时最费时间的地方）：同名枚举散在不同命名空间——
`EHeroAttribute`/`ERoleExtraAttribute`/`ESanState`/`EBuffTriggerType` 在**全局**，
`EExploreSkill` 在 `GamePlayEvent`，`EDamageType` 在 `Game.SkillData`，
Buff 与特质相关的数据类在 `MOD`（`MOD.TraitEvent` 放的是特质效果选项）。
拿不准就 `ilspycmd -t <类名> Assembly-CSharp.dll` 看一眼，比猜快。

## 11. 【心】这类"持续状态"的判定怎么写（buff 事件触发器）

| 触发器 | 含义 | 【心】用在哪 |
| --- | --- | --- |
| 1 `Stable` | 创建时 | 加减伤 / 数值 |
| 15 `BeforeRoundStart` | 回合开始前 | 每回合扣 1 点精神值 |
| 37 `SanStateChange` | 精神状态**变化**时 | 刚掉进衰弱 / 衰竭的瞬间解除 |
| 64 `LifeStateChangeBeforeFIghtOverCheck` | 战斗中生命状态变化后、战斗结果检查前 | 重伤 / 倒下时解除（官方枚举名里就是 `FIght` 这个拼写） |
| 12 `BattleEnd` | 战斗结束时 | 解除并恢复 5 点精神值 |

- `BuffEventData.Checks` + `SatisfyAny`：`false` = 全部满足才算过，`true` = 任一满足就算过。
  例：15 号 + 两个 `Buff_SelfSanStateCheck { SanState:2，Not:true }` / `{ SanState:3，Not:true }`
  = 只有精神值正常时才扣那 1 点。
- **37 号只在"变化"时响**：进战斗那一刻精神值就已经衰弱，它不会触发。
  所以"进场就是衰弱就别开心"这条拦不了数据，必须在插件里 `ActiveBuffOption.Active` 的 Prefix 里先看
  `RoleData.SanState`（实现见 `mods\自定义心\src\XinEditorPlugin.cs`），数据里再补一条 15 号判定做兜底。
- 生命状态：`ELifeState.Healthy=1`／`SevereWound=2（重伤）`／`Death=3（倒下）`。
  `IsDeathClear` 只在真正走到 `Death`、由 `BattleRole.ClearBuffAfterDeath()` 清理时生效；
  重伤那条路要靠 64 号触发器 + `Buff_SelfLifeStateCheck`。
- **默认心和自定义心是两份实现**：默认心是数据 `Game\Buff\880901.txt`，
  自定义心是插件运行时 new 出来的（`HeartBuffBuilder.cs`）——改规则必须两边同步，
  否则"有自定义心的角色"和"没自定义心的角色"行为会不一致。

### 11.1 `OverlayType` 决定图标角标显示什么（2026-09-22 挖）

| 数据里的 `OverlayType` | 图标角标显示 |
| --- | --- |
| `1` OverlyLayer（叠加层数）／`4` Multiple（多个存在） | **层数数字**（`Text_Round.text = CurLayer`） |
| 其它（0 None / 2 Single / 3 Replace）+ `Duration != 0` | 剩余回合的框 |
| 其它（0 / 2 / 3）+ `Duration == 0` | **∞ 图标**（`Image_Infinite`） |

依据是反编译 35721 / 50110 那两段一模一样的代码：

```csharp
if (OverlayType == OverlyLayer || OverlayType == Multiple) { Text_Round.text = CurLayer.ToString(); }
else if (Config.Duration != 0) { /* 回合框 */ }
else { Image_Infinite.SetActive(true); }      // ← ∞
```

**踩过的实例**：剑痕（`Buff\880040.txt`）原来写的是 `OverlayType: 2`（独立）+ `Duration: 0` →
角标显示成 **∞**，而且**层数根本叠不上去**（`AddBuff` 里 `case EBuffOverlyingType.Single:`
直接 `return`，重复添加被丢掉）。改成 `OverlayType: 1` 之后：每次命中 +1 层、5 层封顶、
角标显示真实层数 ✓（`LayerCount.Value` 保持 `"1"`，和原版「流血」`Buff\103.txt` 一致）。

> 想让某个 buff **显示层数**，`OverlayType` 就写 `1`（叠加层数）。
> "独立 / 替换"（2 / 3）那两种语义是"同一个 buff 只留一个"，压根不叠层。

## 12. 状态的"数值怎么退回去"（踩过一次）

给状态加属性有两条路，**退场行为不一样**：

| 写法 | 什么时候加 | 什么时候退 |
| --- | --- | --- |
| `Arrts` 列表 | 状态挂上时 `ChangeAttr(IsAdd:true, Arrts, SourceKey)` | 理论上摘状态时 `ChangeAttr(IsAdd:false, Arrts, SourceKey)`，但增减是按 **SourceKey** 追踪的，数据里 `IsTrackSource:false` 时键会被清成空串，容易对不上——实测「蓄势」最初的 +4 护甲没有退回来 |
| 事件里的 `Buff_RoleAttrOption` | 由触发节点（创建时/回合开始…）决定 | 摘状态时走 `TriggerRemoveOpts`；`CannnotRecoverOnRemove:true` 表示"不还原" |

**结论（需要"到期必退"的数值就这么写）**：
挂上时用事件给数值并把 `CannnotRecoverOnRemove` 设成 `true`（游戏就不会自作主张地退），
然后在"到期"的几个节点各写一条**反向数值**再摘状态：
回合开始（15）、战斗结束（12）、死亡前（20）。
只要反向数值和摘状态写在同一个事件里（先 -N，再移除自身），就不会出现多退或少退。

范例：`mods\自定义心\...\Game\Buff\880906.txt`（「蓄势」+4 护甲，图标借原版 `icon_buff_hujia`）。

### 12.1 【重要】`IsTrackSource` 必须是 `true`，否则数值会永久叠加（2026-09-17 挖出来的大坑）

`Arrts` 列表里的每一条都有一个 `IsTrackSource` 字段，**写 `false` 会出事**，链路是这样的：

```
Arrts 条目 IsTrackSource = false
   → 游戏 _ChangeAttr 把 sourceKey 清成空串（源码：if (!attrData.IsTrackSource) sourceKey = "";）
   → VariableData.Add("", 值)
   → 而 Add(string key, T value) 里：if (string.IsNullOrEmpty(key)) { Add(value); return; }
   → Add(value) 干的是 _baseValue = _baseValue + 值 —— 直接写进【基础值】，一条来源都不记
   → 摘状态时 Remove("") 去找 Key == "" 的来源，一个也找不到 → 什么也退不掉
```

**后果**：这个状态每挂一次，数值就永久涨一层。玩家打 N 场战斗 = 叠 N 层，而且**看不出是谁干的**（基础值变了，来源列表里干干净净）。

**实测证据**：自定义心的心速度填 100、意志 +30，跑完一个模组后存档里
额外速度基础值 = 600（**6 层**）、意志基础值 +180（**6 × 30**）。

**修法**：`IsTrackSource` 一律写 `true`。这时游戏会按 buff 的 `SourceKey` 记进 `_sources`，
摘状态时 `Remove(SourceKey)` 精确退回，反复挂摘也不会累积。

**但注意——不是所有地方都该改 `true`**：

- `Arrts` 列表（随状态挂上/摘下一一对应）→ **必须 `true`**。
- `Events` 里的 `Buff_RoleAttrOption` 等"一次性消耗"（例如心每回合扣 1 点精神值、结束时回 5 点）
  → **保持 `false`**。那是真实消耗，本来就该落进基础值；改成 `true` 的话，摘状态的瞬间会被一起退掉。

**已修的文件**：`mods\自定义心\src\HeartBuffBuilder.cs`（三个辅助方法 `ExAttr`/`HeroAttr`/`SkillAttr`）、
`mods\自定义心\...\Game\Buff\880901.txt`（默认心的 `Arrts` 两条：伤害+2、速度+20）。

### 12.2 手改存档（`GameHallData`）的排版坑（2026-09-19 踩）

调查员数据存在 `%LocalLow%\MeowNature\Depersonalization-Release\<steamid>\GameHallData`，
是 ES3 写的**明文 JSON**（CRLF + Tab），整体必须严格合法——**差一个 `}` 游戏就直接读不进去**：
`System.FormatException: Expected ',' seperating collection items or ']' terminating collection, found '}'`。

**游戏写列表的排版**：上一条的收尾 `}` 和本条的 `,{` **拼在同一行**（`},{`），
条目自己不带独立的收尾 `}`，只有数组最后一条才有。所以往数组尾部追加条目时：

1. 先**去掉原数组最后一条那个独立的 `}` 行**；
2. 每条新条目写 `},{` + 内容；
3. 整个追加块**只在最后收一次尾** `}`。

**改完必须自检**（别只看"看着像"）：逐行扫 `{}` `[]` 配平（跳过字符串里的括号）→
把改过的数组区间切出来用真正的 JSON 解析器解析 → 再把整个 `"BaseData" : { ... }` 块解析一遍。
参考实现：`temp\add_secret_traits.ps1`（追加逻辑 + 自检思路）。

## 15. 自己搭 UI / 调游戏面板的几个坑（2026-09-18 补）

### 15.1 子画布里的按钮**点不动**（极隐蔽）

给一个面板挂了 `Canvas`（想调排序值）之后，画面看着完全正常，但**里面的按钮点不动**。
原因：**`GraphicRaycaster` 只射线检测"注册在它那块画布上"的图形**（`GraphicRegistry.GetGraphicsForCanvas`），
子画布的图形算在子画布名下，父画布的 raycaster 打不到。

**做法**：想盖住同画布里的其它元素**不要另开画布**，把弹窗做成同一画布里的**最后一个兄弟节点**
（`SetAsLastSibling()`）即可——同画布内后面的节点本来就渲染在上面，射线也正常。

### 15.2 Overlay 画布永远盖在相机画布之上

自己搭的界面用 `RenderMode.ScreenSpaceOverlay` 时，想让它"排在某游戏面板下面"是**做不到的**——
Overlay 在所有相机画布之后渲染，调 `sortingOrder` 也没用。
需要让游戏自己的面板（比如掷骰面板）露出来时，**直接把自己整块 `SetActive(false)`**，用完再开回来。

### 15.3 调游戏的掷骰面板（幕间/大厅可用）

```csharp
CheckDiceData data = new CheckDiceData();
data.TypeName = "速度";                     // 面板上显示的名字
data.CheckValue = 100;
data.Source = BattleHelper.SceneHeroInfo;   // 传了才会启用"消耗幸运重投"
await PrefabSingleton<UIDicePanel>.Instance.Roll(
    EDiceShowType.None, data, isLoop: true, EDiceResult.Success, -1, null);
int roll = PrefabSingleton<UIDicePanel>.Instance.RollResultValue;   // 最终点数
```

- `Roll()` 内部 `while (IsOpen)` 等到玩家确认才返回，**返回后才是最终点数**；
- **重投是面板自带的功能**（`_restartCount` / `_againConsume` = `BattleConfig.StartConsumeLuck`），
  不用自己写扣幸运；
- 第 5 个参数 `setVaule > 0` 可以**强制点数**；面板开着时不能再次 `Roll`（开头 `if (IsOpen) return;`），
  要多颗就串行 `await`；
- 幕间本来就有特质成长要掷骰，所以在幕间/大厅里用是安全的。

## 13. 本地化（做英文版时踩过的路）

游戏自己有一套多语言表：`StreamingAssets\{InterludeLocalization|OtherLocalization|EditorLocalization}\Localization_<表名>.xlsx`
（运行时优先读同目录的 `Localization_<表名>_OnlyLoad.txt`，是一份 ES3 的 `List<LocalizationSheet>`，
每条含 `Key / SheetKey / Values[{LanguageKey, Value}]`）。
mod 的那份由 `LoadExternalExcel()` 扫描：**每个已订阅 mod 的根目录递归找 `*.xlsx`**，所以表得跟着 `Project_Depersonal` 一起上传，
数据里再把 `TarKey` + `SheetKey` 填上就能按语言取值（`LanguageKey` 例如 `Chinese` / `English` / `TraditionalChinese`）。

**我们这个 mod 没走这条路**：插件界面、运行时 new 出来的自定义心状态都没法靠数据表翻，
所以改成"插件内建双语"——`src\XinText.cs` 里判断 `LocalizationHandler.IsChineseLanguage`，
非中文时把 `InputText` 覆盖成英文（只覆盖我们自己的编号），界面文案走同一份 `L()` 字典。
要加第三种语言时，把这个文件扩成语言表即可。

## 14. 战斗 UI：往战斗界面上画东西（可视化攻击目标踩出来的）

**完整设计文档**：`docs\可视化攻击目标-需求与设计.md`（含需求、判定口径、实现要点、代码位置行号）。
这里只留最值的几条，避免后来人重踩。

### 14.1 战斗阶段名和直觉相反（最容易搞错）

主循环在 `BattleFightContent.EnterPhase()`（反编译文件 359769，主循环 359347 附近）：

| 阶段 | 实际在干什么 |
| --- | --- |
| `LookStage` | 战斗开始的准备 |
| **`ActionStage`** | **选行动**（敌方瞬间选完、我方逐个等玩家） |
| `PreprepareStage` | 回合开始的 buff／重置 |
| **`ResultStage`** | **出手演出**（`OnCalculationResult()` 在它里面跑） |

所以"选行动阶段"要判 `EFightPhase.ActionStage`，别被名字骗了。

### 14.2 战斗数据从哪读

| 要什么 | 从哪拿 |
| --- | --- |
| 本轮参战角色 | `BattleHelper.FightContent.SequenceList`（或 `Allies` + `CurWaveEnemies`） |
| 某角色的行动 | `BattleRole.CurrentBehaviourData`（`BattleActiveBehaviorData`） |
| 目标 | 上面这个对象的 `Targets`（`List<BattleRole>`）、`TargetSelectType` |
| 行动槽牌子 | `BattleRole.Model.Hud`（`HUDElement`） |

**关键结论**：目标在**装备行动那一刻**就写进 `Targets` 了（敌方 AI 选完技能立刻调
`BattleBaseAI.AutoSelectTarget`，随机目标也在那时摇好），不是出手时才定。
演出前只有四种情况会改指向：目标倒下／逃跑被剔除、目标全没了重选、拦截掩护、被抓取。

### 14.3 行动槽牌子的界面节点

`HUDElement.Awake()`（37491）里全是固定路径，常用的：

| 节点 | 路径 |
| --- | --- |
| 技能牌子（技能图标＋先攻序号） | `Rect_Scale/Trans_Battle/Trans_SkillShow` |
| 先攻序号 | `.../Trans_SkillShow/Anim_SkillShow/Image_ActionOrder` |
| **「编辑行动」牌子** | `Rect_Scale/Trans_Battle/Trans_EditorAction` |
| 「编辑行动」的显示时机 | `HUDElement._UpdateRoleActionOverShow()`（37264）：**正在被玩家编辑、且技能牌子还没出来**的那个角色 |

> 想指向"某个角色的行动槽"时，技能牌子和「编辑行动」牌子都要认，否则轮到正在编辑的角色就会指空。

### 14.4 自建 UI 层的三个坑

1. **UI 节点必须带 `RectTransform`**。`new GameObject("x")` 建出来是普通 Transform，
   后面设锚点直接 `NullReferenceException` 崩掉；要写成 `new GameObject("x", typeof(RectTransform))`。
2. **画布设置要照抄游戏自己的**。用 `UIRoot.AddCanvas(go, sortingOrder)` 建（它就是 HUD 用的那套：
   Screen Space - Camera + `UIRoot.CurrentCamera` + 排序层 "UI"），
   否则可能出现"日志说画了、屏幕上什么都没有"。
   **更稳的做法**：运行时直接读一个活的 `HUDElement` 的画布，把
   `layer（GameObject 层级！）`、`renderMode`、`worldCamera`、`sortingLayerID`、`planeDistance`、
   `overrideSorting` 全抄过来 —— 相机的 culling mask 很可能不包含默认层，这是"看不见"的头号原因。
   想压在行动槽牌子下面就把 `sortingOrder` 抄成牌子减一。
3. **落点别钉在元素中心**。我们的层排在牌子下面，箭头头会被牌子挡住；
   取元素的**上边框中点**再往上抬一点就露出来了（`RectTransform.GetWorldCorners` 取 1、2 号角点）。

### 14.5 纯插件 mod 的上传包长什么样

工坊里那些纯插件 mod（如 `3031614456`）的目录就两样：`plugins\*.dll` + `Uploader`。
**不需要 `Project_Depersonal`**（那是数据类 mod 才要的）。
`Uploader` 是工坊元数据 JSON（`UGCUploaderData`），字段照 `自定义心` 那份抄即可：
`FileId`（首次上传写 0，游戏会填）、`Title`、`PreviewPath`（工坊图绝对路径）、`Description`（支持
`[h2] [list] [*] [b]` 这类标记）、`Visibility`、`Tags`、`ChangeNote`。

**上传前务必用 `build.ps1 -UninstallHost <宿主id>` 把测试用的 DLL 从宿主 mod 目录里撤掉**，
否则会跟着宿主 mod 一起被打包上传。

## 16. 武器槽与「双手武器」（2026-09-21 挖）

**武器栏就是 `RoleData.Weapons`**（`List<MOD_Dynamic_Item>`，长度 2）：`[0]` 主手、`[1]` 副手。
「双手武器」在数据上**并没有多占一个槽结构**，它的全部表现来自一个属性：

```
MOD_Dynamic_Item.IsNeedBoth  →  Config.Weapon.DoubleWeapon      （反编译 167395）
```

读它的地方就下面这几处 —— 所以**要改"双手武器"的行为，改这一个人入口就够**，不用逐个 UI patch：

| 读它的地方 | 管什么 |
| --- | --- |
| `RoleData.EquipWeapon` | 装双手武器：先卸光所有槽、只放 `Weapons[0]`；装单手武器：先卸掉身上所有双手武器 |
| `RoleData.InstallConfigEquipAndWeapons` | 读档时按存档里的 `ItemWeaponList` 重新装一遍装备 |
| `RoleData.CheckWeaponCanUnequip` | 主手"不可卸下的双手武器"会连带锁住副手槽 |
| `UIChangeEquipPanel` / `UIItemManagementPanel` / `UICharacterDetailPanel` | 界面把 `Weapons[0]` 复制一份画到第二格 + 盖上遮罩（看起来"占两格"） |

**副手武器的战斗流程是游戏自带的**：近战行动跑完主手后，只要 `BattleSkillData.OffHandWeapon != null`，
`BattleActiveBehaviorData` 会自动再走一次副手攻击（`isOffhandCheck: true` → `DiceShowAndTriggerEffectProcess`）。
副手那颗**惩罚骰**是在 `DiceShowAndTriggerEffectProcess` 开头硬加的；想在它之前补一颗奖励骰抵消，
必须挂这个方法的 **Prefix**（Postfix 在 async 方法上只跑到第一次 await 就返回，见 12 章前面那条经验）。

**物品的"归属角色"是 `MOD_Dynamic_Item.SourceRole`**（`[ES3NonSerializable]`，**不进存档**）：
拿物品 `AddItem` 时写、读档 `Reload(role)` 时写。注意"读档按存档重装装备"那条路用的是
`ItemFactory.GetData()` **新建的实例**，SourceRole 可能还是空的 —— 想按"这件东西属于谁"来判定，
要么补一个 `RoleData.EquipWeapon` 的 Prefix 把 `SourceRole` 填上，要么拿
`BattleHelper.GetHeroTeamRoleList()` / `GetHeroBackupTeamRoleList()` 去各角色的
`Weapons` / `BagItems` / `Equips` 里按引用兜底找。

**装备菜单里的"主手 / 副手"选项**读的是原始 `Config.Weapon.DoubleWeapon`（不是 `IsNeedBoth`）：
就算把 `IsNeedBoth` 改成 false（当单手武器用），菜单里双手武器**仍然只有"装备（主手）"一个按钮**；
想让它能主动装到副手，得另外补那两个 UI 菜单方法。

## 17. 多段攻击的"演出不同步"问题（2026-09-21 挖）

武器的检定类型分两种（`EDiceCheckType`）：`OneByOne`（逐个检定，值 1）和 `AllTogether`（同时检定，值 2）。
**走"逐个检定"那条分支时，伤害演出是不等的**（反编译 ~10149 起）：

```csharp
ActionEffectProcess(key4, list, isOffhandCheck, diceType == EDiceType.Normal, delegate { curFinishCount++; }, isPursueAttack);
// ↑ 没有 await：演出自己跑自己的
while (curFinishCount < totalCount) { await new WaitForEndOfFrame(); }
```

那个计数器是靠**受击回调**推进的，而受击（`PlayHitData`）在演出开始后 **0.15 秒**就触发 ——
**整段 0.8 秒的演出才播了个头，流程已经走到下一段**（下一段的骰子动画压上来）。
段数少的时候看不出来（多出来的 0.6 秒被下一段的骰子动画盖住），段数一多就非常明显：
"上一刀还没看清，下一刀已经开始了"。

**想让"上一段演完再进下一段"，可用的手段**（已验证不会自锁）：
在 `BattleActiveBehaviorData.ActionEffectProcess` 的 **Prefix**（每段演出开始时都走）里，
把游戏自己的演出暂停开关 `BattleFightContent.BattlePerformPause` 按住一会儿再放开 ——
游戏会在本段末尾的 `while (BattlePerformPause) await WaitForEndOfFrame();` 检查点上等你。
注意四点：① 这开关可能被游戏/玩家自己按着（开关本身是 true 就别抢）；
② 必须有"到点放开"的兜底（在 `Update` 里每帧检查，别用 await 协程）；
③ 时间基准用 `Time.unscaledTime`（战斗演出会把 `Time.timeScale` 改来改去），
并顺手处理"时间被重置"（elapsed 变负就立刻放开）；
④ 同一段里可能有连着几次 `ActionEffectProcess` 调用，用一个"正在按住"的标记去重，做到"每段一次"。

另外记一笔：**演出快慢的全局开关**是 `UIGameSetPanel.BattleAnimSpeed`（战斗界面那个加速按钮，
点一下 = 4 倍，再点回 1），它通过 `GameHelper.SetGameTimeScale` 写 `Time.timeScale`，
由 `EffectShowData.Play` 在每段演出开始时套用、演完恢复成 1。查"战斗演出莫名变快"先看它。

## 18. 图标资源怎么放（2026-09-21 踩了一整天才理清）

### 18.1 两条路，别搞混

| | 道具图标（Item） | 状态图标（Buff） |
| --- | --- | --- |
| `IconPath` | **必须填** `UI/Textures/Icon/Item/<key>` | **必须留空** |
| `IconPathReference` | `{ ReferenceType: 1004, Key: <key> }` | `{ ReferenceType: 1007, Key: <key> }` |
| 文件放哪 | `Assets\Resources\Texture\Item\` | `Assets\Resources\Texture\BuffIcon\`（裸 PNG 就行） |

**给 buff 的 `IconPath` 填了路径 = 图标直接空白**（游戏改按路径找，找不到就放弃），
而且**连这个 buff 的特效一起不显示**（同一个展示配置整体读失败）——这个坑实际踩过，
症状是"心 buff 的图标和火焰同时消失"。

> **2026-09-22 回读代码后的更正**：这条说法不准确，别照它去改数据。
> 实际决定图标显示的是 **`IconPathReference`** —— `BuffData.GetIcon()`（反编译 13809）、
> `HUDBuffShowElement.SetElement()`（35878）、物品那几处（37611 / 38029 / 39121）全都只调
> `IconPathReference.GetResLoad(...)`，**运行时不读 `IconPath` 字段**（它只在编辑器工具里写、在存档里序列化）。
> 所以"buff 的 IconPath 填了路径 → 图标空白"不成立；填不填都不影响显示。
> 现有的数据（私货 buff 里填了原版路径）**不用改**，它们显示正常。

**图标的完整链路（2026-09-22 又踩了一次，这次补全）**

```
数据 IconPathReference { ReferenceType, Key }
   → ResourceLibraryData.GetRes（反编译 196741）
   → 按 ContentTypeId(=ReferenceType) + Key 查【注册表】：InternalConfigure.txt 的 ResourceMap
   → 拿到 { Key, Path, Ext }
   → 按 Ext 决定去哪取文件（_LoadResByExtra，反编译 196863）
```

| 注册表里的 `Ext` | 游戏去哪找 | 你得准备的文件 |
| --- | --- | --- |
| `".png"`（不含 `"rt"`） | `<mod根>\Project_Depersonal\Assets\<Path>.png` | **必须有实体 PNG**（推荐就这样） |
| `""`（空）或含 `"rt"` | 走 RTE，读 `.rtmeta` / `.rtview` 那套 | `<key>.txt` 三件套那种 |

> **2026-09-22 葬花武器图标空白的真正原因**：注册表写的是 `Ext: ".png"`，而目录里只有
> `icon_item_moli_jian.txt` + `.txt.rtmeta` + `.txt.rtview`（**没有 `.png`**）→ 运行时找不到文件。
> 用户用游戏编辑器重新导入 PNG（生成 `<key>.png` + `.png.rtmeta` + `.png.rtview`）之后图标立刻正常。
> 「剑痕」图标（`icon_buff_zanghua_hen`）同理。
> **一句话：只要 `Ext` 是 `.png`，就确保 `<Path>.png` 这个文件真的存在**；
> 那两份 `.rtmeta` / `.rtview` 在 `Ext=".png"` 时只是给编辑器看的，运行时不读。
> 顺带：`Texture\Item\` 里那 4 套茉莉图标现在统一成了 `.png + .png.rtmeta + .png.rtview`
> （另外 3 套备用图标的 PNG 是从旧 `.txt.rtview` 里解出来的，128×128）。

### 18.2 两种文件格式，也别混

- **三件套**（原版格式）：`<key>.txt`（3 行 TextureResourceReference）+ `<key>.txt.rtmeta` + `<key>.txt.rtview`
  —— 原版图标、以及**用游戏编辑器导出的自制图标**都是这个格式。
- **裸 PNG**：直接一张 `<名字>.png` —— 只有 `Texture\BuffIcon\` 这类"自定义图标目录"认。

**同一个目录里混放两种格式（或丢中文名文件进去）→ 整个目录的图标全部读不出来**（游戏不报错，静默失败）。
这次的表现是"心的图标 + 气焰突然全没了"，最后查出来是自己往 `Texture\Buff\` 塞了个裸 PNG。

### 18.3 自制图标必须过一遍游戏编辑器

`.rtmeta` 的结构是：`固定头(A2 40 26 0A 12 09) + 9 字节的文件标识 + 10 <资源ID> + 30 FF…FF 01`。
**那 9 字节是跟这张图绑定的**（换个文件就变），**抄原版图标的元数据不行**——改了三次"资源 ID"都没用，
就是因为这 9 字节一直没对上。

**正确流程**：

1. 在游戏的「编辑模组」里把 PNG 导入一次（批量也行）——游戏会生成 `<名字>.png.rtmeta` / `<名字>.png.rtview`
2. 把这两个文件**复制成 `<key>.txt.rtmeta` / `<key>.txt.rtview`**（`.txt` 那 3 行引用自己写）
3. 数据里按 18.1 的表格填引用

### 18.3.1 【反复踩】编辑器导入会重写注册表，且只认 Item / Buff 两个目录

每次用游戏编辑器导入 PNG，它都会**重建** `InternalConfigure.txt`：

- 它会把已有的条目**重新排序 + 展开格式**（这些没问题）；
- 但它**只认得 `Texture\Item\` 和 `Texture\Buff\` 两个目录** ——
  `Texture\BuffIcon\`（如 `secret_shuangjian_icon`）和 `Texture\ExtraAnim\`（如 `zanghua_hen_01`、`secret_heart_01~03`）
  里的条目会被**静默抹掉**，游戏不报错，只是那些图标和帧动画不显示。

**2026-09-24 和 09-26 各踩了一次**（两次都是导入新图标之后）。

**处理流程**（每次导入完照做）：

1. 编辑器导入 → 生成的文件落在**本地 mod 目录**（`.png` + `.png.rtmeta` + `.png.rtview`）；
2. 把这三件套**同步到工坊目录**（游戏运行时读的是工坊那份）；
3. 跑 `tools\verify_resource_map.ps1` 看注册表有没有被动：
   - 报"注册了但文件不存在" → 文件没同步或名字写错；
   - 报"目录里有图片没登记" → **就是被编辑器抹了**，把缺的条目补回去；
4. 顺手看一眼 `Project_Depersonal\Project.rtmeta`（9 字节，尾段是版本号 `3.5.3`，
   第二个字节像是资源计数），编辑器会更新它，**把新的那份同步到工坊**。

> 省事的做法：本地被抹之后，直接用**工坊那份（完整的）覆盖回本地**，再重新导入时就有完整底子了。

### 18.4 顺手记：本地 ↔ 工坊的工作流

**本地工程目录和工坊目录是分开的**：改本地 → `build.ps1 -InstallHost <工坊ID>` 同步过去 → 进游戏验证 →
没问题再上传。工坊那份当"稳定备份"，**翻车了可以重订阅拉回来**（注意：重订阅会用云端覆盖工坊目录，
临时同步进去的改动会丢）。

## 19. 把一个功能从一个插件搬到另一个插件（2026-09-22 拆私货）

背景：私货特质（百合花 / 白毛少女 / 灰暗孤影 / 记忆的双剑 + 葬花）原来借住在「自定义心」的
`XinEditor.dll` 里。数据和素材先搬去了「可视化攻击目标」，这次把**插件代码**也搬过去。

**流程（照这个顺序做）**

1. 备份：两边 `src\` + `plugins\` + 要动的数据，一起丢 `temp\`。
2. 把要搬的 `.cs` 复制到新插件的 `src\`，机械替换两处：
   - `namespace XinEditor` → 新插件的命名空间；
   - `XinEditorPlugin.LogInfo/LogError(` → 新插件的日志入口。
3. 原插件里"顺带转发一句"的补丁要**一起搬走**。这里原来是在 `HeartBattleSkill.cs` 的
   `Patch_BattleRole_TriggerBuffs` 里调 `SecretTraits.OnBattleTrigger(...)`；搬完要变成新插件
   自己的一个补丁类，否则战斗节点上没人再叫它。
4. 原插件里把那些补丁的**登记**（`PatchOne(harmony, typeof(...))`）和 `Update()` 里的
   `Tick / TickSlow` 调用删掉，再删源文件。
5. 两个插件都重新编译，然后把新 DLL 同步到各自的工坊目录。

**坑**

- **同一个功能绝不能在两边的 DLL 里各留一份**：两份补丁都挂在同一个游戏方法上，效果会翻倍
  （这里的"近战整套再来一遍"会变成 ×4）。是**搬**，不是复制。
- **命名空间一定要换**：Harmony 按完整类名注册补丁，两边留同名补丁类时排查很费劲；
  换掉之后日志里的 `钩子已挂：Patch_XXX` 也能一眼看出是谁挂的。
- 编译产物大小是**最快的自检**：`XinEditor.dll` 129024 → 75264 字节、
  `AttackTargetVisualizer.dll` 22016 → 81408 字节。
- 想确认"搬干净了"，直接在 DLL 里搜类名字符串即可（不用反编译）：

  ```powershell
  $text = [System.Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($dll))
  ([regex]::Matches($text,'SecretTraits')).Count   # 老 DLL 应为 0，新 DLL > 0
  ```

- 工坊目录里的**测试残留要清**：`plugins\` 下挂过别的 mod 的 DLL（会跟正主抢加载、还会跟着上传），
  `xineditor_installed.manifest` 这类安装清单（内容过时，跑 `-UninstallHost` 会按它误删正式数据）。
- **两个插件同时加载时**：BepInEx 是按 GUID 认插件的，同一个 DLL 出现在两个 mod 的 `plugins\`
  里只会加载其中一个（顺序不定）——"我明明更新了插件"却没好，先想想是不是有残留的那份在挡路。
- **数据搬了，注册表别忘**：`Config\InternalConfigure.txt` 里的 `ResourceMap`（1004 道具图标 /
  1007 状态图标 / 1023 帧图）是"Key ↔ 资源路径"的对照表，**每个 mod 各一份**。私货的
  `icon_item_moli_jian` / `icon_buff_zanghua_hen` / `zanghua_hen_01` 原来注册在「自定义心」那份里，
  数据搬走时注册表**没跟着搬**（而且「自定义心」那份还被覆写成了只剩私货条目、心自己的 `xin_icon`
  + `xin_glow` 20 帧全丢了）。两边 2026-09-22 都补好了：这一包新建了自己的注册表，
  「自定义心」那份按线上版恢复。**漏搬的症状就是图标空白、剑痕贴图不出来**，游戏不报错。
- `tools\rebuild_fx_resource_map.ps1` 重建注册表时**只写心自己的 1007 + 1023**，会把别的条目冲掉；
  它现在只在「自定义心」里用，跑之前先确认那个 mod 里没有别人的注册项。

### 19.1 武器/装备的"自定义效果描述"走哪个字段

装备面板上那段效果文字**不是** `ItemEffectDes`（这个字段游戏 UI 不读，只有编辑器工具用），
而是 `EquipmentConfigData` 里的两个字段（反编译 ≈167742 / 168381）：

```
"EnableOverrideDesc": true,
"OverrideDesc": { "TarKey": "", "SheetKey": "", "InputText": "……" }
```

- `EnableOverrideDesc: true` 时显示 `OverrideDesc`（**整段覆盖**，顺带不显示自动拼的数值行）；
- 此时若 **`TarKey` 非空**，`GetValue()` 会去本地化表查那条 key —— 自己写的 `InputText` 会被表里的旧文案顶掉。
  「葬花」就是照抄蔷薇黑剑数据时把这个 key 一起抄来了，结果面板上显示的是**蔷薇黑剑**的效果描述；
  **自己写文案就把 `TarKey` 留空**。
- `EnableOverrideDesc: false` 时游戏自己拼"徒手伤害 / 装备-描述"那些行，看不到自定义效果文字。

> **从原版数据抄底子时，描述类字段要一个个检查**（2026-09-22 连栽两次）：
> 抄 `10135`（阿特拉克人偶）做「百合花」、抄 `10680`（蔷薇黑剑）做「葬花」时，
> **`OverrideDesc` 的 `TarKey` + `InputText` 都跟着抄了过来**，结果装备面板上显示的是**别人的效果描述**
> （葬花显示蔷薇黑剑的充能文案、百合花显示人偶的"意志智力敏捷减半"）。
> 抄完底子必须清一遍：`OverrideDesc.TarKey` 留空 + `OverrideDesc.InputText` 换成自己的；
> `ConfirmEquipDesc` 同理（不用弹窗就把 `ShowConfirmEquipPopup` 设 false、`TarKey` 留空）。
> 一句话：**凡是带 `TarKey` 的字段，抄过来就等于抄了别人的文案**。

### 19.2 【改武器效果时必做】描述、关键词、实现三处一起对

2026-09-27 用户明确提醒：**改武器的某个效果，不能只改实现**，要同时检查三处：

| 改了什么 | 要跟着改的地方 |
| --- | --- |
| 数值 / 机制 | `tools\make_xxx_item.ps1`（生成数据）、插件里对应的常量与逻辑 |
| 玩家看得到的说明 | 数据的 `OverrideDesc`（装备面板正文，见 19.1）|
| 右栏的关键词 | `tools\make_secret_terms.ps1` 里那条 `TermData`（改完要重跑脚本）|

**这次踩到的**：荆棘的速度台阶从 20 改成 50、加了"命中挂【荆棘】"，
但只改了 `OverrideDesc`，忘了同步 `持握·荆棘` 那条术语 —— 结果正文写着 50、右栏还写着 20。

> 另外记住：术语是**独立编号**（`Game\TermData\`，我们占 880001 起），
> 武器数据里的 `Terms` 字段才是"这件装备要显示哪几条关键词"的挂钩；
> 新加术语后**别忘了把它加进武器的 `Terms`**，否则右栏不会出现。

## 20. 「额外攻击 / 连击段数」的原版写法（2026-09-22 对照检查）

游戏里"多打一次"有**两条完全不同的路**，别混：

| 机制 | 实际效果 | 谁在用 | 怎么触发 |
| --- | --- | --- | --- |
| `Buff_ChangeWeaponAttackCount`（编辑器标签"修改武器连续攻击次数"）／`Item_ChangeAttackCount` | **连击段数 +N** —— 写进武器的 `ExtraContinuousAttackCount`，按 `SourceKey` 追踪 | 蔷薇黑剑（`Buff\860.txt`）、双截棍（`Item\3007.txt`，敏捷≥70 时 2→4 连）、徒手 buff（`Buff\623.txt`） | 配在 Buff / 道具的 `Events` 里 |
| `Buff_SetExtraAttackCount`（"设置额外攻击次数"）+ `Item_PursueAttack`（"追加伤害"） | **强制命中的一次追加攻击**（内部写死 `EDiceResult.Success`，所以描述写"本次无需检定"） | 《尸鬼教典仪》（`Item\2008.txt`） | **只能挂在道具的 ResultFuncs 上**，特质/Buff 直接用不了 |

**结论：蔷薇黑剑那种"可以额外攻击一次"就是用「连击段数 +1」实现的**
（`Buff\860.txt` 里 `Buff_ChangeWeaponAttackCount { Count: 1 }`），不是"再跑一遍行动"。
所以"改连击段数"这条路**是原版的正当做法**，插件做同类效果不必有心理负担。

**插件里想贴原版就这么写**（`ExtraContinuousAttackCount` 是 `VariableData<int>`）：

```csharp
weapon.ExtraContinuousAttackCount.Add(sourceKey, 1);     // 加
weapon.ExtraContinuousAttackCount.Remove(sourceKey);     // 摘（成对，别忘）
```

按 `SourceKey` 记来源，和 `Arrts` 那套是同一个机制（见 12.1 的 `IsTrackSource` 坑）——
**加/摘必须成对**，否则永久叠加。

**我们自己那两处**（`mods\可视化攻击目标\src\ZangHua.cs` / `SecretTraits.cs`）：

- 葬花"本次攻击段数 +1"：走 `BattleSkillData.GetSkillEffectCount` 的 Postfix（消费式，用完即清）。
  语义和原版一致，手段是"接管计算"——好处是不依赖"挂 buff → 摘 buff"的时间窗，**不会残留**；
- 灰暗孤影"整套再来一遍"：同一条 Postfix ×2。原版**没有**对应机制
  （`Item_PursueAttack` 是强制命中、还只能道具触发），只能自己走这条路。
- `GetSkillEffectCount` 全局只有 **3 个调用点**（9262 / 9315 / 9346，都在攻击方真跑行动时），
  改动面可控 —— 来龙去脉见 `temp\私货特质_进度备忘.md` 4.4。

**游戏自己的疑似笔误**（用到就注意）：`BattleSkillData.GetSkillEffectCount` 里副手那一段是

```csharp
num += OffHandWeapon.ExtraContinuousAttackCount.Value + OffHandWeapon.ExtraContinuousAttackCount.Value;
```

同一个字段加了两次（反编译 375799）—— 给副手加连击数会被算两遍。

### 20.1 真·额外行动：`Buff_TriggerBattleActionEffect`（2026-09-22 补齐，上一轮漏了这条）

上面那两条路都是"多打几下"。**原版还有一条真正"再行动一次"的机制**，
装备「**老旧怀表**」（`Item\5009.txt`）用的就是它：

- 装备后挂 `Buff\659.txt`（老旧怀表效果buff）；
- 659 的 `Events`：触发器 **`EBuffTrigger: 5` = `ActionEnd`（行动结束）**，
  `Checks` = `Buff_InSceneCheck{IsInBattle:true}` + `Buff_CheckUseSameSkillWithLast`（这次技能与上次是否相同），
  `Funcs` = `Buff_TriggerBattleActionEffect { UseLastRecord: true, Opts: [Buff_ItemChargeChangeOption{ItemId:5009, Value:1}] }`；
- 效果：**重复执行上一次用的那个行动**，代价是消耗 1 点充能。

**`Buff_TriggerBattleActionEffect`（反编译 341984，执行逻辑 342053 起）到底干了什么**：

1. 备份当前的 `BattleSkillData` / `MagicData`；
2. 选技能：指定技能 id ／ 怪物技能 ／ 法术 ／ **`UseLastRecord`（取 `role.LastSkillRecord`）**；
3. `EquipSkill(skillData)`，目标沿用记录（`UseLastActionTargets`）或重新选；
4. **`await finalTarget.CurrentBehaviourData.Run()`** ← **完整的行动流程，重新掷骰、重新演出**；
5. 只有 `IsTriggerActionEnd == true` 时才再触发一次 `ActionEnd`（**默认 false，所以不会递归**）；
6. 完事执行 `Opts`（怀表用它扣充能）。

`role.LastSkillRecord` 是在**选行动那一刻**写的（`SaveRoleSkillSelectRecord`，363037）——
所以 `ActionEnd` 触发时它已经是"刚刚这次"的技能和目标；`UseSameSkillWithLast` 也是那一刻算好、
存进 `role.UseSameSkillWithLast` 的。

> **想在特质/Buff 上做"真·额外行动"**：配一个隐藏 buff → `Events` 里 `EBuffTrigger: 5`（ActionEnd）
> + 自己的 Check（比如"这次是近战武器攻击"）+ `Funcs: Buff_TriggerBattleActionEffect { UseLastRecord: true, UseLastActionTargets: true }`，
> 插件负责在合适的时候挂/摘这个 buff。比"改连击段数"更贴原版，但目标、条件、递归都要重新实测。
>
> **别和 `Item_PursueAttack` 混**：那个是"强制命中（写死 Success）的一次追加"，只能挂道具；
> 这个是"把整个行动再跑一遍"，Buff 就能配。

## 21. 伤害结算的两个口子：`CalculationDamage` 与 `SetDamage`（2026-09-27 荆棘踩出来）

一次伤害从"算"到"扣"经过两层，插件想改数值得先想清楚拦哪一层：

| 层 | 干什么 | 适合改什么 |
| --- | --- | --- |
| `BattleHelper.CalculationDamage`（静态、返回 int） | 从武器基础骰开始逐项加：角色加值、武器词条、`ExtraItemAddDamage`、目标减免、百分比修正…… | 改"这一击的数值规则"（倍率、破甲、额外伤害） |
| `BattleRole.SetDamage`（实例、async） | 真正扣血（`ChangeAttr(CurrentHp, -x)`），并触发 `BeDamage_Before / BeDamage / GetDamage`；`OnHit` 内部最后也走它 | 想让"最终结果"精确可控时，在这里覆盖数值 |

- **为什么有时必须在 `SetDamage` 拦**：`OnHit` 里（反编译 356626 附近）还会再加一次
  `attacker.Data.DamageAddData.DamageValue(...)`（角色身上的"额外伤害值"）。
  在 `CalculationDamage` 里算得再干净，也会被这层顶回去 —— 要"只造成武器基础伤害"就得拦最后一口。
- **要在 `SetDamage` 里做"多次扣血"**（比如把一次伤害拆 3 次）：Prefix 返回 false 时
  **必须给 `ref Task __result` 交一个我们自己的 Task**（例：`SplitDamage(...)` 返回的 Task），
  否则调用方 `await null` 直接 NRE —— 和"剑痕不可驱散"那次踩的是同一个坑
  （凡是被跳过的原方法返回 `Task`，都要补一个已完成/自建的 Task）。
  另外要放一个"正在拆分的目标"集合做重入闸门，否则自己的补扣会被自己的 Prefix 再拦一遍。
- **武器基础伤害怎么掷**：`new DiceValueData(); FreshDiceValue(data.Value); GetValue()`
  （`DamageData.Value` 就是面板上的 `"1D4+2"` 这种串）。
- **"只让防御侧生效"怎么做**（先发 / 反击那种"自己不加成、对面照常算"）：
  不要手工复刻护甲 / 减伤公式，而是**屏蔽攻击者一侧、让游戏原方法照跑**：
  1. 克隆 `DamageData`，把基础骰固定成掷好的数字（`Value = "5"`，`FreshDiceValue` 会把它当加值）、`DB_Bonus = 0`；
  2. 克隆 `DamageAdditionalData`，把 `ExtraItemAddDamage / BonusDamage / DamageRatio` 归零；
  3. 在**纯同步窗口**里临时把 `source.Data.CauseDamageChangePercentData` 换成空对象、
     `ExtraDamageData.AdditionalDamageDatas` 换成空列表，调完 `CalculationDamage` 立刻恢复（中间不要 await）；
  4. 用一个 `InGuardedCalc` 标志让"内层调用"跳过我们自己的所有附加处理（否则花香、剑痕会被加两遍）。
  这样护甲、`CalculateReducDamage`、`CalculateReducRoleDamage`、`BeDamageChangePercentData`、
  免疫区间都还是游戏原逻辑，只有攻击者的加成被摘掉。
- **想"行动 1 次、伤害结算 N 次（各算护甲）"**：克隆 `DamageAdditionalData` 重跑 `CalculationDamage`，
  再走 `OnHit` —— 别去拆最终数值，拆数值等于只算一次护甲（2026-09-27 先做错了一版）。
  参考实现：`mods\可视化攻击目标\src\JingJi.cs` 的 `TryBaseDamageWithDefense` / `TryGreatSuccessBonus` / `TripleStrike`。
- **给目标挂"受到伤害 +X%"（易伤）**：用
  `role.Data.BeDamageChangePercentData.Add(sourceKey, false, percent, 0, 0, damageTypes)`，
  摘掉用 `.Remove(sourceKey)`（成对，和属性那套一样）。两个坑：
  ① `damageTypes` **不能为 null** —— 计算时的判断是 `if (DamageTypes == null || !Contains(...)) continue`，
  想让"对全部伤害类型生效"就得把 `EDamageType` 的全枚举塞进去；
  ② `FloorDamageValue != 0` 时走的是"固定值下限"通道、`Percent` 不生效，别混用。
  参考：`mods\可视化攻击目标\src\ZangHua.cs` 的 `SyncMarkBeDamage`（剑痕的 +15%/层）。

## 22. "检定值 ±N"与"探索回合"的挂点（2026-09-27 花香踩出来）

- **想给某个检定加值 / 减值（成功区间 ±N）**：挂在 `BattleHelper.GetDiceCheckValue` 上最省事 ——
  战斗技能检定（54084 / 54166）、buff 触发检定（9792 的 `num` 来源）、探索/面板检定
  （16963 的 `GetDiceData` 内部就调它）**三条路最终都走它**，
  在 Postfix 里改 `__result` 就三处一起生效，而且天然不会重复加。
  - 判"是哪种检定"看参数：`attrType == EHeroAttribute.POW` = 意志检定、`skillType` = 技能检定、
    `extraType` = 二级属性检定。
- **探索回合的钩子**：游戏探索里每回合（约 12 秒）会对每个队员调
  `role.TriggerEquipItemsEffect(ESkillTriggerType.ExploreRoundChange)`
  （反编译 `RoundChange()` / `TimeChangeInRuleModule()`），插件挂这个方法就能拿到"探索回合"事件。
  注意 `ESkillTriggerType` 在 **`Game.FixedSkill`** 命名空间（找不到类型时先想这里）。
  - 数据侧对应的 Item InfoData `TriggerType = 21` 是同一个时机（参考原版「灵玉符文」）。
- **"力量对抗 / 属性对抗"走哪条路**（2026-09-27 白毛少女踩）：战斗技能里的对抗由技能的
  `UseCheck.CheckAttr`（属性）+ `isCompareDice = true` 配置（反编译 115717 / 55956），
  最终仍然落到 `BattleHelper.GetDiceCheckValue(role, ..., attrType, ..., isCompareDice: true)` ——
  和上面"检定值 ±N"是同一个挂点，用 `isCompareDice && attrType == EHeroAttribute.STR` 就能精确锁定"力量对抗"。
  - **想做"通用版"就别看 `isCompareDice`**：只判 `attrType == STR`，这样"力量检定（探索/面板/战斗技能）"
    和"力量对抗"一次全覆盖。**其它模组**只要用游戏标准配置（`CheckAttr = STR`）配技能也同样吃得到——
    我们挂的是底层入口不是具体技能；只有"自己另写判定、不调 `GetDiceCheckValue`"的模组技能覆盖不到。
