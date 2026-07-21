using NUnit.Framework;
using PumpGF;
using R3;
using Cysharp.Threading.Tasks;

namespace PumpGF.Tests
{
    public class BehaviorTreeTests
    {
        // ── Blackboard ──

        [Test]
        public void Blackboard_SetGet_RoundTrips()
        {
            var bb = new Blackboard();
            bb.Set("hp", 42);
            Assert.AreEqual(42, bb.Get<int>("hp"));
            Assert.IsTrue(bb.Has("hp"));
        }

        [Test]
        public void Blackboard_TryGet_MissingKey_ReturnsFalse()
        {
            var bb = new Blackboard();
            Assert.IsFalse(bb.TryGet<int>("nope", out _));
        }

        [Test]
        public void Blackboard_Observe_PushesOnSet()
        {
            var bb = new Blackboard();
            int received = -1;
            var sub = bb.Observe<int>("k").Subscribe(v => received = v);
            bb.Set("k", 7);
            Assert.AreEqual(7, received);
            sub.Dispose();
        }

        // ── Sequence / Selector ──

        [Test]
        public void Sequence_AllSuccess_ReturnsSuccess()
        {
            var tree = BehaviorTreeBuilder.Create("t")
                .Sequence()
                    .Do(_ => NodeStatus.Success)
                    .Do(_ => NodeStatus.Success)
                .End()
                .Build();

            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
        }

        [Test]
        public void Sequence_FirstFailure_ShortCircuits()
        {
            int secondRan = 0;
            var tree = BehaviorTreeBuilder.Create("t")
                .Sequence()
                    .Do(_ => NodeStatus.Failure)
                    .Do(_ => { secondRan++; return NodeStatus.Success; })
                .End()
                .Build();

            Assert.AreEqual(NodeStatus.Failure, tree.Tick(0.1f));
            Assert.AreEqual(0, secondRan);
        }

        [Test]
        public void Selector_FirstSuccess_ShortCircuits()
        {
            int secondRan = 0;
            var tree = BehaviorTreeBuilder.Create("t")
                .Selector()
                    .Do(_ => NodeStatus.Success)
                    .Do(_ => { secondRan++; return NodeStatus.Success; })
                .End()
                .Build();

            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
            Assert.AreEqual(0, secondRan);
        }

        [Test]
        public void Selector_AllFailure_ReturnsFailure()
        {
            var tree = BehaviorTreeBuilder.Create("t")
                .Selector()
                    .Do(_ => NodeStatus.Failure)
                    .Do(_ => NodeStatus.Failure)
                .End()
                .Build();

            Assert.AreEqual(NodeStatus.Failure, tree.Tick(0.1f));
        }

        [Test]
        public void Sequence_Running_ResumesSameChild()
        {
            int firstTicks = 0;
            int secondTicks = 0;
            var tree = BehaviorTreeBuilder.Create("t")
                .Sequence()
                    .Do(_ => { firstTicks++; return firstTicks < 2 ? NodeStatus.Running : NodeStatus.Success; })
                    .Do(_ => { secondTicks++; return NodeStatus.Success; })
                .End()
                .Build();

            // 第 1 帧：first 返回 Running，second 不执行
            Assert.AreEqual(NodeStatus.Running, tree.Tick(0.1f));
            Assert.AreEqual(0, secondTicks);

            // 第 2 帧：first 完成 Success，second 执行
            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
            Assert.AreEqual(1, secondTicks);
        }

        // ── Decorators ──

        [Test]
        public void Inverter_FlipsSuccessToFailure()
        {
            var tree = BehaviorTreeBuilder.Create("t")
                .Inverter()
                .Do(_ => NodeStatus.Success)
                .Build();

            Assert.AreEqual(NodeStatus.Failure, tree.Tick(0.1f));
        }

        [Test]
        public void Condition_GuardsChild()
        {
            bool allow = false;
            int childRan = 0;
            var tree = BehaviorTreeBuilder.Create("t")
                .Condition(_ => allow)
                .Do(_ => { childRan++; return NodeStatus.Success; })
                .Build();

            Assert.AreEqual(NodeStatus.Failure, tree.Tick(0.1f));
            Assert.AreEqual(0, childRan);

            allow = true;
            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
            Assert.AreEqual(1, childRan);
        }

        [Test]
        public void Cooldown_BlocksAfterSuccess()
        {
            var tree = BehaviorTreeBuilder.Create("t")
                .Cooldown(1f)
                .Do(_ => NodeStatus.Success)
                .Build();

            // 首次成功
            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
            // 冷却中 → Failure
            Assert.AreEqual(NodeStatus.Failure, tree.Tick(0.1f));
            // 推进时间超过冷却
            tree.Tick(1.0f);
            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
        }

        [Test]
        public void Repeat_CountedRepeats()
        {
            int ran = 0;
            var tree = BehaviorTreeBuilder.Create("t")
                .Repeat(3)
                .Do(_ => { ran++; return NodeStatus.Success; })
                .Build();

            // Repeat 每帧执行一次子节点直到达到次数
            NodeStatus s = NodeStatus.Running;
            for (int i = 0; i < 5 && s == NodeStatus.Running; i++)
                s = tree.Tick(0.1f);

            Assert.AreEqual(NodeStatus.Success, s);
            Assert.AreEqual(3, ran);
        }

