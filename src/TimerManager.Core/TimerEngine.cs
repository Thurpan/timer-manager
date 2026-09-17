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
                ? timer with { Remaining = TimeSpan.Zero, DeadlineUtc = timer.DeadlineUtc ?? TimerTiming.Finish(now, remaining), Status = TimerStatus.Finished }
                : timer with { Remaining = remaining };
        }).ToArray() };
    }

    public Guid Create(string name, TimeSpan duration, IEnumerable<string> tags)
    {
        Advance();
        duration = TimerInput.Duration(duration);
        return Add(name, tags, duration, duration, Deadline(duration));
    }

    public Guid CreateUntil(string name, DateTimeOffset finishUtc, IEnumerable<string> tags)
    {
        Advance();
        var remaining = finishUtc - clock.UtcNow;
        if (remaining <= TimeSpan.Zero) throw new ArgumentException("Choose a finish time in the future.");
        return Add(name, tags, TimerInput.Duration(TimerTiming.WholeSeconds(remaining)), remaining, finishUtc);
    }

    private Guid Add(string name, IEnumerable<string> tags, TimeSpan duration, TimeSpan remaining, DateTimeOffset finishUtc)
    {
        var timer = new TimerItem
        {
            Name = TimerInput.Name(name), Tags = TimerInput.Tags(tags), Duration = duration,
            Remaining = remaining, CreatedUtc = clock.UtcNow, Status = TimerStatus.Running,
            DeadlineUtc = state.PreserveRemaining ? null : finishUtc
        };
        state = state with { Timers = [.. state.Timers, timer] };
        return timer.Id;
    }

    public void Edit(Guid id, string name, IEnumerable<string> tags, TimeSpan? duration, DateTimeOffset? finishUtc = null)
    {
        Advance();
        var timer = Get(id);
        var updated = timer with { Name = TimerInput.Name(name), Tags = TimerInput.Tags(tags) };
        if (duration is not null && finishUtc is not null)
            throw new ArgumentException("Change duration or finish time, not both in one operation.");
        if (duration is null && finishUtc is null) { Replace(updated); return; }

        var now = clock.UtcNow;
        var elapsed = TimerTiming.Elapsed(timer, now, state.PreserveRemaining);
        TimeSpan remaining;
        if (finishUtc is { } target)
        {
            remaining = target - now;
            if (remaining <= TimeSpan.Zero) throw new ArgumentException("Choose a finish time in the future.");
            duration = TimerInput.Duration(TimerTiming.WholeSeconds(elapsed + remaining));
        }
        else
        {
            duration = TimerInput.Duration(duration!.Value);
            remaining = duration.Value - elapsed;
        }
        if (timer.Status == TimerStatus.Paused && remaining <= TimeSpan.Zero)
            throw new ArgumentException("For a paused timer, duration must be longer than the time already counted.");
        var finish = TimerTiming.Finish(now, remaining);
        var status = remaining <= TimeSpan.Zero ? TimerStatus.Finished
            : timer.Status == TimerStatus.Paused ? TimerStatus.Paused : TimerStatus.Running;
        var reactivated = timer.Status == TimerStatus.Finished && status == TimerStatus.Running;
        Replace(updated with
        {
            Duration = duration.Value, Remaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
            Status = status,
            DeadlineUtc = status == TimerStatus.Finished || (status == TimerStatus.Running && !state.PreserveRemaining) ? finish : null,
            Acknowledged = reactivated ? false : timer.Acknowledged,
            AlertClaimed = reactivated ? false : timer.AlertClaimed
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
        return TimerTiming.Finish(clock.UtcNow, duration);
    }

    private TimerItem Get(Guid id) => state.Timers.FirstOrDefault(timer => timer.Id == id)
        ?? throw new InvalidOperationException("This timer no longer exists.");

    private void Replace(TimerItem item) => state = state with
    {
        Timers = state.Timers.Select(timer => timer.Id == item.Id ? item : timer).ToArray()
    };
}
