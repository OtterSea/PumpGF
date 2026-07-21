using System;
using System.Collections.Generic;
using UnityEngine;

namespace PumpGF
{
    /// <summary>
    /// 效用选择节点（Utility AI 收敛点）：对每个子节点计算"效用分"，选出得分最高者执行。
    /// <para>打分流水线（每个子节点）：
    /// <c>raw = scorer(ctx)</c> → Max 归一化到 [0,1] → 可选 <c>AnimationCurve</c> 重映射 → <c>× weight</c> → 取最高分。</para>
    /// <para>被选中的子节点返回 Running 时，默认继续执行同一子节点（避免决策抖动），
    /// 直到其完成或被中止后才重新评分。</para>
    /// <para>框架只提供打分机制；具体 scorer、曲线、权重由业务定义。</para>
    /// </summary>
    public sealed class UtilitySelectorNode : CompositeNode
    {
        /// <summary>每个子节点对应的打分配置（与 Children 一一对应）。</summary>
        internal sealed class Scorer
        {
            public Func<TickContext, float> Score;
            public float Weight = 1f;
            public AnimationCurve Curve; // 可空
        }

        private readonly List<Scorer> _scorers = new(4);

        // 当前选中的子节点索引（-1 表示未选中）
        private int _selected = -1;

        // 打分缓冲（预分配，避免每帧分配）
        private float[] _rawBuffer;

        /// <summary>为最近添加的子节点附加打分配置（构建期调用）。</summary>
        public void SetScorer(Func<TickContext, float> score, float weight, AnimationCurve curve)
        {
            // 补齐 scorer 列表到与 Children 对齐
            while (_scorers.Count < Children.Count)
                _scorers.Add(new Scorer());

            if (_scorers.Count == 0) return;
            var s = _scorers[_scorers.Count - 1];
            s.Score = score;
            s.Weight = weight;
            s.Curve = curve;
        }

        protected override void OnEnter(in TickContext context)
        {
            base.OnEnter(in context);
            _selected = -1;
        }

        protected override NodeStatus OnTick(in TickContext context)
        {
            if (Children.Count == 0) return NodeStatus.Failure;

            // 若已有选中且仍在 Running，继续执行它（避免抖动）
            if (_selected >= 0 && _selected < Children.Count)
            {
                var st = Children[_selected].Tick(in context);
                if (st == NodeStatus.Running) return NodeStatus.Running;
                // 完成 → 允许下次重新评分
                _selected = -1;
                return st;
            }

            int best = SelectBest(in context);
            if (best < 0) return NodeStatus.Failure;

            _selected = best;
            var status = Children[best].Tick(in context);
            if (status != NodeStatus.Running)
                _selected = -1;
            return status;
        }

        private int SelectBest(in TickContext context)
        {
            EnsureBuffer();

            // 1. 收集原始分
            float maxRaw = 0f;
            for (int i = 0; i < Children.Count; i++)
            {
                float raw = 0f;
                if (i < _scorers.Count && _scorers[i].Score != null)
                    raw = _scorers[i].Score(context);
                if (raw < 0f) raw = 0f; // 负分钳制为 0
                _rawBuffer[i] = raw;
                if (raw > maxRaw) maxRaw = raw;
            }

            // 2. 归一化 + 曲线 + 权重，取最高
            int bestIndex = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < Children.Count; i++)
            {
                float normalized = maxRaw > 0f ? _rawBuffer[i] / maxRaw : 0f;

                float shaped = normalized;
                float weight = 1f;
                if (i < _scorers.Count)
                {
                    var sc = _scorers[i];
                    if (sc.Curve != null) shaped = sc.Curve.Evaluate(normalized);
                    weight = sc.Weight;
                }

                float finalScore = shaped * weight;
                if (finalScore > bestScore)
                {
                    bestScore = finalScore;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        public override void Abort(in TickContext context)
        {
            base.Abort(in context);
            _selected = -1;
        }

        private void EnsureBuffer()
        {
            if (_rawBuffer == null || _rawBuffer.Length != Children.Count)
                _rawBuffer = new float[Children.Count];
        }
    }
}