        // ── Composite via Blackboard ──

        [Test]
        public void Check_ReadsBlackboard()
        {
            var bb = new Blackboard();
            bb.Set("inMelee", true);

            var tree = BehaviorTreeBuilder.Create("t")
                .Sequence()
                    .Check(c => c.Blackboard.Get<bool>("inMelee"))
                    .Do(_ => NodeStatus.Success)
                .End()
                .Build(bb);

            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
        }

        // ── UtilitySelector ──

        [Test]
        public void UtilitySelector_PicksHighestScore()
        {
            int lowRan = 0, highRan = 0;
            var bb = new Blackboard();

            var tree = BehaviorTreeBuilder.Create("t")
                .UtilitySelector()
                    .Do(_ => { lowRan++; return NodeStatus.Success; })
                        .WithUtility(_ => 0.2f)
                    .Do(_ => { highRan++; return NodeStatus.Success; })
                        .WithUtility(_ => 0.9f)
                .End()
                .Build(bb);

            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
            Assert.AreEqual(0, lowRan);
            Assert.AreEqual(1, highRan);
        }

        [Test]
        public void UtilitySelector_WeightAffectsChoice()
        {
            int aRan = 0, bRan = 0;

            var tree = BehaviorTreeBuilder.Create("t")
                .UtilitySelector()
                    .Do(_ => { aRan++; return NodeStatus.Success; })
                        .WithUtility(_ => 0.5f, weight: 1f)
                    .Do(_ => { bRan++; return NodeStatus.Success; })
                        .WithUtility(_ => 0.5f, weight: 3f) // 同分但权重更高
                .End()
                .Build();

            tree.Tick(0.1f);
            Assert.AreEqual(0, aRan);
            Assert.AreEqual(1, bRan);
        }

        // ── Reactive ──

        [Test]
        public void Reactive_AbortsChildOnKeyChange()
        {
            var bb = new Blackboard();
            bb.Set("hp", 1.0f);

            int enterCount = 0;
            var tree = BehaviorTreeBuilder.Create("t")
                .Reactive<float>("hp")
                .Do(c =>
                {
                    // 用 running 让节点保持进行中，从而能观测被中止后的重进入
                    if (c.Blackboard.Get<float>("hp") > 0.5f)
                    {
                        enterCount++;
                        return NodeStatus.Running;
                    }
                    return NodeStatus.Success;
                })
                .Build(bb);

            tree.Tick(0.1f); // enter 1 次，Running
            tree.Tick(0.1f); // hp 未变，继续 Running（同一节点不重入）
            Assert.AreEqual(1, enterCount);

            bb.Set("hp", 0.9f); // 值变化 → 中止子节点 → 下帧重入
            tree.Tick(0.1f);
            Assert.AreEqual(2, enterCount);
        }

        // ── Tree lifecycle ──

        [Test]
        public void Abort_ResetsRunningState()
        {
            int enterCount = 0;
            var tree = BehaviorTreeBuilder.Create("t")
                .Do(_ => { enterCount++; return NodeStatus.Running; })
                .Build();

            tree.Tick(0.1f);
            tree.Tick(0.1f); // 仍是同一次 running，不重入
            Assert.AreEqual(1, enterCount);

            tree.Abort();
            tree.Tick(0.1f); // abort 后重新进入
            Assert.AreEqual(2, enterCount);
        }

        // ── Parallel ──

        [Test]
        public void Parallel_RequireOne_SucceedsWhenAnyChildSucceeds()
        {
            var tree = BehaviorTreeBuilder.Create("t")
                .Parallel() // 默认 RequireOne 成功 / RequireAll 失败
                    .Do(_ => NodeStatus.Running)
                    .Do(_ => NodeStatus.Success)
                .End()
                .Build();

            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
        }

        [Test]
        public void Parallel_RequireAllFailure_FailsWhenAllChildrenFail()
        {
            var tree = BehaviorTreeBuilder.Create("t")
                .Parallel(ParallelSuccessPolicy.RequireAll, ParallelFailurePolicy.RequireAll)
                    .Do(_ => NodeStatus.Failure)
                    .Do(_ => NodeStatus.Failure)
                .End()
                .Build();

            Assert.AreEqual(NodeStatus.Failure, tree.Tick(0.1f));
        }

        // ── AsyncAction ──

        [Test]
        public void AsyncAction_SynchronousCompletion_ReturnsResult()
        {
            var tree = BehaviorTreeBuilder.Create("t")
                .DoAsync((_, __) => UniTask.FromResult(NodeStatus.Success))
                .Build();

            // 任务同步完成：同帧即可读到结果
            Assert.AreEqual(NodeStatus.Success, tree.Tick(0.1f));
        }

        [Test]
        public void CaptureSnapshot_IncludesNodes()
        {
            var tree = BehaviorTreeBuilder.Create("boss")
                .Sequence()
                    .Do(_ => NodeStatus.Success)
                    .Do(_ => NodeStatus.Success)
                .End()
                .Build();

            tree.Tick(0.1f);
            var snap = tree.CaptureSnapshot();

            Assert.AreEqual("boss", snap.TreeName);
            // 根 Sequence + 2 个叶节点 = 3
            Assert.AreEqual(3, snap.Nodes.Count);
        }
    }
}
