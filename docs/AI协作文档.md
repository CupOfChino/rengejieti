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

### 建卡特质页空白（2026-11-18）

- `Player.log` 中 `UICharGenPanel._ReFreshTraits()[0x00085]` 越界时，先核对对应版本的程序集。当前版本在这一步从可创建的「遭遇」特质中抽默认项，候选为空会中断整个建卡初始化。页面顶部 `5/5` 是初始显示值。
- 原版 `StreamingAssets/Game/Trait` 有 16 个可创建的遭遇，其中第一条在当前目录枚举顺序的第 104 个文件。游戏的 `BaseFactory<T>.All` 会缓存首次读到的非空列表，`AddItem` 不刷新该缓存。插件应在配置表加载完成后再访问 `All`，可参考 `WorkshopHelper.EndLoadConfigData` 的后置钩子。
- 本次重点排查工坊 `3810876729`「自定义播放战斗背景和BGM」0.2.0。其 `CustomBattleBgPlugin.IsGameReady` 每三秒用 `TraitFactory.All.Count` 判断就绪，插件日志显示初始化早于配置加载结束。源码在 `mods/自定义播放战斗背景和BGM/src/CustomBattleBgPlugin.cs`。只停用这一项并重启后，用户实测特质页恢复。

- 0.2.1 移除 `IsGameReady` 轮询，改为收到 `WorkshopHelper.EndLoadConfigData` 后置通知后再初始化。已有的英文文案刷新和背景状态表预加载也从配置完成后开始。用模组内 `tools/build.ps1` 编译并将 `plugins/CustomBattleBg.dll` 同步到本机工坊目录。重启后日志确认 0.2.1 在配置加载结束后才初始化，状态表预加载完成，用户确认建卡特质页恢复。修复版已更新工坊原条目 `3810876729`，线上 manifest 为 `3174035637484380731`，更新说明为「修复了bug」。

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

**工坊上传踩坑（2026-10-18）**：`Uploader` 是游戏用 ES3 保存的 JSON，描述文本里如果出现
`\“` / `\”` 这类非法 JSON 转义，`GetUploadFilePath` 会读取失败，`CurrentUploadData` 变成 null；
`UpLoadItem()` 随后会 new 一个 `FileId = 0` 的对象，于是**把更新误走成“创建新物品”**。
现象是原条目更新报 `Failed to download manifest` / `Access Denied`，随后工坊里多出一个没有内容的新 ID。
修复：只保留合法转义（中文引号直接写 `“”`，不要在前面加反斜杠），确认
`Get-Content Uploader -Raw | ConvertFrom-Json` 能通过，再把 `FileId` 改回原条目。
另外，`SearchAndGenerateConfigFile` 提示缺少 `<mod>\Resource` 只是跳过额外资源扫描，
不会阻止上传；项目自带资源在 `Project_Depersonal\Assets\Resources\` 下时是正常的。

**工坊隐藏条目的 `Access Denied`（2026-10-18）**：Steamworks 文档明确，Workshop 物品在作者
同意《Steam 创意工坊法律协议》前会**默认隐藏**。游戏的 `CreateWorkshopItem` 会检查
`m_bUserNeedsToAcceptWorkshopLegalAgreement` 并提示，但 `UpdateWinWorkshopItem` **没有检查**，
所以更新已存在条目时只会得到 `Access Denied`，标题/描述也改不了。处理：登录作者账号打开
`https://steamcommunity.com/workshop/workshoplegalagreement/` 接受协议，再刷新条目页；
如果协议已接受仍隐藏，才考虑审核/举报/DMCA，联系 Steam 支持或游戏开发者。

**独立工坊上传器（2026-10-18）**：游戏上传界面会把 `Uploader` 描述里的 `\“` / `\”` 原样写回，
导致下一次 `ESCheckValid` 读取失败并新建物品。`tools-external\workshop_uploader` 里有可复用的
Steamworks.NET 控制台上传器：先修正 `Uploader`（合法 JSON、FileId 指向旧条目），再用
`WorkshopUploader.exe <内容目录> <Uploader路径> <旧itemId>` 上传；它会直接 `StartItemUpdate`
旧条目并打印 `SubmitItemUpdateResult_t`。本次已用该工具把「自定义心」成功更新到 `3801085814`
（manifest `4138376817386004355`）。工具依赖游戏的 `com.rlabrecque.steamworks.net.dll` 和
`steam_api64.dll`，所以放在 `tools-external`，不要放进要上传的 mod 内容目录。

**隐藏/审核条目的更新限制（2026-10-18）**：如果工坊条目被 Steam 或游戏开发者置于隐藏/审核状态，
`StartItemUpdate` 和内容上传仍可能推进（Steam 日志会出现新的 ManifestID），但最后的
`SubmitItemUpdate` 会返回 `k_EResultAccessDenied`，条目的 `time_updated`/manifest 不会变化。
这不是本地网络或 Uploader 代码能绕过的；必须由条目所有者、游戏开发者或 Steam 客服解除隐藏/完成审核。
此时反复重试无意义，也不要为了绕过而新建重复物品（订阅和收藏不会迁移）。

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

### 18.3.2 【2026-09-27 又踩】生成脚本会把"编辑器里的修改"回退

**症状**：按脚本重新生成数据后，明明在游戏编辑器里改好的东西（图标 key、`OverlayType`、帧特效开关）
全都变回旧样子 —— 游戏不报错，只是悄悄回退（实例：剑痕图标变回原版、
`OverlayType` 从 1 变回 2 → 层数永远 1 层、`zanghua_hen` 帧特效被清空）。

**原因**：生成脚本里的模板是**当初**写的，之后在编辑器里改过的字段没有回写进脚本，
一跑脚本就整体覆盖。

**规矩**：

1. 凡是在游戏编辑器里改过数据（图标 / `OverlayType` / 帧特效 / 描述…），**立刻把改动回写到生成脚本**；
2. 跑完脚本后**抽查关键字段**是否还在：图标 Key、`OverlayType`、`UseFrameEffect` / `EffectName`、描述文本；
3. 能参数化的就参数化（例：`make_zanghua_buffs.ps1` 的 `$overlayType` / `$fxName` 两个参数）。

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

### 19.3 武器和百合花系列的右栏词条（2026-11-19）

- 主面板正文保存在 `ItemEffectDes.InputText` 与 `EquipmentConfigData.OverrideDesc.InputText`。右栏由 `Terms` 关联通用术语，`GetAffixPair` 同时收集 `BuffsLink`，按显示名称去重。葬花的右栏剑痕只通过术语 880001 显示，Buff 880040 仍用于战斗状态。
- 私货当前使用术语 880001～880014。名称和正文都可用 Unity 的 `<color=#RRGGBB>…</color>`，流血用红色、剑痕用蓝色、花香用紫色。Buff 数据没有 `Terms` 字段，三项状态直接在 `Des` 里用彩色小标题整理。英文状态文案在 `src/SecretText.cs`，改装备数据时同步检查对应的 `tools/make_*.ps1`。

- 2026-11-19 实测旧插件只补载 6 条硬编码术语，导致原来的剑痕标题和状态标题颜色不同而重复，四件百合花饰品也没有右栏。现在 `SecretRuntime.EnsureTerms` 在 `WorkshopHelper.EndLoadConfigData` 之后，从 DLL 所属模组的 `Game/TermData` 读取 880001～880014 中缺失的条目。新增后只清一次术语工厂缓存，文案从数据文件维护。
- 剑痕是持续状态帧动画，`Game/ExtraAnim/zanghua_hen.txt` 的 `AnimSize` 为原尺寸的 2/3。目标锁定（880052）沿用状态栏图标。橙金四角由 `MoliAmbientFx.UpdateLockMark` 绘制，中心对齐人物，外框高约为头脚高度的 1.32 倍，宽度按当前精灵投影加留白。初次出现时用 0.20 秒收紧，中心保持透明，状态解除后清理。生成器 `tools/make_target_lock.ps1` 关闭 Buff 的原版 prefab 特效。

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
- **荆棘的三段是跳字**：原版 `OnHit` → `SetDamage` 只算和扣一次总伤。
  `Patch_JingJi_DamageDisplay` 仅在 `SetDamage` 异步状态机直接调用改血的位置标记荆棘。原版 `ShowHudNum` 拿到实际用于跳字的整数后，同一帧创建三个原版数字元素并错开摆放。总伤 46 显示 16、15、15。旧版有三次实际伤害，首段击杀会跳过后两段。
- **茉莉的输出和反击减伤**：`SecretTraits.ApplyMoliDamageModifiers` 统一处理孤影的近战 +1、百合花状态的近战 −2、白毛少女的普通/物理伤害 −25%。剑刃圆舞曲仅对原版闪避对抗发起的敌方回击减伤；`Patch_Moli_DodgeCounter` 标记原版骰子流程的两个调用点，避免混进技能或受击状态触发的其他反击。
- **剑痕的追加法伤**：`MoliCombat` 在 `SetDamage` 记录当前主动近战命中的受击者，原版 `ActionEnd` 后每个受击者按当时剑痕层数结算一次。固定伤害按（层数+1）÷3向下取整，走原版魔法免疫、减伤和扣血。
- **白毛少女专属行动**：`Game/BattleSkill/880071.txt` 用敌方全体、普通意志检定和条件技艺（`IsMachiningSkill`），原版战斗菜单会把未通过 `ShowCheck` 的条件技艺隐藏。`WhiteHairAction` 在工坊配置加载完成时挂选定/成功效果。技能数据的自身特质筛选明确要求苏醒的白毛少女，使用战斗 HUD 字体显示粉色表情，有理智且 `RoleData.IsHuman` 的敌人施加原版魅惑；无理智的敌人叠两层弱点暴露。
- 「喵喵叫卖萌」的装备表情持续跟随当前技能槽。定位复用箭头的整块槽矩形，以顶边中点对齐。在原版 `Run` 设置 `IsRunning` 并开始收槽时隐藏装备表情。意志检定成功时，`DiceSuccess` 在收槽前记录的 HUD 局部位置显示喵叫表情 2 秒。换掉行动、无法继续显示的战斗状态和退出战斗由 `WhiteHairAction` 清理。
- **孤影与花香**：孤影的旧意志追击从触发点注释停用，闪避获得隐匿读原版防御骰的困难或大成功。`Patch_Moli_ReduceSanLoss` 在原版额外属性数值入口消减实际 SAN 变化量。花香根据 `RoleData._ChangeAttr` 的实际扣血记录消耗一层，沿用一次层数减少回复 1 血、1 精神。
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

## 23. 武器的"命中特效 / 音效"怎么播（2026-09-27 排查"看不到攻击特效"踩出来）

**结论先说：能放数据里就放数据里，插件那套只留给"没有武器表现链的攻击"（先发 / 反击 / 吸血）。**

### 23.1 数据层（首选）：`DamageData` 自己就是武器的表现入口

武器 `EquipmentConfigData.Damage` 上有两个字段决定"这次攻击长什么样"（反编译 `DamageData` 346520 起，
播放逻辑 346600 起）：

- `UseCommonEffect = true` + `CommonEffectId = N` → 走**通用战斗表现**，
  即 `StreamingAssets\Game\CommonBattleEffectShow\N.txt`（含攻击动画 + 命中特效 + 音效 + 受击动作）。
  常用 id：**1 通用徒手攻击 / 2 通用枪械射击 / 4 利刃受击 / 5 钝器受击 / 9 手雷 / 14 通用生命值恢复 / 16 通用精神恢复**。
  例：原版 `Item\101.txt`（利刃武器）就是 `true + 4`。
