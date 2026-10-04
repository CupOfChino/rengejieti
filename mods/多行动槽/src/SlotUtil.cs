// 小工具：把 Unity 协程等成 Task（给 async Task 流程用）。
//
// 不直接用游戏的 IEnumeratorAwaitExtensions——它来自别的程序集，
// 在插件编译环境里 await 它拿不到正确的 GetAwaiter。这里用插件自己的 MonoBehaviour
// 起协程，等的是 WaitForSeconds（受 timeScale 影响，和原版节奏一致）。

using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace DuoActionSlot
{
    /// <summary>
    /// 让「编辑行动」克隆牌保持在我们给定的位置（在 LateUpdate 里压住它自己动画录制的位移），
    /// 同时保留动画的缩放 / 透明度等效果——这样"编辑行动"的放大动画仍然会出现。
    /// </summary>
    internal sealed class SlotEditorFollower : MonoBehaviour
    {
        private RectTransform _rect;
        private Vector2 _base;
        private float _offset;

        internal void Init(RectTransform rect, Vector2 basePos)
        {
            _rect = rect;
            _base = basePos;
            _offset = 0f;
            Apply();
        }

        internal void SetOffset(float offset)
        {
            _offset = offset;
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            if (_rect != null)
            {
                _rect.anchoredPosition = _base + new Vector2(_offset, 0f);
            }
        }
    }

    internal static class SlotWait
    {
        internal static async Task Seconds(float seconds)
        {
            TaskCompletionSource<object> source = new TaskCompletionSource<object>();
            if (DuoActionSlotPlugin.Instance == null)
            {
                return;   // 插件还没起来：不等待，别把主循环卡住
            }
            DuoActionSlotPlugin.StartRoutine(WaitSeconds(seconds, source));
            // 超时兜底：协程万一没跑（对象被禁用等），也不让战斗卡死。
            int timeoutMs = (int)((seconds + 2f) * 1000f);
            await Task.WhenAny(source.Task, Task.Delay(timeoutMs));
        }

        internal static async Task EndOfFrame()
        {
            TaskCompletionSource<object> source = new TaskCompletionSource<object>();
            if (DuoActionSlotPlugin.Instance == null)
            {
                return;
            }
            DuoActionSlotPlugin.StartRoutine(WaitFrame(source));
            await Task.WhenAny(source.Task, Task.Delay(1000));
        }

        private static IEnumerator WaitSeconds(float seconds, TaskCompletionSource<object> source)
        {
            if (seconds > 0f)
            {
                yield return new WaitForSeconds(seconds);
            }
            source.TrySetResult(null);
        }

        private static IEnumerator WaitFrame(TaskCompletionSource<object> source)
        {
            yield return new WaitForEndOfFrame();
            source.TrySetResult(null);
        }
    }
}
