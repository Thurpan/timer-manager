using TimerManager.Core;
using Xunit;

namespace TimerManager.Tests;

public sealed class FakeClock : ITimerClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    public TimeSpan AwakeTime { get; private set; }
    public void Advance(double seconds, bool awake = true)
    {
        UtcNow += TimeSpan.FromSeconds(seconds);
        if (awake) AwakeTime += TimeSpan.FromSeconds(seconds);
    }
    public void JumpClock(double seconds) => UtcNow += TimeSpan.FromSeconds(seconds);
}

public class TimerEngineTests
{
    [Fact]
    public void ConcurrentTimersExpireIndependentlyAndOnlyClaimOnce()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        engine.Create("Tea", TimeSpan.FromSeconds(10), ["Kitchen"]);
        engine.Create("Laundry", TimeSpan.FromSeconds(30), []);
        clock.Advance(11);
        engine.Advance();
        Assert.Equal(TimerStatus.Finished, engine.Snapshot.Timers[0].Status);
        Assert.Equal(TimeSpan.FromSeconds(19), engine.Snapshot.Timers[1].Remaining);
        Assert.Single(engine.ClaimAlerts());
        Assert.Empty(engine.ClaimAlerts());
        engine.Advance();
        Assert.Empty(engine.ClaimAlerts());
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 20)]
    public void ReopeningPreservesTheChosenMode(bool preserve, int remaining)
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = preserve });
        engine.Create("Test", TimeSpan.FromSeconds(30), []);
        clock.Advance(10);
        engine.Advance();
        var saved = engine.Snapshot;
        clock.Advance(10);
        var restored = new TimerEngine(clock, saved);
        restored.Advance();
        Assert.Equal(TimeSpan.FromSeconds(remaining), restored.Snapshot.Timers[0].Remaining);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 20)]
    public void SleepCountsOnlyInDefaultMode(bool preserve, int remaining)
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = preserve });
        engine.Create("Test", TimeSpan.FromSeconds(30), []);
        clock.Advance(10);
        engine.Advance();
        clock.Advance(3600, awake: false);
        engine.Advance();
        Assert.Equal(TimeSpan.FromSeconds(remaining), engine.Snapshot.Timers[0].Remaining);
    }

    [Fact]
    public void ModeChangesKeepRemainingAndDoNotResumePausedTimers()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var running = engine.Create("Running", TimeSpan.FromSeconds(60), []);
        var paused = engine.Create("Paused", TimeSpan.FromSeconds(60), []);
        engine.PauseOrResume(paused);
        clock.Advance(10);
        engine.SetPreserveRemaining(true);
        Assert.Equal(TimeSpan.FromSeconds(50), engine.Snapshot.Timers.Single(t => t.Id == running).Remaining);
        clock.Advance(20, awake: false);
        engine.SetPreserveRemaining(false);
        Assert.Equal(clock.UtcNow.AddSeconds(50), engine.Snapshot.Timers.Single(t => t.Id == running).DeadlineUtc);
        Assert.Equal(TimerStatus.Paused, engine.Snapshot.Timers.Single(t => t.Id == paused).Status);
    }

    [Fact]
    public void PauseEditResumeDismissRestartAndDelete()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var id = engine.Create("A", TimeSpan.FromSeconds(10), [" Work ", "work", ""]);
        Assert.Equal(["Work"], engine.Snapshot.Timers[0].Tags);
        Assert.Throws<InvalidOperationException>(() => engine.Edit(id, "B", [], TimeSpan.FromSeconds(20)));
        clock.Advance(3);
        engine.PauseOrResume(id);
        clock.Advance(100);
        engine.Edit(id, " B ", ["New"], null);
        Assert.Equal(TimeSpan.FromSeconds(7), engine.Snapshot.Timers[0].Remaining);
        engine.Edit(id, "B", [], TimeSpan.FromSeconds(20));
        Assert.Equal(TimerStatus.Paused, engine.Snapshot.Timers[0].Status);
        engine.PauseOrResume(id);
        clock.Advance(21);
        engine.Advance();
        engine.Dismiss(id);
        Assert.True(engine.Snapshot.Timers[0].Acknowledged);
        engine.Restart(id);
        Assert.Equal(TimeSpan.FromSeconds(20), engine.Snapshot.Timers[0].Remaining);
        Assert.False(engine.Snapshot.Timers[0].AlertClaimed);
        engine.Delete(id);
        Assert.Empty(engine.Snapshot.Timers);
    }

    [Theory]
    [InlineData(false, 60)]
    [InlineData(true, 60)]
    public void BackwardClockChangeAffectsOnlyDeadlines(bool preserve, int original)
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = preserve });
        engine.Create("Clock", TimeSpan.FromSeconds(original), []);
        clock.JumpClock(-30);
        engine.Advance();
        Assert.Equal(TimeSpan.FromSeconds(preserve ? 60 : 90), engine.Snapshot.Timers[0].Remaining);
        clock.JumpClock(120);
        engine.Advance();
        Assert.Equal(preserve ? TimerStatus.Running : TimerStatus.Finished, engine.Snapshot.Timers[0].Status);
    }

    [Fact]
    public void OverdueRecoveryDoesNotResetDurationAndBatchesAlerts()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        engine.Create("One", TimeSpan.FromSeconds(10), []);
        engine.Create("Two", TimeSpan.FromSeconds(20), []);
        clock.Advance(3600);
        var recovered = new TimerEngine(clock, engine.Snapshot);
        recovered.Advance();
        Assert.Equal(2, recovered.ClaimAlerts().Length);
        Assert.All(recovered.Snapshot.Timers, timer => Assert.Equal(TimeSpan.Zero, timer.Remaining));
    }

    [Fact]
    public void FilteringUsesAnyTagWithoutCaseSensitivityAndSortingIsStable()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var first = engine.Create("First", TimeSpan.FromSeconds(40), ["Kitchen"]);
        clock.Advance(1);
        var second = engine.Create("Second", TimeSpan.FromSeconds(10), ["Work"]);
        engine.Create("Third", TimeSpan.FromSeconds(90), ["Other"]);
        var filtered = TimerQueries.Select(engine.Snapshot.Timers, TimerSort.Remaining, ["KITCHEN", "work"]).ToArray();
        Assert.Equal([second, first], filtered.Select(timer => timer.Id));
        Assert.Equal(second, TimerQueries.Select(engine.Snapshot.Timers, TimerSort.Newest, ["Work"]).First().Id);
        var tie = engine.Snapshot.Timers[1] with { Id = Guid.Empty };
        Assert.Equal(Guid.Empty, TimerQueries.Select([engine.Snapshot.Timers[1], tie], TimerSort.Remaining, []).First().Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    public void InvalidDurationsAreRejected(double seconds)
    {
        var engine = new TimerEngine(new FakeClock(), new());
        Assert.Throws<ArgumentException>(() => engine.Create("Bad", TimeSpan.FromSeconds(seconds), []));
        Assert.Empty(engine.Snapshot.Timers);
    }

    [Fact]
    public void InvalidNamesAndOverflowAreRejected()
    {
        var engine = new TimerEngine(new FakeClock(), new());
        Assert.Throws<ArgumentException>(() => engine.Create(" ", TimeSpan.FromSeconds(1), []));
        Assert.Throws<ArgumentException>(() => engine.Create("Too long", TimeSpan.FromDays(4_000_000), []));
    }
}
