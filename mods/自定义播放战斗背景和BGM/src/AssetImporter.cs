// 资源导入：打开系统文件管理器 → 校验格式 → 复制到 mod 的资源目录。
//
// 文件选择器直接用游戏自己的 FolderBrowserHelper（Comdlg32 原生对话框），
// 和游戏"上传预览图"用的是同一套东西，不引第三方库。
// 复制规则（用户口径）：
//   · 背景只收 mp4 / png / jpg，BGM 只收 ogg / mp3；
//   · 目标文件夹里已经有同名文件 → 取消并提示"资源重名"（不覆盖）。

using System;
using System.IO;

namespace CustomBattleBg
{
    /// <summary>问玩家"要不要覆盖同名文件"：编辑器提供实现（弹确认框），assetImporter 只管调用。</summary>
    internal delegate void AskOverwriteHandler(string fileName, Action onOverwrite, Action onCancel);

    internal static class AssetImporter
    {
        /// <summary>打开文件管理器挑一张背景，成功则回调复制后的文件名。</summary>
        internal static void PickBackground(Action<string> onPicked, Action<string> onHint, AskOverwriteHandler ask)
        {
            PickFile(
                "战斗背景 (*.mp4;*.png;*.jpg)\0*.mp4;*.png;*.jpg",
                DomainConstants.BackgroundExtensions,
                DomainConstants.BackgroundFolderName,
                CustomBattleBgPlugin.Store.BackgroundDir,
                "这个文件格式不对：背景只支持 mp4 / png / jpg。",
                onPicked,
                onHint,
                ask);
        }

        /// <summary>打开文件管理器挑一首 BGM，成功则回调复制后的文件名。</summary>
        internal static void PickBgm(Action<string> onPicked, Action<string> onHint, AskOverwriteHandler ask)
        {
            PickFile(
                "战斗BGM (*.ogg;*.mp3)\0*.ogg;*.mp3",
                DomainConstants.BgmExtensions,
                DomainConstants.BgmFolderName,
                CustomBattleBgPlugin.Store.BgmDir,
                "这个文件格式不对：BGM 只支持 ogg / mp3。",
                onPicked,
                onHint,
                ask);
        }

        private static void PickFile(string filter, string[] allowedExts, string kindName, string targetDir,
            string badExtMsg, Action<string> onPicked, Action<string> onHint, AskOverwriteHandler ask)
        {
            try
            {
                FolderBrowserHelper.SelectFile(delegate(string path)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(path))
                        {
                            return;   // 玩家取消了
                        }
                        string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                        bool ok = false;
                        for (int i = 0; i < allowedExts.Length; i++)
                        {
                            if (ext == allowedExts[i])
                            {
                                ok = true;
                                break;
                            }
                        }
                        if (!ok)
                        {
                            Hint(onHint, badExtMsg);
                            return;
                        }

                        string fileName = Path.GetFileName(path);
                        if (string.IsNullOrEmpty(fileName))
                        {
                            return;
                        }

                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        string target = Path.Combine(targetDir, fileName);
                        if (File.Exists(target))
                        {
                            // 同名文件已存在 → 问玩家要不要覆盖（用户口径：不再直接拒绝）
                            if (ask != null)
                            {
                                string src = path;
                                string dst = target;
                                string name = fileName;
                                ask(fileName,
                                    delegate { DoCopy(src, dst, name, kindName, onPicked); },
                                    delegate { Hint(onHint, "已取消覆盖，资源库里原来的文件没有动。"); });
                                return;
                            }
                            Hint(onHint, "资源重名：目标文件夹里已经有同名文件，换一个名字或先删掉旧的。");
                            CustomBattleBgPlugin.LogInfo("资源重名，已取消复制：" + kindName + "\\" + fileName);
                            return;
                        }

                        DoCopy(path, target, fileName, kindName, onPicked);
                    }
                    catch (Exception e)
                    {
                        CustomBattleBgPlugin.LogError("导入资源失败：" + e);
                        Hint(onHint, "复制资源失败，看日志。");
                    }
                }, filter);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("打开文件选择器失败：" + e);
                Hint(onHint, "复制资源失败，看日志。");
            }
        }

        private static void DoCopy(string src, string target, string fileName, string kindName,
            Action<string> onPicked)
        {
            try
            {
                bool existed = File.Exists(target);
                File.Copy(src, target, true);
                CustomBattleBgPlugin.LogInfo((existed ? "已覆盖" : "已导入") + kindName + "资源：" +
                    fileName + "  （来自 " + src + "）");
                if (onPicked != null)
                {
                    onPicked(fileName);
                }
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("复制资源失败：" + src + " → " + target + " —— " + e.Message);
            }
        }

        private static void Hint(Action<string> onHint, string msg)
        {
            try
            {
                if (onHint != null)
                {
                    onHint(DomainText.L(msg));
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
