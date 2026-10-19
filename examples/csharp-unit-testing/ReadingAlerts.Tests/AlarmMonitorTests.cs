using ReadingAlerts;
using Xunit;

namespace ReadingAlerts.Tests;

public sealed class AlarmMonitorTests
{
    [Theory]
    [InlineData(69.9, 0)]
    [InlineData(70.0, 1)]
    [InlineData(70.1, 1)]
    public void Observe_AtBoundary_SendsExpectedCount(double value, int expectedCount)
    {
        // Arrange
        var sink = new RecordingSink();
        var monitor = new AlarmMonitor(70, sink);

        // Act
        monitor.Observe(new Reading("pump-A", value));

        // Assert
        Assert.Equal(expectedCount, sink.Alerts.Count);
    }

    [Fact]
    public void Observe_AtLimit_SendsReadingDetails()
    {
        var sink = new RecordingSink();
        var monitor = new AlarmMonitor(70, sink);

        monitor.Observe(new Reading("pump-A", 70));

        Assert.Equal(new Alert("pump-A", 70), Assert.Single(sink.Alerts));
    }

    [Fact]
    public void Observe_NonFiniteValue_ThrowsWithoutSending()
    {
        var sink = new RecordingSink();
        var monitor = new AlarmMonitor(70, sink);

        Assert.Throws<ArgumentException>(() => monitor.Observe(new Reading("pump-A", double.NaN)));
        Assert.Empty(sink.Alerts);
    }

    [Fact]
    public void Observe_SinkFailure_IsNotHidden()
    {
        var failure = new InvalidOperationException("sink unavailable");
        var monitor = new AlarmMonitor(70, new FailingSink(failure));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => monitor.Observe(new Reading("pump-A", 70)));
        Assert.Same(failure, thrown);
    }

    private sealed class RecordingSink : IAlertSink
    {
        public List<Alert> Alerts { get; } = [];
        public void Send(Alert alert) => Alerts.Add(alert);
    }

    private sealed class FailingSink(Exception failure) : IAlertSink
    {
        public void Send(Alert alert) => throw failure;
    }
}
