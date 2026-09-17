using TimerManager.Core;
using Xunit;

namespace TimerManager.Tests;

public class TimerTimingTests
{
    [Fact]
    public void CreationRequiresAChoiceAndDurationStartsAtSave()
    {
        var clock = new FakeClock();
        var draft = new TimerTimingDraft(clock, null, false);
        Assert.Null(draft.Input);
        draft.Select(TimingInput.Duration);
        draft.SetDuration(TimeSpan.FromMinutes(10));
        clock.Advance(90);
        Assert.Equal(clock.UtcNow.AddMinutes(10), draft.Values.FinishUtc);
        var engine = new TimerEngine(clock, new());
        engine.Create("A", draft.Values.Duration, []);
        Assert.Equal(TimeSpan.FromMinutes(10), engine.Snapshot.Timers[0].Remaining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatingByFinishKeepsChosenTimeWhileDialogIsOpen(bool preserve)
    {
        var clock = new FakeClock();
        var draft = new TimerTimingDraft(clock, null, preserve);
        var finish = clock.UtcNow.AddDays(1).AddSeconds(30);
        draft.Select(TimingInput.Finish);
        draft.SetFinish(finish);
        clock.Advance(10.25);
        Assert.Equal(finish, draft.Values.FinishUtc);
        Assert.Equal(TimeSpan.FromDays(1).Add(TimeSpan.FromSeconds(20)), draft.Values.Duration);
        var engine = new TimerEngine(clock, new() { PreserveRemaining = preserve });
        engine.CreateUntil("A", finish, []);
        var timer = engine.Snapshot.Timers[0];
        Assert.Equal(finish - clock.UtcNow, timer.Remaining);
        Assert.Equal(draft.Values.Duration, timer.Duration);
        Assert.Equal(preserve ? null : finish, timer.DeadlineUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunningDurationAndFinishEditsKeepTimeAlreadyCounted(bool preserve)
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = preserve });
        var start = clock.UtcNow;
        var id = engine.Create("A", TimeSpan.FromHours(1), []);
        clock.Advance(600);
        engine.Edit(id, "B", ["Work"], TimeSpan.FromHours(2));
        var timer = engine.Snapshot.Timers[0];
        Assert.Equal(TimerStatus.Running, timer.Status);
        Assert.Equal(TimeSpan.FromMinutes(110), timer.Remaining);
        Assert.Equal(start, timer.CreatedUtc);
        clock.Advance(600);
        engine.Edit(id, "B", [], null, start.AddMinutes(90));
        timer = engine.Snapshot.Timers[0];
        Assert.Equal(TimeSpan.FromMinutes(90), timer.Duration);
        Assert.Equal(TimeSpan.FromMinutes(70), timer.Remaining);
        Assert.Equal(preserve ? null : start.AddMinutes(90), timer.DeadlineUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PausedEditsKeepPausedAndExcludePausedTime(bool preserve)
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = preserve });
        var id = engine.Create("A", TimeSpan.FromMinutes(10), []);
        clock.Advance(120);
        engine.PauseOrResume(id);
        clock.Advance(3600);
        engine.Edit(id, "A", [], TimeSpan.FromMinutes(20));
        Assert.Equal(TimerStatus.Paused, engine.Snapshot.Timers[0].Status);
        Assert.Equal(TimeSpan.FromMinutes(18), engine.Snapshot.Timers[0].Remaining);
        engine.Edit(id, "A", [], null, clock.UtcNow.AddMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(7), engine.Snapshot.Timers[0].Duration);
        Assert.Equal(TimeSpan.FromMinutes(5), engine.Snapshot.Timers[0].Remaining);
        Assert.Null(engine.Snapshot.Timers[0].DeadlineUtc);
        clock.Advance(60);
        engine.PauseOrResume(id);
        Assert.Equal(TimeSpan.FromMinutes(5), engine.Snapshot.Timers[0].Remaining);
    }

