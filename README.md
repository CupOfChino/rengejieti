# 人格解体本地存档

放置「人格解体」mod 的全部工程内容。一个 mod 一个文件夹，可分别上传创意工坊、分别启用。

## 目录结构

```
人格解体本地存档\
├─ docs\                    知识库与清单（看这里入门）
│   ├─ AI协作文档.md         游戏 mod 体系的全部要点：数据格式、ID 表、坑、资料位置
│   └─ 模组清单.md           每个 mod 对应哪些角色卡、占了哪些 ID、当前状态
├─ mods\                    一个子文件夹 = 一个可独立上传的 mod
│   └─ <模组名>\
│       ├─ Project_Depersonal\   ← mod 本体，上传工坊只需要这个
│       ├─ Uploader               ← 上传元数据（标题/简介/标签）
│       ├─ README.md              这个 mod 装了什么
│       └─ tools\                 这个 mod 自己的生成脚本
├─ tools\                   通用脚本：挂载测试 / 卸载 / 新建模组
├─ tools-external\          第三方工具（如 ILSpy），不进 git
├─ temp\                    临时文件与备份，不进 git
└─ .gitignore
```

## 常用操作

```powershell
# 内容生成（每个 mod 自己的脚本）
powershell -File "mods\跑团卡特质包\tools\build_traits.ps1"

# 本地测试：把某个 mod 临时挂到游戏里"已启用的 mod"下面
powershell -File "tools\install_test.ps1"   -ModName 跑团卡特质包

# 测试完还原
powershell -File "tools\uninstall_test.ps1" -ModName 跑团卡特质包
```

**为什么本地 mod 要挂在别的 mod 下面**：游戏只会加载「Steam 已订阅且已启用」的 mod 里的数据内容，
所以没上传的本地内容直接丢进创意工坊目录是不生效的。测试期用挂载脚本借用别人的目录，
正式做法是上传工坊后订阅自己那份。

## 改完必须重启游戏

数据表在游戏启动时一次性读入，热改不生效。
