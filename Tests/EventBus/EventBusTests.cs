using NUnit.Framework;
using PumpGF;

namespace PumpGF.Tests
{
    public readonly struct TestEvent
    {
        public readonly int Value;
        public TestEvent(int value) { Value = value; }
    }

    public class EventBusTests
    {
        private EventBus _bus;

        [SetUp]
        public void SetUp() => _bus = new EventBus();

        [TearDown]
        public void TearDown() => _bus.Dispose();

        [Test]
        public void Publish_SubscribedHandler_ReceivesEvent()
        {
            int received = 0;
            _bus.Subscribe<TestEvent>(e => received = e.Value);

            _bus.Publish(new TestEvent(42));

            Assert.AreEqual(42, received);
        }

        [Test]
        public void Subscribe_Dispose_Unsubscribes()
        {
            int count = 0;
            var sub = _bus.Subscribe<TestEvent>(_ => count++);

            _bus.Publish(new TestEvent(1));
            sub.Dispose();
            _bus.Publish(new TestEvent(2));

            Assert.AreEqual(1, count);
        }
    }
}
