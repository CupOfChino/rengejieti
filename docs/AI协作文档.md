# 人格解体 Mod 开发 · 协作文档

> 只记重点和"去哪找"。细节看活样例，不要在这份文档里找实现。
> 存放位置总览见 `..\README.md`，各 mod 的分工见 `模组清单.md`。

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
| 屏蔽游戏快捷键（自己开窗时必做） | 游戏按键都走 `KeyboardEventManager.ResponseList`，每一项是 `InputResponseData`，最终在 `InputResponseData.Run()` 里分发；窗口打开时给 `Run()` 挂个返回 false 的 Prefix 就不响应了。WASD 移动方向在 `KeyboardEventManager._UpdateRoleMoveDir()`，要一起停 |
| 自己搭界面的字体 | 用 `MODToolConfig.Instance.LocalizationFonts` 里能画中文的那套（见上面第 4 条坑） |

**命名空间坑**（写代码时最费时间的地方）：同名枚举散在不同命名空间——
`EHeroAttribute`/`ERoleExtraAttribute`/`ESanState`/`EBuffTriggerType` 在**全局**，
`EExploreSkill` 在 `GamePlayEvent`，`EDamageType` 在 `Game.SkillData`，
Buff 与特质相关的数据类在 `MOD`（`MOD.TraitEvent` 放的是特质效果选项）。
拿不准就 `ilspycmd -t <类名> Assembly-CSharp.dll` 看一眼，比猜快。
