using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AttackTargetVisualizer
{
    /// <summary>
    /// 把一串箭头画成 UGUI 网格。纯代码生成，不需要任何贴图/材质素材。
    /// </summary>
    internal class ArrowGraphic : MaskableGraphic
    {
        private readonly List<ArrowSpec> _arrows = new List<ArrowSpec>();

        // 当前这条曲线的采样表
        private readonly List<Vector2> _points = new List<Vector2>();
        private readonly List<float> _lengths = new List<float>();
        private float _total;

        /// <summary>虚线相位（画布单位）。每帧往前推，虚线就朝终点流动。</summary>
        internal float DashPhase;

        internal void SetArrows(List<ArrowSpec> arrows)
        {
            _arrows.Clear();
            _arrows.AddRange(arrows);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i < _arrows.Count; i++)
            {
                EmitArrow(vh, _arrows[i]);
            }
        }

        // ---------- 画一条箭头 ----------

        private void EmitArrow(VertexHelper vh, ArrowSpec a)
        {
            Vector2 p0 = a.From;
            Vector2 p2 = a.To;
            Vector2 d = p2 - p0;
            float dist = d.magnitude;
            if (dist < 2f)
            {
                return;
            }

            // 控制点：让弧线一律朝屏幕上方鼓起来
            Vector2 perp = new Vector2(-d.y, d.x) / dist;
            if (perp.y < 0f)
            {
                perp = -perp;
            }
            // 虚线的弧线抬得更高（AOE 一眼能认出来，也不会和单体线糊在一起）
            float arcFactor = a.Dashed ? ArrowStyle.ArcFactor * ArrowStyle.DashArcBoost : ArrowStyle.ArcFactor;
            float arcMax = a.Dashed ? ArrowStyle.DashArcMax : ArrowStyle.ArcMax;
            float arc = Mathf.Clamp(dist * arcFactor, ArrowStyle.ArcMin, arcMax);
            Vector2 p1 = (p0 + p2) * 0.5f + perp * arc;

            SampleCurve(p0, p1, p2, dist);
            if (_total < 1f)
            {
                return;
            }

            float s0 = Mathf.Clamp01(a.PS0) * _total;
            float s1 = Mathf.Clamp01(a.PS1) * _total;
            if (Mathf.Abs(s1 - s0) < 1f)
            {
                return;
            }

            if (a.Dashed)
            {
                EmitDashed(vh, a, s0, s1);
            }
            else
            {
                EmitRun(vh, a, s0, s1);
            }

            if (a.Head)
            {
                EmitHead(vh, a, s1, s0);
            }
        }

        private void SampleCurve(Vector2 p0, Vector2 p1, Vector2 p2, float dist)
        {
            _points.Clear();
            _lengths.Clear();

            int n = Mathf.Clamp(Mathf.CeilToInt(dist / 12f), 12, 64);
            _points.Add(p0);
            _lengths.Add(0f);

            Vector2 prev = p0;
            float acc = 0f;
            for (int i = 1; i <= n; i++)
            {
                float t = (float)i / n;
                float u = 1f - t;
                Vector2 pt = u * u * p0 + 2f * u * t * p1 + t * t * p2;
                acc += Vector2.Distance(prev, pt);
                _points.Add(pt);
                _lengths.Add(acc);
                prev = pt;
            }
            _total = acc;
        }

        /// <summary>取弧长 s 处的坐标；tangent 是"弧长增大方向"的切线。</summary>
        private Vector2 PosAt(float s, out Vector2 tangent)
        {
            tangent = Vector2.right;
            if (_points.Count < 2 || _total <= 0f)
            {
                return _points.Count > 0 ? _points[0] : Vector2.zero;
            }
            s = Mathf.Clamp(s, 0f, _total);

            int i = 1;
            while (i < _lengths.Count - 1 && _lengths[i] < s)
            {
                i++;
            }

            float sa = _lengths[i - 1];
            float sb = _lengths[i];
            float k = (sb - sa) > 1e-4f ? (s - sa) / (sb - sa) : 0f;

            Vector2 a = _points[i - 1];
            Vector2 b = _points[i];
            Vector2 dir = b - a;
            if (dir.sqrMagnitude > 1e-8f)
            {
                tangent = dir.normalized;
            }
            return Vector2.Lerp(a, b, k);
        }

        /// <summary>杆子宽度：根部到位，接近箭头处略收。</summary>
        private float WidthAt(float s)
        {
            float t = _total > 0f ? Mathf.Clamp01(s / _total) : 0f;
            return ArrowStyle.LineWidth * Mathf.Lerp(1f, ArrowStyle.TaperRatio, t);
        }

        private void EmitRun(VertexHelper vh, ArrowSpec a, float s0, float s1)
        {
            float span = Mathf.Abs(s1 - s0);
            if (span < 0.5f)
            {
                return;
            }
            int steps = Mathf.Clamp(Mathf.CeilToInt(span / 7f), 1, 64);

            Vector2 prevPos = PosAt(s0, out Vector2 prevTan);
            float prevS = s0;
            for (int i = 1; i <= steps; i++)
            {
                float s = Mathf.Lerp(s0, s1, (float)i / steps);
                Vector2 curPos = PosAt(s, out Vector2 curTan);
                AddQuad(vh, prevPos, curPos, prevTan, curTan, WidthAt(prevS), WidthAt(s), a.Color);
                prevPos = curPos;
                prevTan = curTan;
                prevS = s;
            }
        }

        private void EmitDashed(VertexHelper vh, ArrowSpec a, float s0, float s1)
        {
            float period = ArrowStyle.DashLength + ArrowStyle.DashGap;
            if (period <= 1f)
            {
                EmitRun(vh, a, s0, s1);
                return;
            }

            float lo = Mathf.Min(s0, s1);
            float hi = Mathf.Max(s0, s1);
            bool forward = s1 >= s0;

            // 相位往前推 → 实段整体朝弧长增大的方向移动 → 观感是"从起点往终点流"
            float phase = Mathf.Repeat(DashPhase, period);
            for (float s = lo + phase - period; s < hi; s += period)
            {
                float da = Mathf.Max(lo, s);
                float db = Mathf.Min(hi, s + ArrowStyle.DashLength);
                if (db - da < 0.5f)
                {
                    continue;
                }
                if (forward)
                {
                    EmitRun(vh, a, da, db);
                }
                else
                {
                    EmitRun(vh, a, db, da);
                }
            }
        }

        private void EmitHead(VertexHelper vh, ArrowSpec a, float sTip, float sOther)
        {
            Vector2 tip = PosAt(sTip, out Vector2 tan);
            float sign = (sTip >= sOther) ? 1f : -1f;
            Vector2 dir = tan * sign;
            if (dir.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector2 back = tip - dir * ArrowStyle.HeadLength;
            Vector2 n = new Vector2(-dir.y, dir.x) * ArrowStyle.HeadHalfWidth;

            int i0 = vh.currentVertCount;
            vh.AddVert(new Vector3(tip.x, tip.y), a.Color, Vector2.zero);
            vh.AddVert(new Vector3(back.x + n.x, back.y + n.y), a.Color, Vector2.zero);
            vh.AddVert(new Vector3(back.x - n.x, back.y - n.y), a.Color, Vector2.zero);
            vh.AddTriangle(i0, i0 + 1, i0 + 2);
        }

        private static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1,
                                    Vector2 t0, Vector2 t1, float w0, float w1, Color color)
        {
            Vector2 n0 = new Vector2(-t0.y, t0.x) * (w0 * 0.5f);
            Vector2 n1 = new Vector2(-t1.y, t1.x) * (w1 * 0.5f);

            int i0 = vh.currentVertCount;
            vh.AddVert(new Vector3(p0.x - n0.x, p0.y - n0.y, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(p0.x + n0.x, p0.y + n0.y, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(p1.x + n1.x, p1.y + n1.y, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(p1.x - n1.x, p1.y - n1.y, 0f), color, Vector2.zero);
            vh.AddTriangle(i0, i0 + 1, i0 + 2);
            vh.AddTriangle(i0, i0 + 2, i0 + 3);
        }
    }
}
