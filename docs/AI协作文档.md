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
- [ ] 角色卡的**装备**（护符/药剂/特殊武器）搬到 `Game\Item\`
- [ ] 想做"副本结束才获得的特质"要配 `Game\InterludeVacat\`，参考 `3490801895`
- [ ] 实测"放进编辑模组工程目录"这条本地测试路径
- [ ] 把跑团卡特质包正式上传工坊，摆脱临时挂载
