// 图片 / 音频的磁盘加载。
//
// 图片：ReadAllBytes + Texture2D.LoadImage（png / jpg）。
// 音频：走 UnityWebRequest + DownloadHandlerAudioClip（mp3 / ogg），
//       和游戏自己加载 mod 音频的路子一致。

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace CustomBattleBg
{
    internal static class MediaLoader
    {
        /// <summary>同步加载一张 png / jpg。失败返回 null（调用方负责写日志）。</summary>
        internal static Texture2D LoadTexture(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("读取背景图片失败：" + path + " —— " + e.Message);
                return null;
            }
        }

        /// <summary>按扩展名猜 AudioType。</summary>
        internal static AudioType GuessAudioType(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ext == ".mp3")
            {
                return AudioType.MPEG;
            }
            if (ext == ".ogg")
            {
                return AudioType.OGGVORBIS;
            }
            if (ext == ".wav")
            {
                return AudioType.WAV;
            }
            return AudioType.UNKNOWN;
        }

        /// <summary>协程：从磁盘加载一首 BGM，完成后回调（失败回调 null）。</summary>
        internal static IEnumerator LoadAudioClip(string path, Action<AudioClip> onDone)
        {
            string url = "file://" + path;
            AudioType type = GuessAudioType(path);
            if (type == AudioType.UNKNOWN)
            {
                CustomBattleBgPlugin.LogError("不认识的音频格式：" + path);
                if (onDone != null) { onDone(null); }
                yield break;
            }

            UnityWebRequest req = null;
            try
            {
                req = UnityWebRequestMultimedia.GetAudioClip(url, type);
            }
            catch (Exception e)
            {
                CustomBattleBgPlugin.LogError("创建音频请求失败：" + e.Message);
            }
            if (req == null)
            {
                if (onDone != null) { onDone(null); }
                yield break;
            }

            using (req)
            {
                yield return req.SendWebRequest();

                AudioClip clip = null;
                try
                {
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        clip = DownloadHandlerAudioClip.GetContent(req);
                    }
                    else
                    {
                        CustomBattleBgPlugin.LogError("加载 BGM 失败：" + path + " —— " + req.error);
                    }
                }
                catch (Exception e)
                {
                    CustomBattleBgPlugin.LogError("解析 BGM 失败：" + path + " —— " + e.Message);
                }
                if (onDone != null) { onDone(clip); }
            }
        }
    }
}
