# 「自定义播放战斗背景和BGM」· 对外接口文档

> 给**其它模组作者**看的：怎么开关 / 重写本 mod 的幕间入口。
> 面向版本：`CustomBattleBg.dll`（2026-10-01 起）。
> 本文件不进上传包。

---

## 0. 插件信息

| 项 | 值 |
| --- | --- |
| 插件 DLL | `CustomBattleBg.dll` |
| BepInEx GUID | `codex.depersonal.custombattlebg` |
| 命名空间 | `CustomBattleBg` |
| 对外接口类 | `CustomBattleBg.DomainApi`（public static） |

---

## 1. 幕间入口一览

本 mod 会在幕间时光里挂三条入口。每条都有一个**键**（key），外部模组就是拿这个键来操作的：

| 键（常量） | 值 | 对应幕间入口 | 幕间数据编号 |
| --- | --- | --- | --- |
| `DomainApi.MenuAdd` | `"add"` | 添加「领域」（给角色加【领域】特质） | `InterludeVacat 881003` |
| `DomainApi.MenuEdit` | `"edit"` | 编辑自定义战斗背景和BGM（打开编辑面板） | `InterludeVacat 881002` |
| `DomainApi.MenuRemove` | `"remove"` | 移除「领域」（删特质 + 删配置 + 删资源） | `InterludeVacat 881004` |

---

## 2. API

### 2.1 开关入口

```csharp
DomainApi.SetMenuEnabled(string menuKey, string requester, bool enabled);
DomainApi.IsMenuEnabled(string menuKey);   // 查询当前是否可用
```

- `menuKey`：上面的键（`"add"` / `"edit"` / `"remove"`）；
- `requester`：**填你自己模组的名字**（会写进日志，方便排查是谁关的）；
- `enabled`：`false` = 关闭（入口**不再显示**在幕间列表里）；`true` = 恢复；
- **投票规则：任何一个模组设为 `false`，这个入口就不展示**；所有注册方都允许时才可用；
- 没注册过任何开关 = 默认可用。

### 2.2 重写入口行为

```csharp
DomainApi.SetMenuOverride(string menuKey, string requester, Action<HeroRoleData> handler);
DomainApi.ClearMenuOverride(string menuKey, string requester);   // 取消（只有当前生效的是自己时才清）
```

- 注册后，玩家点这条幕间入口时**执行你的 `handler`**（参数是选中的调查员 `HeroRoleData`），不再走本 mod 的默认逻辑；
- 同一个入口重复注册时，**最后注册的生效**；
- 入口被关闭（2.1）时，重写不会被调用（因为入口根本不显示）。

### 2.3 日志

- 插件在游戏就绪时会打印一次全部入口的状态，例如：

```
幕间入口状态：
  添加「领域」：正常（没有模组注册开关）
  编辑自定义面板：已关闭（关闭者：我的模组）
  移除「领域」：正常（开启者：某某模组）
```

- 每次有人调用 `SetMenuEnabled` / `SetMenuOverride` 时，也会打印对应的变更和最新状态。

---

## 3. 怎么调用

### 3.1 方式 A：引用 DLL（推荐）

把你的插件源码里加上引用：`<创意工坊目录>\<本mod 的 id>\plugins\CustomBattleBg.dll`，
然后：

```csharp
using CustomBattleBg;

// 关掉"编辑面板"这条入口
DomainApi.SetMenuEnabled(DomainApi.MenuEdit, "我的模组", false);

// 重写"添加「领域」"：改走自己的界面
DomainApi.SetMenuOverride(DomainApi.MenuAdd, "我的模组", delegate(HeroRoleData role)
{
    // role 就是当前选中的调查员
    MyMod.OpenMyPanel(role);
});
```

### 3.2 方式 B：反射调用（不想加引用时）

```csharp
Type api = Type.GetType("CustomBattleBg.DomainApi, CustomBattleBg");
if (api != null)
{
    api.GetMethod("SetMenuEnabled").Invoke(null, new object[] { "edit", "我的模组", false });
}
```

> 程序集名就是 `CustomBattleBg`（对应 `CustomBattleBg.dll`）。
> 找不到类型说明本 mod 没安装或者没启用，**直接跳过即可**，不要抛异常。

### 3.3 调用时机

- 在你插件的 `Awake()` 里调用就行（本 mod 的入口列表是在玩家**打开幕间**时动态构建的，晚注册也能生效）；
- 如果你希望"如果我这个模组没启用，就自动不关"——那不用做任何事：**没调用 = 不关**。

---

## 4. 行为细节（避免踩坑）

1. **关闭是"隐藏"而不是"删除"**：被关闭的入口会从幕间列表里移除（UI 池回收），数据表本身不动；
2. **关闭期间点击无效**：即使因为某些原因列表里还留着，点击也不会执行（会直接忽略）；
3. **重写只影响"点击之后干什么"**，入口的标题/说明文字仍然是本 mod 数据表里的（想改文字可以覆盖 `InterludeVacat 881002-881004` 的本地化）；
4. **同时关闭 + 重写**：关闭优先（重写不会被调用）；
5. 本接口是**只加不改**的最小面：以后加新能力会新增方法/常量，不会改现有签名。

---

## 5. 变更记录

| 日期 | 内容 |
| --- | --- |
| 2026-10-01 | 首版：`SetMenuEnabled` / `IsMenuEnabled` / `SetMenuOverride` / `ClearMenuOverride` + 三个入口键 + 启动日志 |