    [Fact]
    public void PreserveModeDurationEditsExcludeSleepAndExit()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = true });
        var id = engine.Create("A", TimeSpan.FromMinutes(10), []);
        clock.Advance(120);
        engine.Advance();
        clock.Advance(3600, awake: false);
        var draft = new TimerTimingDraft(clock, engine.Snapshot.Timers[0], true);
        draft.SetDuration(TimeSpan.FromMinutes(20));
        clock.Advance(3600, awake: false);
        Assert.Equal(clock.UtcNow.AddMinutes(18), draft.Values.FinishUtc);
        engine = new TimerEngine(clock, engine.Snapshot);
        engine.Edit(id, "A", [], draft.Values.Duration);
        Assert.Equal(TimeSpan.FromMinutes(18), engine.Snapshot.Timers[0].Remaining);
    }

    [Fact]
    public void EditingPreviewKeepsTheRunAnchorAcrossDelayAndSwitchingFields()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        engine.Create("A", TimeSpan.FromHours(1), []);
        var start = clock.UtcNow;
        clock.Advance(600);
        engine.Advance();
        var draft = new TimerTimingDraft(clock, engine.Snapshot.Timers[0], false);
        draft.SetDuration(TimeSpan.FromHours(2));
        clock.Advance(60);
        Assert.Equal(start.AddHours(2), draft.Values.FinishUtc);
        draft.SetFinish(start.AddMinutes(90));
        clock.Advance(60);
        Assert.Equal(TimeSpan.FromMinutes(90), draft.Values.Duration);
        draft.SetDuration(TimeSpan.FromMinutes(30));
        Assert.Equal(start.AddMinutes(30), draft.Values.FinishUtc);
    }

    [Fact]
    public void PausedPreviewMovesWithNowButDoesNotConsumeTime()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var id = engine.Create("A", TimeSpan.FromMinutes(10), []);
        clock.Advance(120);
        engine.PauseOrResume(id);
        var draft = new TimerTimingDraft(clock, engine.Snapshot.Timers[0], false);
        clock.Advance(600);
        Assert.Equal(clock.UtcNow.AddMinutes(8), draft.Values.FinishUtc);
        draft.SetFinish(clock.UtcNow.AddMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(7), draft.Values.Duration);
        clock.Advance(60);
        Assert.Equal(TimeSpan.FromMinutes(6), draft.Values.Duration);
    }

    [Fact]
    public void ShorteningCompletesOnceAndExtendingACompletedTimerRearmsAlerts()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var start = clock.UtcNow;
        var id = engine.Create("A", TimeSpan.FromMinutes(10), []);
        clock.Advance(120);
        engine.Edit(id, "A", [], TimeSpan.FromMinutes(1));
        Assert.Equal(TimerStatus.Finished, engine.Snapshot.Timers[0].Status);
        Assert.Equal(start.AddMinutes(1), engine.Snapshot.Timers[0].DeadlineUtc);
        Assert.Single(engine.ClaimAlerts());
        Assert.Empty(engine.ClaimAlerts());
        clock.Advance(60);
        engine.Edit(id, "A", [], TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(2), engine.Snapshot.Timers[0].Remaining);
        Assert.False(engine.Snapshot.Timers[0].AlertClaimed);
        clock.Advance(120);
        engine.Advance();
        Assert.Single(engine.ClaimAlerts());
        Assert.Empty(engine.ClaimAlerts());
    }

    [Fact]
    public void PreservePreviewMatchesSaveWhenOriginalTimerExpiresInTheEditor()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = true });
        var id = engine.Create("A", TimeSpan.FromSeconds(10), []);
        var draft = new TimerTimingDraft(clock, engine.Snapshot.Timers[0], true);
        draft.SetDuration(TimeSpan.FromSeconds(30));
        clock.Advance(20);
        engine.Advance();
        engine.Edit(id, "A", [], draft.Values.Duration);
        Assert.Equal(clock.UtcNow + engine.Snapshot.Timers[0].Remaining, draft.Values.FinishUtc);
        Assert.Equal(TimeSpan.FromSeconds(20), engine.Snapshot.Timers[0].Remaining);
    }

    [Fact]
    public void ADialogOpenDuringExpiryDoesNotRestartOnNameOnlySave()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var id = engine.Create("A", TimeSpan.FromSeconds(1), []);
        clock.Advance(2);
        engine.Edit(id, "B", [], null);
        Assert.Equal(TimerStatus.Finished, engine.Snapshot.Timers[0].Status);
        Assert.Equal("B", engine.Snapshot.Timers[0].Name);
    }

    [Fact]
    public void InvalidTimingChangesDoNotReplaceTimerData()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var id = engine.Create("A", TimeSpan.FromMinutes(10), []);
        Assert.Throws<ArgumentException>(() => engine.CreateUntil("B", clock.UtcNow, []));
        Assert.Throws<ArgumentException>(() => engine.Edit(id, "B", [], null, clock.UtcNow.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(() => engine.Edit(id, "B", [], TimeSpan.FromMinutes(1), clock.UtcNow.AddMinutes(1)));
        clock.Advance(120);
        engine.PauseOrResume(id);
        Assert.Throws<ArgumentException>(() => engine.Edit(id, "B", [], TimeSpan.FromMinutes(1)));
        Assert.Equal("A", engine.Snapshot.Timers[0].Name);
        Assert.Equal(TimeSpan.FromMinutes(10), engine.Snapshot.Timers[0].Duration);
        Assert.Equal(TimerStatus.Paused, engine.Snapshot.Timers[0].Status);
    }

    [Fact]
    public void LocalFinishHandlesDatesOffsetsAndRejectsDstAmbiguity()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 12, 30, 15, TimeSpan.Zero),
            TimerTiming.ParseLocalFinish("2026-09-18", "13:30:15", zone));
        Assert.Throws<ArgumentException>(() => TimerTiming.ParseLocalFinish("2026-02-30", "13:30:00", zone));
        Assert.Throws<ArgumentException>(() => TimerTiming.ParseLocalFinish("2026-09-18", "24:00:00", zone));
        Assert.Throws<ArgumentException>(() => TimerTiming.ParseLocalFinish("2026-03-29", "01:30:00", zone));
        Assert.Throws<ArgumentException>(() => TimerTiming.ParseLocalFinish("2026-10-25", "01:30:00", zone));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartUsesEditedTotalDuration(bool preserve)
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = preserve });
        var id = engine.Create("A", TimeSpan.FromMinutes(10), []);
        clock.Advance(120);
        engine.Edit(id, "A", [], null, clock.UtcNow.AddMinutes(5));
        engine.Restart(id);
        Assert.Equal(TimeSpan.FromMinutes(7), engine.Snapshot.Timers[0].Remaining);
        var draft = new TimerTimingDraft(clock, engine.Snapshot.Timers[0], preserve);
        Assert.Equal(clock.UtcNow.AddMinutes(7), draft.Values.FinishUtc);
    }
}
