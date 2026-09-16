namespace TimerManager.Core;

public sealed class TimerEngine
{
    private readonly ITimerClock clock;
    private AppState state;
    private TimeSpan lastAwake;

    public TimerEngine(ITimerClock clock, AppState state)
    {
        this.clock = clock;
        this.state = state;
        lastAwake = clock.AwakeTime;
    }

    public AppState Snapshot => state with { Timers = state.Timers.ToArray() };

    public void Advance()
    {
        var awake = clock.AwakeTime;
        var elapsed = awake >= lastAwake ? awake - lastAwake : TimeSpan.Zero;
        lastAwake = awake;
        var now = clock.UtcNow;
        state = state with { Timers = state.Timers.Select(timer =>
        {
            if (timer.Status != TimerStatus.Running) return timer;
            var remaining = state.PreserveRemaining ? timer.Remaining - elapsed : timer.DeadlineUtc!.Value - now;
            return remaining <= TimeSpan.Zero
                ? timer with { Remaining = TimeSpan.Zero, DeadlineUtc = null, Status = TimerStatus.Finished }
                : timer with { Remaining = remaining };
        }).ToArray() };
    }

    public Guid Create(string name, TimeSpan duration, IEnumerable<string> tags)
    {
        Advance();
        duration = TimerInput.Duration(duration);
        var timer = new TimerItem
        {
            Name = TimerInput.Name(name), Tags = TimerInput.Tags(tags), Duration = duration,
            Remaining = duration, CreatedUtc = clock.UtcNow, Status = TimerStatus.Running,
            DeadlineUtc = state.PreserveRemaining ? null : Deadline(duration)
        };
        state = state with { Timers = [.. state.Timers, timer] };
        return timer.Id;
    }

    public void Edit(Guid id, string name, IEnumerable<string> tags, TimeSpan? duration)
    {
        Advance();
        var timer = Get(id);
        if (duration is not null && timer.Status != TimerStatus.Paused)
            throw new InvalidOperationException("Pause this timer before changing its duration.");
        Replace(timer with
        {
            Name = TimerInput.Name(name), Tags = TimerInput.Tags(tags),
            Duration = duration is null ? timer.Duration : TimerInput.Duration(duration.Value),
            Remaining = duration ?? timer.Remaining
        });
    }

    public void PauseOrResume(Guid id)
    {
        Advance();
        var timer = Get(id);
        if (timer.Status == TimerStatus.Finished) return;
        var pause = timer.Status == TimerStatus.Running;
        Replace(timer with
        {
            Status = pause ? TimerStatus.Paused : TimerStatus.Running,
            DeadlineUtc = pause || state.PreserveRemaining ? null : Deadline(timer.Remaining)
        });
    }

    public void Restart(Guid id)
    {
        Advance();
        var timer = Get(id);
        Replace(timer with
        {
            Remaining = timer.Duration, Status = TimerStatus.Running, Acknowledged = false,
            AlertClaimed = false, DeadlineUtc = state.PreserveRemaining ? null : Deadline(timer.Duration)
        });
    }

    public void Delete(Guid id) => state = state with { Timers = state.Timers.Where(timer => timer.Id != id).ToArray() };

    public void Dismiss(Guid id)
    {
        var timer = Get(id);
        if (timer.Status == TimerStatus.Finished) Replace(timer with { Acknowledged = true, AlertClaimed = true });
    }

    public void SetPreserveRemaining(bool enabled)
    {
        Advance();
        if (state.PreserveRemaining == enabled) return;
        var timers = state.Timers.Select(timer => timer.Status == TimerStatus.Running
            ? timer with { DeadlineUtc = enabled ? null : Deadline(timer.Remaining) } : timer).ToArray();
        state = state with { PreserveRemaining = enabled, Timers = timers };
    }

    public void SetStartWithWindows(bool enabled) => state = state with { StartWithWindows = enabled };

    public TimerItem[] ClaimAlerts()
    {
        var pending = state.Timers.Where(timer => timer.Status == TimerStatus.Finished && !timer.AlertClaimed).ToArray();
        foreach (var timer in pending) Replace(timer with { AlertClaimed = true });
        return pending;
    }

    private DateTimeOffset Deadline(TimeSpan duration)
    {
        try { return clock.UtcNow.Add(duration); }
        catch (ArgumentOutOfRangeException) { throw new ArgumentException("The duration is too large for a timer finish date."); }
    }

    private TimerItem Get(Guid id) => state.Timers.FirstOrDefault(timer => timer.Id == id)
        ?? throw new InvalidOperationException("This timer no longer exists.");

    private void Replace(TimerItem item) => state = state with
    {
        Timers = state.Timers.Select(timer => timer.Id == item.Id ? item : timer).ToArray()
    };
}