- `UseCommonEffect = false` + `EffectShow`（自定义表演序列）→ 自己拼 `PlayRoleAnimationData` /
  `PlayRoleEffectData` / `PlaySoundData` / `PlayHitData` 四件套。
  **照抄 `CommonBattleEffectShow\4.txt` 的结构只换特效和音效**最不容易漏字段；
  原版样例：`Item\10306.txt` / `10604.txt` 用 `FXSkillHit_Puncture` 做突刺。
- 两者互斥：`UseCommonEffect = true` 时游戏**完全忽略** `EffectShow`（别以为会叠加）。

**当前武器表现（2026-11-20）**

- 葬花 880001 保留原版动作、声音和命中节点。武器的 `EffectShow` 以 `EffectName=ZangHuaSlash` 标记调用，`BattleBladeFx` 播淡绿斩击，随后拉出浅绿残影，白、淡蓝花瓣错时翻落。单体主刃宽以目标身高的 1.64 倍为基准，每攻击段各播一次。横扫按目标组跨度加留白、压扁，扫过整组目标。贴图载入失败时回退原版利刃受击效果。
- 荆棘 880006 的原版命中时机触发淡绿突刺，主刃宽以目标身高的 1.72 倍为基准。刃尖先展开，残影随后跟进，两对透明气浪在不同时间上下扩散。普通攻击三段动画相隔 0.1 秒、上下错开，先发和反击各播一段。刀刺音效仍用游戏音效。伤害在原版命中时点一次结算，三个数字同屏。
- `SecretFx` 负责现有命中节点和原版兜底，`BattleBladeFx` 的瞬态粒子随战斗场景回收。剑气以同一张贴图的 16 个精灵裁片沿出手方向展开，再收淡。原版突刺 `FXSkillHit_Puncture` 的 64 像素帧按 100 PPU、子节点 2 倍缩放换算，完整帧宽约 1.28 Unity 单位。本模组以此为比例参照并放大瞬态剑气。原版 `FX_BladeHit` 的光晕、火花、轮廓分层可作动效参考，通用命中特效窗口为 0.5 秒。剑气贴图与花香、孤影的 PNG 一起嵌入 DLL。比例预览用原版成年调查员 `AdultYaNan_Idle_1_200` 的待机帧，原版角色贴图仅作本地预览。独立的 `LimbusSlashFx` 实验件仍停用。

**茉莉的循环状态粒子（2026-11-19）**

- 常驻状态控制密度和遮挡，锁定框以人物为中心。短时攻击可覆盖更大的范围，主刃从无到有，残影、气浪和花瓣错时出现。

- `src/MoliAmbientFx.cs` 按当前场景角色的状态驱动花香（880049）和灰暗孤影（880026）。探索与战斗分别使用原版地图、战斗摄像机。转换场景、状态消失或模型销毁时清理粒子，角色预览界面不运行。
- 花香三种粉色花瓣轮换，最多八片，单片为模型高度的 6%～9%，从肩侧交替生成，向外轻摆、翻转并在脚边淡出。低位粉雾最多两缕，绘制在角色背层。灰暗孤影只有两道半透明白线，沿身侧、脚边绕行，按前后位置随角色排序。尺寸、位置取当前模型头脚挂点和精灵宽度。密度与轨迹在 `UpdatePetals`、`UpdateMist` 和 `UpdateWind` 调整。茉莉存档的 `Custom_ModelReference.Key` 是 `Model_Custom_all`，对应工坊 3253405563「生命树奥萝菈人物模型」的 `ResourcesFramework/Input/all/1-*.png` 待机帧，原画布 64×80，轴心 (0.45, 0)。
- 源贴图位于 `mods/可视化攻击目标/assets/Particles`。`tools/build.ps1` 使用 Roslyn 响应文件处理 Windows 长命令行，并把循环粒子 PNG 作为嵌入资源编入 DLL。本机工坊只需要同步 DLL。运行时从 alpha 边界生成精灵，保留透明通道。

**离线特效预览与调位（2026-11-20）**

- `Buff/880049.txt` 的 `FxPlayType` 为 0，清空旧的肾上腺素 `FX_Buff_130`。花香特效由插件绘制。花瓣、粉雾以精灵中心为基准，默认横向范围为旧版的 62%，白风的轨道半径为旧版的 54%。葬花贴图上下翻转，沿出手方向从斜上到斜下展开。
- 执行 `mods/可视化攻击目标/tools/make_vfx_preview.ps1` 生成模组 `temp/特效预览器.html`。在浏览器打开即可用茉莉的待机帧预览六种特效。工坊模型贴图只写入本机临时页面。模板在 `tools/vfx_preview.html`。花瓣随机轨迹、原版模型挂点和横扫目标跨度在浏览器用固定样例模拟，可在游戏里复核。
- 浏览器和插件共用 `assets/VfxPlacement.defaults.json` 的默认调位参数。X 正值右移，Y 正值上移，1.00 为一个头脚挂点高度，角度正值逆时针。花香和白风的角度调各粒子的姿态，攻击角度转动整组特效。网页导出的 `VfxPlacement.json` 可放入本机测试版 DLL 同级目录，插件启动时读取。发布工坊时发布默认参数，个人调位文件留在本机。
- 攻击动作与攻击段触发继续由游戏处理。调整运行时轨迹或贴图时，对照 `MoliAmbientFx`、`BattleBladeFx` 与浏览器里的对应函数，再重新生成页面。

**人物特效编辑器（2026-11-20）**

- 仓库根目录 `tools/打开特效编辑器.cmd` 是入口。双击会启动本地页面，窗口随用随关。实现位于 `tools/vfx-editor/`，需要本机 Node.js。游戏安装目录和工坊库从 Steam 常用位置、`libraryfolders.vdf` 查找。非标准路径可通过 `DEPERSONAL_GAME`、`DEPERSONAL_WORKSHOP`、`DEPERSONAL_LOCAL` 指定。
- 左右角色可分别选择动作、延迟、镜像和前后站位。图层的「定位角色」决定头顶、身体与脚边挂点。动作时序和场景绘制统一由 `scene.mjs` 处理。原有单角色工程保持保存的模型 ID 与画面中央挂点。动作列表只显示能够从实有精灵帧还原的动作。
- 「保存检查图」写入 `mods/可视化攻击目标/temp/特效编辑器缓存/检查记录/`。从 `latest.json` 找最新编号，取该目录里的 `current.png`、`timeline.png`、`manifest.json` 和可原样打开的 `project.vfx.json`。检查图使用编辑器本身的渲染代码。
- [Agent 自查指南](../tools/vfx-editor/Agent自查指南.md)记录素材搜索、定时截图、与目标工程或参考图并排对照的命令。用脚本返回的图片路径检查实际画面。替换原贴图前先保存参考截图，避免新旧工程同时读取当前素材。
- `catalog.mjs` 先读本地和订阅模组的 `ExtraAnim`、`Texture/ExtraAnim`、`ResourcesFramework/Input` 和自定义粒子图，再按帧名用 AssetStudioCLI 提取原版 `resources.assets`。原版帧缓存在可视化攻击目标的 `temp/特效编辑器缓存`，由打包资源时间戳和筛选条件区分版本。游戏和工坊源文件只读。遇到同名且无法判定来自哪个图集的精灵，跳过相关动画，避免加载错帧。带 `.rtprefab` 的 Unity 粒子由游戏引擎播放，离线工具处理可提取的二维逐帧素材。
- `animation.mjs` 负责挂点、时间与播放预设，`app.mjs` 负责图层和导出。画布 X 正值向右，Y 正值向上。工程的 `.vfx.json` 保存图层和动作，并内嵌手动导入的图片。引用游戏和工坊的工程在打开时需要对应旧素材仍可扫描。
- 「导出透明帧」输出 24 FPS 的 PNG ZIP，里面有连续帧及记录画布尺寸、挂点和帧率的 `manifest.json`。画面里的人物和参考线是预览辅助。导出的帧可拿进游戏编辑器再建 `ExtraAnim` 并挂到 Buff 或攻击动作。游戏内原有的 `VfxPlacement.json` 仍由上一节的调位页管理。

**游戏预制体联动（2026-11-21）**

- 启动同一个编辑器，点「游戏预制体」进入联动页。可以启动游戏或连接已打开的游戏，等到首页后读取预制体。选中一个粒子层，改倍率和颜色，点「应用并重播」。左侧回放游戏相机捕获的帧，右侧「游戏回读」对照 Unity 组件读出的原值和应用值。
- 网页逻辑在 `tools/vfx-editor/live.mjs`，本机服务器的预览接口在 `bridge.mjs`。游戏端是「可视化攻击目标」的 `PrefabPreviewBridge.cs`。两端经工坊测试模组自身的 `temp/VfxEditorBridge` 匹配会话编号和请求序号；令牌过期或主动断开时释放预览副本及相机。调试时从 Steam 启动游戏。
- 原版资源经 `Resources.Load` 按白名单加载。在独立位置复制预制体，固定随机种子，按 12 FPS 推进粒子并读取专用相机画面。粒子发射量倍率同时作用在发射率和爆发粒子数上。游戏端用内置 Newtonsoft.Json 传层列表，Unity `JsonUtility` 曾在实测中漏掉插件自定义的层数组。
- 「保存副本」输出 `.prefab-vfx.json`，记录原版预制体引用和调参值，可在联动页打开。游戏编辑器可独立导入的 `.rtprefab` 还需要验证序列化与材质资源引用。
- 本机实测读取了利刃命中、魅惑和肾上腺素。对利刃的 `HCFX_Hit_08` 层调参时，Unity 回读初始大小 8 → 16，爆发粒子数 2 → 4。浏览器成功回放游戏相机帧，保存副本后可以重新读取同一组改动。

**瓦伦希娜战斗规则、卡面与音轨（0.5.4）**

- 0.5.4 在 BepInEx 声明自定义心、领域两个硬前置。心的展示状态 885140 只引用 880901 的外观。领域按配置动态生成伤害强化；铳剑保留 885153 以兼容旧档，`ValennaDomainDamage.cs` 调用现有领域程序集的构建器获取稳定效果，再按装备自己的叠层与战斗期管理生命周期。构建瓦伦希娜只引用前置 DLL，工坊包只分发自己的插件。

- 发布从仓库根的 `工坊待上传/瓦伦希娜` 选择目录；包内需带 `resource/skill-audio`、`resource/sound` 与 `resource/voice-subtitles.json`。开发目录的 `temp/特效浏览器` 保存实际分镜，逐帧图与对照图还用于后续调参。更新其他既有条目若要求保留多语言介绍，只向 Steam 提交内容路径，并核对新 manifest 和原描述。

