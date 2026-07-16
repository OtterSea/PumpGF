using NUnit.Framework;
using PumpGF;

namespace PumpGF.Tests
{
    public class StateMachineTests
    {
        [Test]
        public void Transition_WhenConditionMet_ChangesState()
        {
            var sm = StateMachineBuilder.Create("Test")
                .State("Idle")
                    .TransitionTo("Run").When(() => true)
                .State("Run")
                .InitialState("Idle")
                .Build();

            sm.Tick(0.016f);

            Assert.AreEqual("Run", sm.CurrentState.Name);
        }

        [Test]
        public void NoTransition_WhenConditionFalse_StaysState()
        {
            var sm = StateMachineBuilder.Create("Test")
                .State("Idle")
                    .TransitionTo("Run").When(() => false)
                .State("Run")
                .InitialState("Idle")
                .Build();

            sm.Tick(0.016f);

            Assert.AreEqual("Idle", sm.CurrentState.Name);
        }
    }
}