- 排查「选完判定后没有攻击」，先查本机游戏日志里的 Harmony 补丁加载异常。0.5.2 的 `StrikePlayback.Prepare(Task, …)` 被识别为补丁初始化方法，攻击挂钩整体跳过。已把业务方法改名。攻击栏列出 7/7 招只说明菜单生成成功，仍须核对成功事件回退和分段伤害入口。
- 伙伴资料用 `PartnerData/885101`，基础体质 80，酒鬼特质扣 10。旧档在 `ValennaKit.RefreshPartnerProfile` 迁移，原生体质变更入口重算 HP 上限。游戏内显示「瓦伦希娜」，旧的立绘名、磁盘路径与桌面角色卡原名仍可用于查找。简介有七项标签，其中「二老板」「七大家族」绘制红色删除线。
- `build_data.ps1` 分开维护神话卡底部短说明及装备完整规则。大图和横幅是独立素材。七招的公共条件、各段伤害和触发时点写进 `BattleSkill.Description`。旧 UGUI 的行内状态图使用真实 Buff 图标，优先确认在原生滚动框里能读到处置最后的攻击后与击杀两项。特质卡原版外层已靠上，内层描述组的默认对齐是 MiddleCenter，需要在切换到其他卡片时还原。
- 预知眼扣费前为本次闪避记录充能与过热状态。战斗成功区间、探索检定、满层未来奖励骰，以及主动技能面板的概率明细各有自己的原生入口，见 `ValennaPrescience.cs` 和 `AcceleratedFuture.cs`。验证补丁注册可以在 Windows .NET Framework 下使用游戏随附的 Harmony 执行整个 `PatchAll`，这与运行 Unity 战斗、观察界面排版是两项验证。

- 入本前的伙伴神话栏走 `PartnerData/885101 → MythicalItem/885112 → Item/885112`，玩家角色的候选列表是另一条入口。专属限制和旧档迁移在 `ValennaEyeEquipment.cs`。神话卡采用原生 1005 资源类型，128×194 大图和 166×54 横幅各自有单独资源键；工坊原版示例和本体「不死者吊坠」可供核对。角色卡的八项特质由 `build_data.ps1` 生成，其中「心——瓦伦希娜」885155 使用原生战斗速度 119；技能层数加成在 `ValennaHeart.cs`。
- `ValennaEventAudio.cs` 独立处理闪避和引爆声音。以每次闪避开骰前的过热状态选取语音资格，未过热时的成功池 2 条、失败池 5 条；按原字幕表取词。对照引爆时，检查 `ValennaTremor.cs` 的真实增长、图标和数字，与 `BurstBatch` 传出的有效次数。五声的音频起点相隔 20 毫秒。
- 排查攻击菜单常亮的感叹号时，用原版 `UIBattlePanel.UpdateNewSkillTips` 核对。它查询 `IsMachiningSkill`、角色绑定与 `ShowCheck`，不看 `HideInSkillSelectList`。退役表须同时隐藏并关闭新技提示资格。保留原版已读记录，玩家仍能正常发现其他新技能。
- 技能说明先列整招规则，再用【I】至【V】逐段列骰和命中效果。`tools/build_data.ps1` 写规则，`tools/format_rules.ps1` 写颜色与状态链接，`ValennaRuleVisuals.cs` 显示段号徽章、词条图标与预知眼下划线。神话卡面由原生界面绘制标题与背景，投影只画右侧物品。
- 处置与预知的技能框见 `ValennaSkillFrame.cs`。双侧警标和顶边滚动贴图取自 `resource/skill-ui`，由 `ValennaWarningArt.cs` 绘制。行动槽徽标在 `ValennaActionWarning.cs`，多行动槽按各格绑定的技能刷新。两个界面都复用对象，换技时须清除旧装饰。
- 原版技能图取自桌面保存的灰机 Wiki「主线战斗9-42」页面，131401 至 131407 为七项攻击，131408 为预知。`tools/import_skill_icons.ps1` 注册 `Texture/SkillIcon` 下的八张 PNG。选技能列表与行动槽分别使用 `CustomSkillElementIconReference` 和 `BattleSkillIconReference`，均为 1006；发布时一起带上图片、边车、项目资源表和 `Project.rtmeta`。
- 伙伴库会读取旧存档中 `PartnerRoleData` 的姓名。表里已写「瓦伦希娜」时仍要核对 `RoleData.Name` 和 `NameKeyData`。针对伙伴 885101 的旧名兼容见 `ValennaKit.cs`。
- 数值和单位按原版连写，例如「10点伤害」「1颗奖励骰」。导出脚本用 U+2063 和 U+2064 标出需要同行的短语，配置文件要按序数比较才能检查这两个不可见字符。`ValennaRuleWrapping.cs` 在显示时移除标记，按 UGUI 的实际宽度决定换行。复核时同时看技能正文和战斗状态悬浮框，后者直接写入 `Text_NameContent.text`。改动这套规则后，插件和对应配置表要一同打包。

- `mods/boss战 瓦伦希娜/resource/sound/Wiki音效` 保存敌方单位 1314「音效」表中的 28 个原始 OGG。子目录对应表中「特殊1–4」「技能1–3」「拼点」「范围攻击」九行，文件保留 Wiki 原名。编辑器搜索「Wiki音效」可试听，具体技能和起播时间由玩家配置。[灰机 Wiki 原表](https://limbuscompany.huijiwiki.com/wiki/敌方单位/1314#音效)

- 编辑器画布上方的「瓦伦希娜技能 → 查看整招」从模组 `temp/特效浏览器` 载入动作并读取已应用的音轨。整招列表与处置前后段的衔接在 `tools/vfx-editor/skill-projects.mjs`。若改处置穿身、回身举铳或回位的时长，同步核对 `StrikeProfiles.cs`、`PlayExecutionFinish` 和编辑器中的整招开火点。[查看动作与音效时间](../tools/vfx-editor/Agent自查指南.md#配置演示音效)

- 伙伴资料与装备风味用桌面 `111]/卡14-瓦伦娜.md`。战斗数值以玩家确认的游戏版为准。解象和处置使用时各给 3 层呼吸法。预知眼与技艺的闪避回理智分别计数，技艺每战最多两次。七招效果在 `StrikeResolution.cs`，装备与补弹在 `ValennaEquipmentRules.cs`，分段时序仍由 `StrikeProfiles.cs` 控制。原生联合技能配置只能填两个探索技能，解鹿和解象在构造主动骰时取斗殴技能值与 DEX 属性值，依原生联合公式相加并按主技能上下限截取。
- 构建入口为模组 `tools/build_presentation.ps1`、`build_data.ps1`、`build.ps1` 和 `test_install.ps1`。铳剑、切斩刀由原武器图等比缩小，独立头像和四件装备图标的源文件在 `resource/装备与头像`。「狩猎目标」状态图和战场双剑分别由图标资源与 `ValennaHunt.cs` 管理。
- 本地音效编辑器可在同一秒叠放多条音效并分别调音量。「应用到技能」同步 `resource/skill-audio/<技能ID>.json`，`StrikeAudio.cs` 在下次使用技能时读取。同一套分镜时间用于编辑器和游戏。维护技能 ID 或音频路径时同时核对编辑器、运行时与安装脚本。[编辑器音效操作](../tools/vfx-editor/Agent自查指南.md#配置演示音效)

- 技能音轨的 `speed` 是保音高语速（0.5～2），`group` 为 0 时独立播放，正整数为随机组号。编辑器可勾选两条或更多语音编入同一组，每次施招每组只抽一条。旧的双句组沿用相同格式。`subtitle` 缺省时依 `resource/voice-subtitles.json` 回填，显式空串关闭该句字幕。默认表的 22 条由敌方单位 1314 的语音文件与本地 OGG 核对哈希，再按对白 ID 对应本机汉化 `LLC_zh-CN/BattleSpeechBubbleDlg-a1c9p3.json`。武器音效默认无台词。游戏变速与头顶字幕分别在 `VoiceTempo.cs` 和 `StrikeVoiceCaption.cs`，预览对应 `audio.mjs` 和 `scene.mjs`。测试保存与同步时请使用临时工程和宿主，玩家正在编辑的技能音轨只读。

**瓦伦娜演出测试（2026-11-21）**

- 独立工程见[模组说明](../mods/boss战%20瓦伦希娜/README.md)。基础数据优先用 111] 的《卡14-瓦伦娜》，游戏接口核对本机程序集，演出参数和本地参考素材索引见[招式演出分段](../mods/boss战%20瓦伦希娜/招式演出分段.md)。
- 本机版本的 `UIBattlePanel._heroRole` 是指向 `BattleHelper.BattleHeroInfo` 的属性。在 `_SetSkillSelectButtonElement` 筛选专属攻击时直接读取 `BattleHeroInfo`。演出只暂移角色模型，用原生命中事件结算，结束时恢复模型位置。
- 0.1.1 实机能读到伙伴和插件，但日志缺少预加载演出绑定。0.2.0 改从 `BattleActiveBehaviorData.TriggeOptionEffect` 的成功事件触发。编辑器预览为 `valenna_arc_preview`，测试模型为 `valenna_test_model`。
- 0.2.0 的实机日志确认测试模型载入，技能 885123 开始播片。0.2.1 先更新切割攻击，网格刀光和编辑器预览都从 `src/BladeRibbonProfile.cs` 获取截面与展开参数。
- 0.2.2 参考模组下 `valencina_ref/params/参数说明.md` 的分层做法。切割攻击在挥刀期间启动主刀光，命中节点负责结算、火星与余弧。柔光只叠在刀光附近，可随轮廓一起调。
- 0.2.3 挂载用户提供的立绘和首领目录 `1314_Thumb_FatherAppearance` 的 SD 帧。头像键为 `valenna_portrait`，模型仍用 `valenna_test_model`。原图映射在 `tools/build_presentation.ps1`，脚下落点、朝向、头像显示范围及读档时的默认头像刷新在 `src/ValennaAppearance.cs`。
- 0.3.0 以 `video/39860503251-1-192.mp4` 从 00:23 起的八条演出为准。人物动作直接切换已有姿势。工坊 1379 侧参数用于对照图层和帧时序，制作用 `E:\lim` 的刀光、噪声、闪光及烟火遮罩。
- `src/StrikeProfiles.cs` 是招式顺序、阶段和测试伤害的共同数据源。`tools/SlashFramePainter.cs` 生成 `valenna_arc_preview` 序列，同一批帧嵌入 DLL。先按本地特效浏览器自查指南核对亮刃、余弧、交叉闪光与色彩，再在实机确认位移、受击和清理。[招式演出分段](../mods/boss战%20瓦伦希娜/招式演出分段.md) [特效浏览器自查](../tools/vfx-editor/Agent自查指南.md)
- 0.3.1 接入模组 `resource` 的 30 张用户整理单图，并从已有首领素材补齐「处置」的 6 张回撤与举枪帧。姿势对应文件与脚下落点在 `src/ValennaPoses.cs`，构建脚本把同一份坐标输出到模型目录的 `pose-pivots.json`，供本地特效浏览器对齐逐帧切图。修改切图时同时检查游戏材质引用、动作轨和原片对照。
- 0.3.2 按用户逐帧指正调整三招。切割攻击约 1.00 秒保持挑斩收势。尖端打击 1.01 秒后仰、1.08 秒中段直刺、1.41 秒末段直刺。处置约 2.50 秒至少三束，中束粗亮，两侧快进快退。突刺、举枪蓄光和枪刃余光的帧在 `tools/SlashFramePainter.cs`，浏览器工程中的重点分镜在 `temp/特效浏览器/原片对照`。
- 0.3.3 优先使用模组内 `resource/可能的粒子和材质` 生成外围剑气、红色碎片、白火星和命中序列。切割攻击 0.20 秒沿用握柄起手，0.64 秒用 `skill1_3.png`。人物周围的黄色火焰由现有「心」状态绘制，末段的枪口蓄光是独立序列。反手斩在浏览器里用同一序列开启纵向镜像。
- 0.4.12 的七招动作顺序已经由玩家逐帧对齐。调整演出时优先对照原片的亮刃、余风和命中叠层，预知保留，处置身上的黄焰归「心」状态。原片绝对帧号、分镜对照和遮罩分工见[主动招式光效](../mods/boss战%20瓦伦希娜/招式演出分段.md#0412-主动招式光效)。新增帧序列时同步修改 `build_presentation.ps1` 的动画注册、`build.ps1` 的内嵌目录和 `StrikeArt.cs` 的加载、释放。
- 处置最外层剑风已获玩家认可。处置亮度在 `SlashFramePainter.Draw(..., execution: true)` 和 `PaintExecutionBlade` 内单独校准。枪口环的色样取自「自定义心」的成品 PNG，蓄力和开火后的环使用同一组尺寸。[处置逐帧记录](../mods/boss战%20瓦伦希娜/招式演出分段.md#0413-处置亮刃和枪口环)
- 七招换姿要同时核对 `StrikeProfiles.cs` 的命中前后时长与 `build_presentation.ps1` 的动作间隔、停留权重。相邻动作若用了同一张姿势图，应合并计算可见时长。穿过靶影后还会定姿，`PassMoveTime` 单独计命中后的移动窗口。参考原片用目标距离和人物高度辨认停身，镜头缩放会改变屏幕距离。普通攻击主副刃用 `valenna_attack_sweep`、`valenna_attack_companion`，第 12 帧起接通用尾帧。原片边界、遮挡和亮刃检查方法见[七招动作占比与亮刃](../mods/boss战%20瓦伦希娜/招式演出分段.md#0414-七招动作占比与亮刃)。
- `StrikeRepeats.cs` 的续刺相对首发出光计时，`TurningSweeps.cs` 的回扫相对换姿计时。转刀可能早于末斩起光。对照参考时连续查看上下刺线、前后刃的交替与余光重叠。需要强化少数重斩时，`valenna_peak_sweep` 只替换这几刀的主刃，普通帧和处置帧各自独立。[连刺、转刀与斩击强光](../mods/boss战%20瓦伦希娜/招式演出分段.md#0415-连刺转刀与斩击强光)
- **换姿与脚步**。尖端和子弹贯穿的两张刺姿仅前手不同，两张都要带位移。伸手前踏、收手轻退，停姿时站稳。转刀的收力、发力和落势各有不同方向和步幅。`StrikeFootwork.Step` 按实际显示的切图决定落脚；改动作顺序先核对用户认定的原片帧。持续运动每五个实际渲染帧留一张当前身影，换姿立即留旧影，同帧只出一张。检查脚下用动态预览，检查刀光层次用固定站位分镜。[换姿落脚与起手时序](../mods/boss战%20瓦伦希娜/招式演出分段.md#0416-换姿落脚与起手时序)
- 0.3.4 以原片第 728 与 751 帧复核切割攻击的左眼、鞘口和暗红尖弧。左眼在刘海下，预闪坐标和脚下落点共用 `ValennaPoses.cs`，浏览器从 `pose-pivots.json` 按同一姿势换挂点。校正首斩和举刀收势，随后按原片低扫、直刺与拦击阶段检查七招。切割分层和原片帧号见模组内 `招式演出分段.md`，本机巴士状态图见 `状态图标索引.md`。

- 0.3.5 逐帧复核八招的亮刃、余焰和旋转。切割、解兔、解鹿和「洞穿」的主副刃须各自对齐，尖端打击用长短双刺，解象与处置各有从近竖到横扫的过渡。改起点、错时、宽度和转向在 `StrikeProfiles.cs`，从短刃沿弧长出的遮罩在 `SlashFramePainter.cs`。浏览器使用同一组贴图、倍率与平滑转向参数。浏览器工程与保存检查图共用 32 层上限。关键帧号和图见模组内 `招式演出分段.md`。

- 0.3.6 攻击栏按当前行动槽角色是否拥有「巴勒莫之剑」885141 筛选。数据表的 `ShowCheck` 与按钮层同时限制七项主动技，旧测试行用于兼容。伙伴入场时朝左。残影取姿势透明度，切图时保留上帧位置。
- `ValennaPrescience.cs` 在原版生成临时骰后加奖惩骰，在 `ActionEffectProcess` 按目标累计闪避结果，等同一个敌人的 `BattleActiveBehaviorData.Run` 完了再判定一次解兔反击。低层阈值按扣 5 层之前检查。连段成功继承同一组奖励，失败后下一段重新发动。战斗 `RoundStart` 和探索 Buff 的地图回合事件分别回层，两个状态表用 `OverrideInitLayer` 设为 30 与 0。眼标动画与状态图标独立，朝向和透明度跟随实际闪避。详见模组内 `README.md`。

- 0.3.7 把反击与发放挂在「预知」特质 `885142`，充能、闪避奖惩和「加速的未来」挂在预知眼装备 `885112`。`ValennaEyeEquipment.cs` 走 `EnterModule` 和入战入口，查装备与背包后使用原版 `EquipItem` 穿戴护具栏（`EEquipType.Cloth`）。神话、绑定和不可卸写进物品数据。`ValennaPrescience.cs` 在受击和直接伤害入口共用命中记录，同一次命中只扣一次。`AcceleratedFuture.cs` 叠层后由招式入口保留整招倍率，解兔跳过倍率与清层。装备图标取 `饰品-9256.webp`，状态图标取 `加速的未来.webp`，原图路径及登记键见模组内 `状态图标索引.md`。

- 0.4.0 将“大行动轮开始”对齐到选择行动前的原版 `BeforeRoundStart`，该事件在首轮 `LookStage` 和以后各轮 `PreprepareStage` 触发。原版 `RoundStart` 已在行动选择后。`ValennaHunt.cs` 在每第 3 轮的 `ResultStage` 完成后决定是否安排补标，下一大行动轮实际开始时施加。处置条件同时走按钮遮罩、目标过滤和提交后的再校验。七项招式配置与预知数值仍在 `StrikeProfiles.cs`。
- 普通与灼热震颤的配置及代理入口见 `ValennaTremor.cs`。原生燃烧为 104，原生昏迷为 101。雷横默工坊号 `3587695213`，运行时查四张状态表 686122775、68612299、68614495、68612256 来决定是否调用它的状态。瓦伦希娜出战时对手模型右侧从开战起显示空的混乱条；引爆增长，满槽触发原生昏迷。联动模式跟踪本模组发起的引爆及其实际意志降幅。
- 本机挂载以瓦伦希娜的工坊目录 `3845388588` 为准。编译并运行 `tools/test_install.ps1` 后，可核对正式目录、工程与待上传包的插件哈希。启动日志的「0.5.5 已载入」包含实际 DLL 路径。旧宿主 `3813306323` 的 `-Remove` 只处理旧哈希清单记录的文件。编辑器应用音轨时也会同步本机瓦伦希娜目录。
- `ValennaPrescience.cs` 等全部分段的后处理完成才统计本次闪避和攻击。原版部分 `ActionEffectProcess` 调用按回调推进。攻击、呼吸法和狩猎伤害改在实际 `SetDamage` 取值，狩猎与未来累计小数，避免 1 点测试分段被逐段向上取整。暴击数字按命中范围定位 HUD，池化数字重置字号。其余装备、初始特质和旧伙伴升级见 `ValennaKit.cs` 与 `tools/build_data.ps1`。
- 0.4.2 把扫刃、气流、碎屑与收势余风错开。最外侧暗红尖弧随原刀光向外卷，在亮刃消失后继续渐隐。`SweepFrames` 从 24 改为 26 时同步更新了浏览器素材 ID 和各层时长。浏览器大小输入上限为 650，长直刺用现有横纵比例保持实际尺寸。调参和修改前后关键帧见[招式演出分段](../mods/boss战%20瓦伦希娜/招式演出分段.md#042-逐帧复查)。
- 0.4.3 由 `tools/IdleBreathPainter.cs` 生成原生待机图帧，`src/ValennaPoses.cs` 共用呼吸变形和眼、鞘、脚点坐标。`pose-pivots.json` 中的 `referenceIdle` 保留浏览器旧工程的模型引用。切割攻击保留 1.47 秒的举刀与外圈，之后才接下放和回握。`StrikeEffects.Advance` 先推进人物位移再重算已有挂件，保持新刀光的起扫年龄。 [待机与出招衔接](../mods/boss战%20瓦伦希娜/招式演出分段.md#043-待机与出招衔接)
- 0.4.4 收势首帧匹配对应招式的末帧，提前结束的连段接通用回握。处置的 `gun` 像素坐标由 `ValennaPoses.cs` 写进 `pose-pivots.json`。运行时和浏览器用同一挂点在举枪后播放蓄光。调举枪停留时长时核对 `AimLead` 以及开火时人物是否已切到持枪帧。[其余招式的收势](../mods/boss战%20瓦伦希娜/招式演出分段.md#044-其余招式的收势)

- 0.4.6 修复了启动和入本。Harmony 补丁类中的 `Prepare` 会被识别为初始化钩子，普通异步方法用业务名称。特质的 `CreateEvent.Inherent` 不能有空项，补给 `ResultFuncs` 的枚举键按 ES3 原样写裸整数。带 `UniqueKey` 的装备实例用派生 ID，去重与识别使用配置 `ItemId`。幕间和入本按实有资料修复旧伙伴，移除重复特质时撤销其独立来源的数值。
- 瓦伦娜的头像实际导出 64×64，供同行者小卡和人物详情按原生尺寸显示。海琳娜、调查员的原版待机精灵为 49–56 像素、64 像素每单位，瓦伦娜统一用 550 像素每单位，左侧伙伴朝右。`StrikeProfiles.PlaybackRate` 给整条招式演出提速，闪避眼标的渐隐单独计时。

- 0.4.7 在 `StrikeCue.PassDistance` 按人物高度指定穿身距离。`StrikeSequence` 在每段接触处结算伤害，处置举铳时按穿身后的反向使用枪口挂点。人物动画、位移与招式特效同用 `StrikeProfiles.PlaybackRate`。命中红线由 `SlashFramePainter.DrawHitLine` 生成，角度和渐隐由 `StrikeEffects` 控制。浏览器固定站位，仅用于比对光效。

- 0.4.9 的 `BeatPause`、处置起手和开火定势把实际秒数乘以 `PlaybackRate` 后写入分镜。停顿中 `StrikeEffects.Advance` 继续推进，余风的后半单独延长。位移残影在 `SamplePose` 按距离采样。处置的 `ExecutionBladeTip` 按平剑切图脚点换算，`PlayExecutionFinish` 控制回身、靠近、插入和开火。`valenna_finish_*` 动作表按用户指定的切图分段，`SweepPoseLead` 以 30 帧分镜为基准提前两帧播放刀光。处置起手圆环和切割红风由 `SlashFramePainter` 独立生成，亮粒和圆环共用轮廓半径。解象的蓄刺汇光、直刃和末斩余风在 `StrikeEffects` 内分开设置。两次转身横斩和中间的竖剑回旋由 `build_presentation.ps1` 的 `valenna_elephant_*` 动作表编排。横斩以 `skill2_10.png` 接 `skill2_11.png` 翻动披风，归位用末帧斩姿。原生 `PlaySpriteAnimation.Play` 会把帧间隔改成 0.1 秒，`ValennaAppearance.KeepPoseInterval` 只为瓦伦娜切图恢复动作表的间隔和首帧时长。调整动作表或绘制资源后重跑 `build_presentation.ps1` 和 `build.ps1`。 [动作与分镜索引](../mods/boss战%20瓦伦希娜/招式演出分段.md#049-停顿与位移)
- 0.4.10 的 `LeadCue` 按斩击或直刺在换姿前接浅风，`MotionFlashFrames` 只给参考里有前闪的节拍加亮，仍走原伤害节点。处置起手的 `ExecutionOpeningTail` 延展刀光帧序列的后半段。人物、效果和位移恢复 5/3 倍，三个停顿按实际秒数换算。0.4.15 的续刺和旋刃让解象分镜超出原有 48 层，编辑器共用的 `animation.mjs/maxLayers` 已扩至 64，导入与检查共用此值。[原片帧号与时序](../mods/boss战%20瓦伦希娜/招式演出分段.md#0410-换姿前的光效)
- 0.4.11 修复战斗重开后的演出。原版 `GameWorld.ReloadRoleOnRestartBattle` 调用 `SwitchRoleModel`，会停用 `RoleModel` 组件，切图播放器仍可用。`StrikeSequence.HasSprite` 检查渲染对象，`UninstallOnRestart` 前释放旧演出，并用参战数据引用识别重开后的角色。命中将目标置为死亡时先清理招式、恢复动作方，再等待原版死亡与命中回调。
- 瓦伦娜的职业显示沿用伙伴卡的 `RoleData.ProfessionName` 入口，`ValennaAppearance.PartnerProfessionName` 按伙伴 ID 885101 返回「蜘蛛巢 拇指父辈」。新旧伙伴卡共享该入口，职业成长仍走伙伴配置。

### 23.2 插件层（次选）：手工构造 `EffectShowData` 的两个静默坑

`DamageData.Play` 会驱动普通攻击和原版反击的表现。荆棘由插件按段播放后，武器数据里相同的特效与音效需要移除，避免双播。先发与吸血由 `SecretFx.cs` 单独播放。
手工构造数据时有两个字段是**旧版遗留、运行时不读**，
漏填就是**静默不播、日志里连报错都没有**：

- `PlayRoleEffectData.FxPath`：标注是"旧版特效(仅作查看用)"。运行时优先读 **`FxPathReference`**
  （`PrefabResoureReference`，`ReferenceType = EPrefabReferenceType.Effect(10001)`，
  `Key` = **资源库里的文件名**，如 `FX_BladeHit`，**不是** `Effect/Prefabs/FX_BladeHit` 这种路径）。
  两者都填最稳：原版数据也是两个都留着。
- `AudioClipData.AudioRes`：类定义里是 `[ShowIf("@false")]`（永远隐藏）的过时字段。
  `AudioManager.PlaySound(AudioClipData)` 只读 **`AudioReference`**
  （`AudioResourceReference`，`ReferenceType = EAudioReferenceType.Sound(2)`，`Key` = 音效文件名，如 `blade_hit`）。
  **只填 AudioRes = 完全没声音**（这次"没感觉到特效"的一半原因就是它）。

资源名去哪查：游戏自带注册表 `Depersonalization-Release_Data\StreamingAssets\InternalConfigure.txt`，
`EffectResUnitDatas` / `SoundResUnitDatas` 两段里是 `Key`+`Path` 成对的全量清单。
想快速确认"这个特效路径到底读不读得到"，可以在插件里 `Resources.Load<GameObject>(fxPath)` 试一下打日志
（`SecretFx.Probe` 就是这么写的，稳定后可删）。

### 23.3 本地 ↔ 工坊同步清单（改特效时别落东西）

`plugins\AttackTargetVisualizer.dll` + 改到的 `Item\*.txt`（还有生成脚本）——
数据文件也**必须一起同步到工坊目录**，否则游戏读到的还是旧数据（插件更新了也没用）。

## 24. 插件里怎么"主动发起一次正常弹骰子的检定"（2026-09-27 孤影追击限制踩出来）

荆棘那次做的是**隐藏检定**（自己 `Random.Range`，不弹面板）。这次孤影要"正常弹骰子"，
就需要直接驱动游戏自己的骰子面板。路子是有的，而且很短：

```csharp
// 1. 构造检定数据：属性检定 / 技能检定 / 二级属性检定 三种重载
CheckDiceData diceData = new CheckDiceData(EHeroAttribute.POW, role, isCompareDice: false);
//   技能：new CheckDiceData(EExploreSkill.xxx, role, extraData, skillSelectData, isCompareDice)
//   二级：new CheckDiceData(ERoleExtraAttribute.xxx, role, isCompareDice)

// 2. 让游戏面板自己演一遍（正常骰子演出 + 成功/失败判定都由游戏算）
await PrefabSingleton<UIDicePanel>.Instance.Roll(EDiceShowType.None, diceData,
    isLoop: true, EDiceResult.Success, -1, null);

// 3. 拿结果
EDiceResult result = PrefabSingleton<UIDicePanel>.Instance.CurrentResult;  // 档位（含大成功/大失败）
int roll = PrefabSingleton<UIDicePanel>.Instance.RollResultValue;          // 骰面值（写日志用）
```

参考实现：原版 `SkillSelectData.GetResult`（反编译 115739 附近，探索/幕间的检定都是这条路）。
几个要点：

- `CheckDiceData` 内部会自己调 `BattleHelper.GetFinalCheckValue`，所以**装备/buff 对检定的修正全都会算进去**
  （花香那种挂在 `GetDiceCheckValue` 上的"+5 成功区间"同样生效）。
- `EDiceShowType.None` = 含大成功大失败的正常骰；想要暗骰用 `DarkDiceNone`。
- 面板不是随时都有实例：动手前判 `PrefabSingleton<UIDicePanel>.HasInstance`，没有就走"按失败处理"的兜底，
  别让异常把行动链卡住（`async void` 里抛异常非常难查）。
- 调用点必须**已经脱离同步事件链**。孤影这套是排队到下一帧的 Tick 里执行才 `await` 的
  （触发点在 `TriggerBuffs` 的 Prefix 里，是同步方法）。

**顺带记两个常用判断（同一轮挖出来的）：**

- **"这次行动是不是攻击行动"**：`role.CurrentBehaviourData.IsAttackAction`。
  它对投掷返回 false（`ActionType == EBattleActionType.Throw` 时 `IsAttackAction` 恒为 false），
  但**法术伤害也算 true**（`MagicData.Config.Type == EMagicType.Attack`）——要排除法术就用
  `behaviour.MagicData != null` 再挡一道。
- **精神状态**：`role.Data.SanState` → `ESanState.None(正常) / Full(充沛) / Weak(衰弱) / Collapse(衰竭)`，
  对应的 buff 常量是 `GameConstants.SAN_FULL_BUFF_ID = 209`、`SAN_WEAK_BUFF_ID = 301`、`SAN_COLLAPSE_BUFF_ID = 302`。
- **扣精神值**：`await role.Data.ChangeAttr(true, new ChangeAttrData(ERoleExtraAttribute.CurrentSan, "-2"), "", false, false)`；
  先读当前值用 `role.Data.GetRoleExtraAttrValue(ERoleExtraAttribute.CurrentSan)`（和葬花扣 1 点回充能同一套写法）。

## 25. 停用清单（用户要求"注释掉"的效果都记在这）

按用户要求停用、但代码留着方便恢复的效果，一律用 `#if false ... #endif` 包住（**不要直接删代码**），
恢复时把 `#if false` 改回 `#if true` 就行。**文本要同步去掉**（中文在数据文件、英文在 `SecretText.cs`）。

| 日期 | 效果 | 代码位置 | 文本 |
|---|---|---|---|
| 2026-09-29 | 白毛少女：战斗开始时魅惑有理智的敌人 | `SecretTraits.ProcessWhiteHair`（`if (HasSan(enemy))` 分支） | `Trait\880022.txt` WakeEvent 第一行 + SleepyEvent 的"不再魅惑敌人" + `SecretText.WhiteHairEffect` 首行 |
| 2026-09-29 | 百合花：受到魅惑的持续时间翻倍 | `SecretTraits.OnBeforeBuffAdd` | 文案里原本就没有这条（`Trait\880021.txt` / `Buff\880024.txt` 都没写）；`SecretText.LilyEffect` 的 "Charm lasts 100% longer on you." 已删 |

注意：`Patch_BuffData_AddBuff` **不能整个注释掉** —— 它同时还负责百合花环的"免疫骨折/流血/中毒/燃烧"
（`ShouldBlockBuff`），停用魅惑时长只动 `OnBeforeBuffAdd` 内部那一段。

## 26. 战斗背景 / BGM 的运行时改法（2026-09-30，做「自定义播放战斗背景和BGM」挖出来的）

这一节是做 `mods\自定义播放战斗背景和BGM` 时确认的机制，**改战斗画面/音乐都从这里抄**。

### 26.1 战斗背景是 2D 精灵场景，别想着换场景

- 战斗场景 = `StreamingAssets\Game\SceneTemplate\<名字>.txt`（战斗场景名以 `ZDCJ_` 开头），
  内容是一整个 2D 场景（`SpriteRenderer` + URP 2D 光照），按排序层分：
  **`BG`（背景）/ `Chara`（角色）/ `Front`（前景）**。
- 战斗相机是**正交**的，`MOD_Entity_BaseRoom.Cam` 可以拿到；视野高度 `2 × camera.orthographicSize`
  （默认 2.4 → 高 4.8 世界单位），宽度 = 高度 × aspect。场景里的背景块一般是 9×5（略大于视野）。
- **想给战斗"换背景"最稳的办法是叠加层**，不是改场景：
  世界空间 Canvas 挂到战斗相机下（`localPosition = (0,0,10)`），
  `canvas.sortingLayerName = "BG"` + `sortingOrder = -1000`（比场景里所有背景都低），
  尺寸按相机视野算。图片/视频铺满它 → 盖住原背景、但**不挡角色和前景**；战斗结束 `Destroy` 这一层就复原。
  - 运行时相机：`BattleHelper.FightContent.Room.RoomEntity.Cam`。
  - "从右往左展开"这类擦除动画用 **`RectMask2D` 容器宽度动画**：容器 `anchor/pivot` 钉在右边缘、
    宽度 0 → 全宽、子 `RawImage` 右边缘对齐容器右边缘，图像就在屏幕上固定、裁剪窗口从右向左扩大
    （图片/视频通用，因为都是 `RawImage`）。
  - 图片铺屏用 `RawImage.uvRect` 手算 cover（保持比例、裁掉多余），视频用 `VideoPlayer.aspectRatio`。

### 26.2 BGM：`AudioManager` 的四个要点

| 要什么 | 怎么拿 |
| --- | --- |
| 当前 BGM 的"键" | `AudioManager.CurrentBgm.Key`（+ `ReferenceType`）——这就是"原始值" |
| 正在播的音频源 | `AudioManager.BgmSource`（public `AudioSource`：`clip` / `loop` / `volume` / `isPlaying`） |
| 走游戏通道切歌 | `AudioManager.SetBgm(AudioClipData)`（注意：**内部写死 `loop = true`**） |
| 硬盘音频文件 | `UnityWebRequest` + `DownloadHandlerAudioClip`，按扩展名选 `AudioType.MPEG / OGGVORBIS / WAV` |

**两个必须知道的坑**：

1. **`SetBgm` 不能实现"只播一次"**：它硬编码 `BgmSource.loop = true`。
   要做单次播放就自己 `Play()` 并把 `loop = false`，再在 `Update` 里盯着 `BgmSource.isPlaying`
   变 false 后切回原曲（记得给开始播放留 0.5 秒宽限，刚 `Play()` 的帧 `isPlaying` 还不可靠）。
2. **切歌前必须杀掉 `_volumeTween`**：游戏用 `SetBgmFadeInOut` 做 1.5 秒淡入，tween 完成时才执行
   `Stop()` + 换 clip。你在这 1.5 秒里自己换歌，tween 结束会把**你的** BGM 顶掉/停掉。
   `_volumeTween` 是私有字段，反射拿它再 `DG.Tweening.TweenExtensions.Kill(tween, false)` 即可
   （字段名拿不到就安静跳过，别抛）。

另外：战斗结束游戏自己会调 `MapContent.BackRoomBgm()` 恢复房间 BGM；
你自己的复原逻辑挂在 `GameWorld.ExitBattle` 的 Prefix 上（那时游戏还没恢复，你先归位、它随后接管）。
重开战斗走 `GameWorld.RestartBattle`，记得把"本场会话"状态清掉。

### 26.3 硬盘上的图片/音频/视频怎么加载

| 类型 | 做法 |
| --- | --- |
| png / jpg | `File.ReadAllBytes` + `Texture2D.LoadImage`（同步，够快） |
| mp3 / ogg / wav | `UnityWebRequestMultimedia.GetAudioClip("file://" + path, type)`，等 `req.result == Success` 后 `DownloadHandlerAudioClip.GetContent(req)` |
| mp4 | `VideoPlayer`：`source = Url`、`url = "file://" + path`、`isLooping = true`、`aspectRatio = VideoAspectRatio.FitOutside`（保持比例铺满；**没有 `Fill` 这个枚举值**）、`audioOutputMode = None`（静音）、`renderMode = RenderTexture` + `targetTexture`，然后 `Prepare()` → 轮询 `isPrepared`（加超时！）→ `Play()`。游戏自己的 `UICustomVideoPanel` 就是这套 |

> 游戏自己的资源加载入口 `ResourceLibraryHelper`（`_LoadResByExtra`）也支持这套磁盘加载：
> 运行时往 `ResourceLibraryManager.Instance.Data.ResourceDatas` 里塞一条 `ResourceData`
> （`Extend.ExtraFolderPath` + `Path`）就能让游戏的 `AudioResourceReference.GetRes` 直接按 Key 取到文件。
> 做"让游戏自己识别新资源"的需求时走这条路。

### 26.4 打开系统文件管理器（选文件）

游戏自带，直接用，别引第三方库：

```csharp
FolderBrowserHelper.SelectFile(delegate(string path) {
    if (string.IsNullOrEmpty(path)) return;   // 玩家取消了
    // path 是完整路径
}, "战斗背景 (*.mp4;*.png;*.jpg)\0*.mp4;*.png;*.jpg");
```

- 过滤串格式：`描述\0模式`（和 Win32 的 `OPENFILENAME` 一致），多组用多个 `\0` 连接；
- 它是**同步模态**对话框（调用期间游戏主线程会卡住），和游戏自己的用法一致；
- 反编译位置：`FolderBrowserHelper.SelectFile`（`Assembly-CSharp` 221417 附近）。

### 26.5 编译 / 挂载两个新坑

1. **csc 命令行会超长**：引用游戏 `Managed` 里**全部** dll（两百多个）时，拼命令行会撞 Windows
   的长度上限（报 `The filename or extension is too long`）。解法：把参数写进 **响应文件**
   （每行一个、带引号），用 `dotnet exec csc.dll "@args.rsp"` 编译。
   模板见 `mods\自定义播放战斗背景和BGM\tools\build.ps1`。
2. **测试挂载不要覆盖宿主已有文件**：把 `Project_Depersonal` 合并进宿主时，宿主自己可能已有
   `Project.rtmeta`；无脑覆盖并记进"卸载清单"，卸载时就会把宿主自己的文件删掉。
   正确做法：**已存在的文件跳过 + 不记清单**（见同目录 `build.ps1`）。
   （2026-09-30 踩过：宿主 `2919360679` 的 `Project.rtmeta` 被覆盖成了别的 mod 的版本）

### 26.6 幕间入口的隐藏与"重写"（2026-10-01，做接口时挖的）

幕间度假列表的构建点是 **`UIInterludePanel.UpdateVacationInfo(HeroRoleData role)`**：

```csharp
ClearAddEle();                                   // 先把旧元素全部 Recycle
List<InterludeVacationConfig> datas = Singleton<ResManager>.Instance.InterludeVacationFactory.Datas;
foreach (...) {
    if (checks 通过) {
        UIChooseAddElement el = Pool_ChooseAdd.Alloc<UIChooseAddElement>();
        el.Fresh(config, role, ScrollView_ChooseAdView);
        ChooseAddElements.Add(el);               // ★ public List<UIChooseAddElement>
    }
}
CurAddEle = null;
```

- **想隐藏某个入口**：在 `UpdateVacationInfo` 的 **Postfix** 里倒着遍历 `panel.ChooseAddElements`，
  命中自己的 `VacatId` 就 `panel.Pool_ChooseAdd.Recycle(el.transform)` + `RemoveAt(i)`——
  `CurAddEle` 在方法末尾已经被清掉，这时回收是安全的（`自定义播放战斗背景和BGM` 就是这么做的）。
- **想接管点击**：入口的点击走 `UIInterludePanel._onClickVacationMode(config, tarRole)`，
  在里面 Prefix 拦下来就行（自定义心和自定义背景/BGM 都用这个挂点）。
- **想让别的模组能开关/重写你的入口**：暴露一个 public 静态 API（投票开关 + 注册回调 + 启动日志打印"谁关了哪些"），
  参考 `mods\自定义播放战斗背景和BGM\src\DomainApi.cs` 与同名 mod 的 `接口文档.md`。

### 26.7 把战斗背景/BGM/领域效果开放给其它模组（2026-10-18）

- 公开 API 在 `mods\自定义播放战斗背景和BGM\src\DomainApiRuntime.cs`，与旧 `DomainApi.cs`
  用 `partial class DomainApi` 合并；旧入口接口签名不变。
- 外部模组用 `RegisterDomain(requester, id, DomainDefinition)` 注册自己的背景/BGM/效果；
  资源可直接给绝对路径，或用 `AssetRoot + 相对路径`，不复制进玩家资源库。
- `ExpandDomain(requester, id, BattleRole caster, reason)` 可在任意战斗方法里直接展开，
  不要求【领域】特质；`PlayVisual / PlayBackground` 依赖战斗相机，`PlayBgm` 任意场景可用。
- **战斗开始时机的准确边界**：`BattleFightContent` 先创建 `Room`，再 `Run()` 触发
  `BeforeBattleStart` / `BattleStart`，所以这两个回调里可以直接展开；更早的
  `GameWorld.EnterBattle` Prefix 可能还没有 `FightContent.Room`，要用
  `DomainApi.IsBattleReady()` 判断或延后到 BattleStart。
- `BindAction(...)` 把定义预先绑定到 `BattleSkillData.Id` / `MagicData.Id` / 任意法术，
  使用时由 `DomainBattle.OnActionStart` 触发；外部绑定优先于玩家自己的「挂载行动」。
- 外部定义仍走 `DomainProfile` / `DomainEffects` / `DomainBuffBuilder` 同一条运行时路径：
  `DomainProfile` 新增 `RuntimeDisplayName / ExternalOwner / ExternalId /
  BackgroundFullPath / BgmFullPath` 等运行时字段（不写 `profiles.cfg`）。
- `DomainEffects` 的目标选择已改为**按施法者阵营**：玩家施法仍走 Allies/CurWaveEnemies，
  怪物/NPC 施法时友方=当前波次敌人、敌方=Allies；给敌人做领域时不会反过来。
- 外部 domain 的 buff 编号与玩家配置共用 `881011-881999` 段，统一经
  `DomainStore.AllocateBuffId()` 分配；`DomainApi.IsBuffIdInUse()` 防止两边撞号。
- 非战斗调用 `PlayBgm` 时只记录一份原始 BGM，不会把战斗会话标记弄脏；
  `StopPlayback` 负责复原。完整示例见 mod 的 `接口文档.md`。
- **展开文字框/音效（2026-10-18）**：`DomainProfile` 新增
  `ShowOpenDialog / PlayOpenSound / SfxFile / RuntimeDialogSuffix`，外部
  `DomainDefinition` 对应 `ShowDialogOnOpen / PlaySoundOnOpen / SoundPath / DialogSuffix`；
  音效放资源库 `Sfx`（ogg/mp3/wav），播放走 `AudioManager.PlaySound(clip,...)` 的 SFX 通道。
  表现使用自定义 `DomainTextBoxOverlay`，按 `Text` preferred size 自动排版，
  位置优先取行动牌上边框中点，再取 `RoleModel.Trans_Head` 上方；
  整个屏幕再叠一张纯网格径向暗角（中心透明、边缘黑），随文字约 2.5 秒淡入淡出。
  独立 ScreenSpaceOverlay Canvas 使用 `overrideSorting = true`、`sortingLayerName = "UI"`、
  `sortingOrder = 30000`，所以战斗开始、行动槽 HUD 还在时也会盖在行动槽上面；不拦截鼠标。
  后缀用 `BaseMeshEffect` 按可见字符区间做逐字交错上下浮动（相位 `i * π`），不依赖旧版 Text
  不支持的动态富文本标签。

**表现尝试记录（不要重复踩坑）**：

1. `RoleModel.ShowDialog(DialogBoxData)`：角色头像对话框，已实现过；它不是用户想要的“黑底文字框”。
2. `GamePlayEvent.TimeLine.ShowBlackScreenOption` → `UIScreenTipPanel.AutoShowTip(ScreenTipData)`：
   原版全屏黑屏文字提示。已实现过，会整屏变黑，不符合“保留战斗画面 + 黑框文字”的目标；
   但该参考仍保留，后续做黑屏剧情/秒杀演出时可复用。
3. 《谎言与欺诈》夜魔：模块在 `GameModules\HUANGYANYUQIZHA`，战斗场景在
   `GameModules\ZHANDOUCHANGJING\Room\YEMOZHANDOU.txt`；技能 `MonsterBattleSkill\29.txt`
   「黑暗拥抱」实际用 `ShowHUDTextOption` 显示“桀桀桀桀桀”（HUD 文本），
   `MonsterBattleSkill\32.txt`「层层恐惧」是黑暗恐惧 buff。夜魔并没有一个可直接复用的
   “黑色文字框”效果，因此最终采用自定义 overlay。
4. 当时的稳定方案：`DomainTextBoxOverlay` 只保留角色头顶文字 + 边缘暗角；
   `SoftEllipseGraphic` 已从代码中删除。定位优先取技能牌 / 编辑行动牌的上边框中点，
   没有行动框时再取 `RoleModel.Trans_Head`（模型顶边）。

表现层在 `DomainEffects.ApplyOnOpen` 里触发，失败不影响背景/BGM/效果。
外部模组不注册领域也可以调用 `DomainApi.ShowHeadText(requester, text, caster, duration)` /
`ShowHeadTextForRole(...)` 在任意时机给角色头顶弹文字；`HideHeadText` 主动关闭。

**渲染踩坑（重要）**：第一版用 `RawImage` + 运行时 RGBA 纹理画椭圆底，实机出现“文字有、黑框没有”；
第二版改成 `Image + Sprite.Create(...)` 仍然没有黑底；第三版用
`SoftEllipseGraphic : MaskableGraphic` 纯网格（32×16 规则网格、按椭圆距离计算顶点 alpha）依旧不显示，
并且启用后会把文字一起弄没。用户已决定停止黑底实验，稳定版只保留文字 + 边缘暗角，
`SoftEllipseGraphic` 已删除。`EdgeVignetteGraphic : MaskableGraphic` 画全屏暗角是已验证可用的，
后续不要再把椭圆网格重新加回来。
Canvas 使用能显示文字/暗角的 **Screen Space - Overlay**；不要擅自切到 Screen Space - Camera，
实测会让整个 Canvas 的文字和网格一起消失。

**领域黑底气泡（2026-11-20，本机测试版）**：领域展开走 `DomainPresentation.ShowTextBox`，
传 `speechBubble: true`。黑色圆角底板、细边和指向施法者的小尖角追加到
`EdgeVignetteGraphic` 的现有顶点色网格，原有 CanvasGroup 一起控制淡入淡出。
用文字容器世界角点转暗角网格的局部坐标，静止时复用网格。文字按实际内宽测量换行。
靠屏幕上沿且下方有空间时，气泡挪到锚点下方并把尖角移到上沿。
普通外部 `DomainApi.ShowHeadText` 沿用原表现。编译与网格验证可离线进行，画面显示仍以实机为准。
早期调研记录见 [领域文字底板调研](自定义背景-领域文字底板调研.md)。

**行动框定位（2026-10-18 修正）**：HUD 的 `GetWorldCorners` 结果属于 HUD 自己的 Canvas，
不能直接交给战斗相机做 `WorldToScreenPoint`。正确做法照抄
`mods\可视化攻击目标\src\ArrowLayer.cs` 的 `TryGetSlotAnchor / TopCenterOf`：
先 `hud.GetComponent<Canvas>()` 取 `worldCamera`，再用
`RectTransformUtility.WorldToScreenPoint(hudCam, (corners[1] + corners[2]) * 0.5f)` 转屏幕坐标；
Screen Space - Overlay 时 `worldCamera == null`，换算结果仍正确。技能牌优先取
`Anim_SkillShow` → `Button_SkillIcon` → `Trans_SkillShow`，编辑行动牌取 `Trans_EditorAction`。

**工坊其它模组调研（2026-10-18）**：

- `3801731500` 可视化攻击目标：有可参考的 `ArrowGraphic : MaskableGraphic`，但只画箭头，没有文字气泡；
- `3229419762` 蠕形之海、`3343752458` 旧日余辉：技能/事件里确有 `ShowBlackScreenOption`，但它是
  `UIScreenTipPanel` 全屏黑屏文字，不是角色头顶框；
- `3149119351` 诡舰迷踪、`3252047536` 逆转召唤、`3381413249` 是谁杀了知更鸟等：
  主要用 `ShowHUDTextOption` / `NpcEachDialogOption`，即 HUD 文本或角色头像对话；
- 角色/模型/伙伴类模组（`3031614456`、`3305525765`、`3413045383`、`3422602415`、`3810060135` 等）
  基本是纯数据/模型资源，没有自定义文字气泡 UI；
- 当前工坊内没有可直接复用的“角色头顶黑色椭圆文字框”实现。
- **特质醒/睡口径**：`DomainMenu.HasDomainTrait` 只检查特质是否存在，幕间编辑不受沉睡影响；
  战斗开始自动展开仍由 `HasAwakeTrait` 判定。沉睡描述已改为
  “不在战斗开始自动播放，不影响挂载的行动”。
- **表现层触发条件**：`DomainBattle.ApplyProfile` 返回背景是否真正应用成功（无背景配置视为成功）；
  `DomainEffects.ApplyOnOpen(..., showPresentation)` 只在该值为 true 时播放文字框/音效。
  这样背景文件缺失、战斗相机缺失或图片加载失败时不会出现“背景没换却有文字框”。

## 27. 「多行动槽」：动战斗核心流程的七个关键点（2026-10-04，做 `mods\多行动槽` 挖出来的）

这一节是做「多行动槽」（一个角色多个行动槽、逐槽选/逐槽打）时确认的机制。
**以后要改"选行动 / 出手顺序 / 行动槽 HUD"，先看这节，能省掉一整天。**
完整设计见 `docs\多行动槽-需求与设计.md`。

1. **选行动的真相：整个 ActionStage 只等一次 `IsActionDecision`。**
   `BattleFightContent.EnterPhase(ActionStage)` 对每个角色调 `RoleEnterActionStage`（async void，不 await），
   所有角色的 `ActionSelect` **一起挂在后台等**；`IsActionDecision` 是等**全部角色都选完**时
   由 `CheckAllAllyActionFinish()` 统一设置的——**不是"每个角色选完就返回一次"**。
   所以"让一个角色选 N 次"不能写成 `for (i<N) await ActionSelect()`（第二次会永远等不到），
   正确做法是：**一次 `ActionSelect` 覆盖整个角色的多槽**，"推进到下一个槽"放在
   `UIBattlePanel._OnNextPhaseButtonClicked` 的 Prefix 里做（玩家每次确认 → 切指针 → 刷新 UI → 返回 false）。
   敌人 AI 不同：`BattleNpcRole` 选完立刻 `IsActionDecision = true`，可以逐槽循环。

2. **出手顺序：`CheckActionSequence()` 生成的是"角色级"列表**（`OrderBy(-Speed)`，LINQ 稳定排序），
   `OnCalculationResult()` 逐个角色跑 `CurrentBehaviourData.Run()`。想多槽就**替换 `OnCalculationResult`**
   （Prefix 返回 false + 自己塞 `__result`），照抄原逻辑、把"每角色一次"改成"该角色逐个槽"；
   **不要展开 `SequenceList`**——`UpdateSkillIconAndActionOrder`、`PriorToSelfEnemyCount` 都会读它，
   展开会把先攻数字和 buff 判定搞乱。

3. **每幕开始的最稳钩子是 `BattleFightContent.UpdateControlRoleOnRoundStart`**（同步 void）：
   它在战斗安装时调一次、每幕 `ActionStage` 开头（玩家选行动之前）调一次，正好是"进战斗 /
   每幕开始"两个时机，而且没有 async 坑。

4. **槽位数据用"指针切换"实现最省事**：让 `role.CurrentBehaviourData` 指向"当前正在编辑 / 执行的槽"，
   游戏其余逻辑（检定、目标、buff、日志、伤害）全部照常。新建的 `BattleActiveBehaviorData` 要手动补
   `Self = role; RollDice.Role = role;`（就是 `BattleActionData.Install` 的全部内容，别去 await 它）。
   小心里面大量逻辑会**直接替换** `CurrentBehaviourData`（昏迷恢复、怀表重放）——进自己的逻辑前
   先把"游戏当前指针"收编成当前槽的数据。

5. **HUD 多槽 + 「可视化攻击目标」免改兼容**：那个 mod 的锚点优先级是
   `Image_ActionOrder.activeSelf` → `Button_SkillIcon.activeInHierarchy` → `Trans_SkillShow`（取上边框中点）。
   所以多槽的做法是：**隐藏原版数字 / 图标按钮**，用 `Anim_SkillShow` 的克隆体显示各槽，
   `Trans_SkillShow` 留在原位置当"组容器"（它就是整组的对称中心）。克隆体记得把 Animator 禁用，
   否则默认状态可能把图标盖掉；UI 节点的 active 状态要自己维护（游戏的 `CloseSkillIcon` 会关整组，
   选到一半时要把已选槽重新亮出来）。

6. **`IEnumeratorAwaitExtensions.GetAwaiter(...)` 在插件编译环境里 await 不了**：
   `SimpleCoroutineAwaiter` 的 `GetAwaiter` 解析不到（编译报 CS1929）。要在自己的 `async Task`
   里等游戏时间，就写一个 `TaskCompletionSource` + 插件自己 `StartCoroutine` 的包装
   （参考 `mods\多行动槽\src\SlotUtil.cs` 的 `SlotWait`）。

7. **插件静态字段不要叫 `Enabled`**：`BaseUnityPlugin` 自带一个 `Enabled`（类型是 `ConfigEntry<bool>`），
   撞名会报"无法将 bool 隐式转换为 ConfigEntry<bool>"这种看着莫名其妙的错。我们自己的总开关叫
   `ModEnabled`。

8. **【首次实机踩到】`RoleData.CanControlAI` 对调查员恒为 `false`**：它的定义是
   `AI != null && ConfigData.CanControlInBattle` 或"是 HeroNpc 的 BattleNpcRole"——玩家调查员
   （`BattlePlayerRole`）两条都不满足。**判断"这是不是玩家控制的角色"要用
   `role.IsHero || role is BattlePlayerRole`**；`CanControlAI` 只用来认"可控制 AI 的友方 NPC"。
   这次错了的症状：玩家被当成 AI 走"逐槽循环"，第二次 `ActionSelect` 永远等不到
   `IsActionDecision`，整个选行动阶段卡死。

9. **HUD 克隆的一个顺序坑**：先隐藏原节点再 `Instantiate`，克隆体会**复制隐藏状态**
   （表现为"槽框在、图标全没了"）。要克隆就先克隆、再隐藏原节点；或者克隆后把
   按钮 / 图标子节点重新 `SetActive(true)`。

10. **给预设角色/怪物直接分配槽数（2026-10-18）**：`ExtraActionSlotApi` 新增
    `SetExtraSlotCountForRoleKey / SetExtraSlotCountForConfigId / SetExtraSlotCountForRoleName`。
    这些是"创作时登记"的静态规则，不依赖战斗中的 `BattleRole` 实例；
    `SlotManager.CalcTargetCount → ExtraActionSlotApi.GetExtraCount` 在每场战斗/每一幕
    重算时自动匹配 `RoleData.RoleLibraryKey / ConfigId / Name`。同一 `sourceKey` 对同一角色
    只算一次（多条命中取最大值），不同来源与运行时 `SetExtraSlotCount` 叠加。
    调查员用稳定 `RoleLibraryKey`，怪物/NPC 优先用 `ConfigId`；名字匹配只作兜底。

## 28. 「可视化攻击目标」：对抗配色与目标改写（2026-10-18）

**对抗配色按双方各自的行动类型判，不按左右/阵营固定**：

- 攻击型（`IsAttackAction`，含伤害法术；投掷按红色攻击型处理）vs 攻击型 → 两条黄色；
- 技艺（`EBattleActionType.Skill`）vs 技艺 → 两条青蓝色；
- 技艺 vs 攻击型 → 技艺侧蓝、攻击侧红，两个箭头头仍在中点相撞。

实现入口：`mods\可视化攻击目标\src\ArrowLayer.cs` 的 `AddDuel` / `DuelSideColor`；
颜色常量在 `src\ArrowSpec.cs`。完整需求见 `docs\可视化攻击目标-需求与设计.md` 第 3.3 节。

**原版“改攻击目标”调研结论**：`BattleActiveBehaviorData.InterceptTarget` 只会被
`BattleSkillEvent.InterceptOption` 写入；原版 BattleSkill 数据里只有 `7 诱敌` 使用它。
诱敌的单体目标分支实际走 `ClearCurrentActionOption`（取消目标行动），
不是“把目标原本打 A 改成打诱敌者”；因此不存在用户举例的 A/B/C 单体强制改指向，
本轮没有实现该预测。以后若模组技能使用 `InterceptOption`，先确认 `TargetType` /
`ForceIntercept` 与目标行动类型，再决定是否把虚拟目标覆盖接入箭头配对。

**自指向箭头（2026-10-18）**：`ETargetSelect.Self`，或 `Targets` 里实际包含自己时，
走 `ArrowLayer.AddSelfArrow`：起点和普通箭头一致（`TryGetSlot(..., true)`，
行动槽 `Anim_SkillShow` / `Trans_SkillShow` 的**上边框中点**），终点取角色模型
`Trans_Head` 的世界坐标投屏，画**蓝色虚线直线**（`ArrowSpec.Straight = true`）。
它单独使用 `SelfCanvas` / `SelfGraphic`，排序值为行动槽画布的 `-1`；普通箭头层是 `+10`。
因此线段穿过行动槽的部分会被牌子遮住，视觉上从牌子下沿露出。
群体行动同时包含自己和队友时，队友照常画，自己额外多一条自指向箭头；
拿不到行动槽就不画，不退化成头顶对头顶。

## 29. 「自定义心」附加效果用的 buff 事件（2026-10-18）

「自定义心」的附加效果没有新增 Harmony 钩子，而是直接写进心 buff 的
`BuffTableData.Events`，由游戏 `BuffData.TriggerBuff` 按节点执行。可用的触发值：

| 界面文字 | 枚举 | 实际时点 |
| --- | --- | --- |
| 行动轮开始 | `BeforeRoundStart` (15) | 和自定义BGM 每轮施加效果一致；比 `RoundStart` (2) 更早 |
| 行动轮结束 | `RoundEnd` (3) | 回合结算末尾 |
| 攻击命中后 | `Damage` (7) | 攻击者结算伤害后触发；聚合参数里带 `RoleHitData` |
| 行动前 | `ActionStart` (4) | `BattleRole.ActionStart()` |
| 行动后 | `ActionEnd` (5) | `BattleRole.ActionEnd()` |

目标用 `Buff_BuffChangeOption.TargetType` / `Buff_BuffLayerOption.TargetType`：

- 自己 → `BuffTarget`
- 所有友方（含自己）→ `AllAlly`
- 仅限友方（不含自己/后备）→ `AllAllyExceptSelfAndBackUp`
- 单个敌人 → 命中触发用 `BeHitRole`，其余触发用 `RandomNotDeadEnemy`
- 所有敌人 → `AllEnemy`

“加 N 层”是 `BuffChangeOption(Add)` + `BuffLayerOption(+N-1)`，因为 `AddBuff` 首次自带 1 层。
**不要**直接写 `ChangeLayer(N)`，否则会多 1 层（这也是自定义BGM 之前踩过的“层数多 1”坑）。

预设效果（自定义BGM 的物理/法术 强化、守护、弱化、易损）在自定义心里用独立的隐藏 buff 承载：
`OverlayType=OverlyLayer`、`MaxLayer=配置层数`、百分比在生成时按 `层数×10%` 写死。
这样重复触发不会无限加层，角标层数与实际百分比一致。隐藏 buff 的 `Comment` 用
`自定义心效果：` 前缀，和心本体的 `自定义心：` 分开，清理时不会误删别的心的核心状态。

状态列表收集必须同时遍历 `BuffFactory.All` 和 `BuffFactory.LoadFileDatas`（逐条 `GetConfig`），
否则游戏本体的中毒/流血等状态不会出现；具体实现见 `mods\自定义心\src\HeartBuffCatalog.cs`。

**幕间入口的特质判断（2026-10-18 用户口径）**：`HeartMenu.HasHeartTrait` 只判断
`role.GetTraitData(880901) != null`，**不要**再要求 `CurrentState == ETraitState.Wake`。
特质沉睡时仍然允许打开「修改当前角色【心】」编辑窗；添加/移除入口也统一按“有没有特质”判断。

**心效果自清理（2026-10-18）**：心附加效果给自己/友方的状态，通过
`HeartEffectCleanupOption` 挂在心的一个 `Stable` 事件里；`BuffData.RemoveBuff` 会遍历
所有 `Config.Events.Funcs` 执行 `TriggerRemoveOpts`，所以心解除/倒下/战斗结束/手动移除都会走
`OnRemove`，按状态 ID 移除全部层数。敌人效果不主动清；同一状态如果有别的来源也会被一起清掉。

## 30. 「自定义心」外观编辑器（2026-11-18，0.2.1）

当前规则见[自定义心-外观编辑器.md](自定义心-外观编辑器.md)。自动拟合已停用，统一默认X=0.05、Y=0.05、宽高缩放均0.30。
用户明确：按调查员分别保存；原点使用原始特效播放挂点(0,0)，不是人物中心。预览、战斗使用同一局部坐标。
模态弹窗提供待机人物、动态心焰、固定坐标轴、独立宽高和居中的应用/重置按钮；保存默认可点，成功弹出确认，确认后关闭两层弹窗回原面板。草稿可叉号/Esc关闭不保存，原面板数值草稿保留。
幕间角色自身RoleLibraryKey可能为空，须经HeartVisualRoleKey从角色库解析稳定编号，不能因此把保存按钮置灰。实现另见HeartVisualSettings.cs、HeartVisualEditor.cs、HeartVisualPreview.cs和旧文件名HeartFxWidthFitter.cs。40项检查及用户实测通过；验证入口为`mods/自定义心/tools/test_visuals.ps1`。
2026-11-18已用独立上传器更新原工坊物品3801085814，manifest=3068979684825246233。发布时显式锁定旧编号，干净暂存包只含Project_Depersonal/plugins/Uploader；上传器先`--dry-run`校验，再提交并通过线上API核对。详见外观编辑器文档发布记录。

## 32. 杀戮尖塔2「Valencina」工坊素材提取（2026-11-22）

用于给 `mods/boss战 瓦伦希娜` 做 boss 演出的对照参考。工坊条目 **3747747045**「Valencina」0.10.4（AppID 2868840），内容在
`E:\SteamLibrary\steamapps\workshop\content\2868840\3747747045\`：`Valencina.pck`（Godot 4.5.1，292MB）、`Valencina.dll`、`compat\` 三个版本分支。

**成品位置**：`mods\boss战 瓦伦希娜\valencina_ref\`（已在该 mod 的 `.gitignore` 排除，不参与上传）。
入口文档 `valencina_ref\README.md`，参数读法 `valencina_ref\params\参数说明.md`。
`valencina_ref\preview\` 里有贴图总览和 3 份粒子轨迹图（S1/S7/1306_S4，每格一个 canvas，蓝→黄→红为时间序）；
`params\particles\` 是同样的粒子中心点数据（按 canvas 归一化到 0–1），可直接拿来量刀光扫过的角度和弧长。

### 32.1 这套模组的角色与素材分布

- **1306 = `thumb_disciple`（瓦伦希娜侧）**，1379 = `thumb_father`（Boss 侧）。两者各有 `animations\native\<id>\character.res`、`spine.res`、`native_fx\*`。
- 特效贴图在 `native_fx\<序号>\` 的 `material.res` 里引用（`raw_tex_0..3`），纹理文件按 sha1 命名，必须先查材质表才能知道哪张图属于哪层。
- 角色帧/卡绘/敌人逐帧在 `Valencina\images\...`；原图在 pck 里都是 `.ctex`（WebP 容器）。
- 演出数据分两层：`stage\shows\*.json`（逻辑分段、命中时间）与 `stage\bake\*.bake`（30fps 逐帧数值）。两者时间轴要分开读。

### 32.2 格式要点（以后还要解别的 Godot 4 PCK 时照这个来）

- **PCK v3**：头 `GDPC` + u32(3) + 引擎 4.5.1 + `pack_flags=2`（相对基址）+ `file_base=128` + `dir_offset`（本包在 0x11703980）+ file_count。
  目录项 = `u32 path_len(含 4 字节对齐)` + path + `u64 offset` + `u64 size` + `md5[16]` + `u32 flags`；文件真实位置 = file_base + offset。
  脚本 `temp\pck\pck_index.py`，索引产物 `temp\pck\index.json`。**注意**：v3 的目录项比旧版多一个 `u32 flags`，少了会全体错位。
- **`.ctex`**：`GST2` + version + u32 w/h + data_format + mipmap_limit + 3×u32 保留；36 字节后是内层
  `u32 data_format + u16 w/h + u32 mipmaps + u32 format`，随后 `u32 size` + WebP 字节（2D 无损）。
  最省事的转 PNG 方式是 Godot 4.5.1 无头加载：`--headless --script export.gd`，脚本里 `ProjectSettings.load_resource_pack(pck, true)` 后 `ResourceLoader.load` + `Image.save_png`。
  工具在 `tools-external\godot\Godot_v4.5.1-stable_win64_console.exe`，脚本 `C:\Users\huawei\AppData\Local\Temp\godot_tool\export.gd`（可重建）。
- **`.res`（RSCC）**：`RSCC` + `u32 cmode(=2 zstd)` + `u32 block_size(4096)` + `u32 解压后总长` +
  块数 × `u32 压缩长` + 各块 zstd 数据 + 尾部 `RSCC`。解压后是压缩版 `RSRC`（省略了 4 字节 `RSRC` 头），
  从 `big_endian/use_real64/ver_major/ver_minor/ver_format` 开始，接着是类名、import 偏移、flags、uid、11 个保留字、
  字符串表、外部资源表、内部资源表、各资源属性。**二进制变体编号和 `Variant::Type` 不是一套**：`STRING=5, COLOR=20, OBJECT=24, DICTIONARY=26, ARRAY=30, VECTOR4=50, PROJECTION=52`。
  解析器 `temp\pck\rsrc.py`（含 zstd 解压与全部变体）。材质资源挂了 C# 脚本（`NativeParticleCanvasDefinition.cs`），标准 Godot 加载会因缺少脚本类型失败，直接解 RSRC 更快。
- **`.bake`**：raw deflate（`zlib.decompress(data, -15)`），magic `1262568022`、version 2；头里有 show id、近/远目标位、canvas 定义、
  事件、音频 cue、帧数组（body/spines/mangs/canvas 粒子）。帧率固定 30fps。事件 kind：`0 GiveDamage`、`1 TweenMove`、`2 HitChecker`、`3 EndChecker`、`4 CameraShake`、`5 Knockback`、`6 Fire`、`7 MangChanged`。
  解码器 `temp\pck\bake_decode.py`，摘要脚本 `bake_summaries.py`。

### 32.3 可复用的脚本与产物

| 脚本 | 用途 |
| --- | --- |
| `temp\pck\pck_index.py` | 列 PCK 全部文件，写 index.json |
| `temp\pck\rsrc.py` | RSCC/RSRC 解压与属性解析（zstd 用 MSYS2 的 libzstd.dll） |
| `temp\pck\bake_decode.py` | bake 逐帧解码 |
| `temp\pck\bake_summaries.py` | 27 份 bake 的帧时间线/事件/粒子摘要 |
| `temp\pck\build_ref.py` | 组装 `valencina_ref`（贴图分类、shader 去重、spine/character 参数、清单） |
| `temp\pck\params_build.py` | 生成 shader_uniforms、attack_timeline、material_textures |

注意：`valencina_ref` 约 140MB（其中 `textures\monsters` 79MB、`cards` 47MB），只放本地；要清理直接删这个目录即可，
`temp\pck` 里有可重跑的脚本。原模组作者资源版权自负，别直接打进要公开发布的 mod。

