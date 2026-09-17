using System.Globalization;

namespace TimerManager.Core;

public enum TimingInput { Duration, Finish }
public sealed record TimingValues(TimeSpan Duration, DateTimeOffset FinishUtc);

public static class TimerTiming
{
    public static TimeSpan WholeSeconds(TimeSpan value) => TimeSpan.FromTicks(
        checked((long)Math.Ceiling((decimal)value.Ticks / TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond));

    public static DateTimeOffset Finish(DateTimeOffset now, TimeSpan remaining)
    {
        try { return now.Add(remaining); }
        catch (ArgumentOutOfRangeException) { throw new ArgumentException("The duration is too large for a timer finish date."); }
    }

    public static TimeSpan Elapsed(TimerItem timer, DateTimeOffset now, bool preserve)
    {
        var remaining = timer.Status != TimerStatus.Paused && !preserve && timer.DeadlineUtc is { } deadline
            ? deadline - now : timer.Remaining;
        return timer.Duration - remaining;
    }

    public static DateTimeOffset ParseLocalFinish(string date, string time, TimeZoneInfo zone)
    {
        if (!DateTime.TryParseExact($"{date.Trim()} {time.Trim()}", "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new ArgumentException("Enter a finish date as YYYY-MM-DD and time as HH:MM:SS (24-hour).");
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            throw new ArgumentException("That local time does not exist because the clocks move forward. Choose another time.");
        if (zone.IsAmbiguousTime(local))
            throw new ArgumentException("That local time occurs twice because the clocks move back. Choose an unambiguous time.");
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}

// A draft uses the clock for previews only; the engine applies edits against current state on save.
public sealed class TimerTimingDraft
{
    private readonly ITimerClock clock;
    private readonly TimerItem? timer;
    private readonly bool preserve;
    private readonly TimeSpan openedAwake;
    private TimeSpan duration;
    private DateTimeOffset finish;
    public TimingInput? Input { get; private set; }

    public TimerTimingDraft(ITimerClock clock, TimerItem? timer, bool preserve)
    {
        this.clock = clock;
        this.timer = timer;
        this.preserve = preserve;
        openedAwake = clock.AwakeTime;
        duration = timer?.Duration ?? TimeSpan.FromMinutes(5);
        var now = clock.UtcNow;
        finish = TimerTiming.Finish(now, duration - Elapsed(now));
        Input = timer is null ? null : TimingInput.Duration;
    }

    private TimeSpan Elapsed(DateTimeOffset now)
    {
        if (timer is null) return TimeSpan.Zero;
        var elapsed = TimerTiming.Elapsed(timer, now, preserve);
        if (timer.Status == TimerStatus.Running && preserve)
        {
            elapsed += clock.AwakeTime >= openedAwake ? clock.AwakeTime - openedAwake : TimeSpan.Zero;
            // The original timer stops consuming awake time when it finishes, even while its editor stays open.
            if (elapsed > timer.Duration) elapsed = timer.Duration;
        }
        return elapsed;
    }

    public TimingValues Values
    {
        get
        {
            var now = clock.UtcNow;
            return Input == TimingInput.Finish
                ? new(TimerTiming.WholeSeconds(Elapsed(now) + (finish - now)), finish)
                : new(duration, TimerTiming.Finish(now, duration - Elapsed(now)));
        }
    }

    public void Select(TimingInput input)
    {
        var values = Values;
        duration = values.Duration;
        finish = values.FinishUtc;
        Input = input;
    }

    public void SetDuration(TimeSpan value)
    {
        duration = TimerInput.Duration(value);
        Input = TimingInput.Duration;
    }

    public void SetFinish(DateTimeOffset value)
    {
        finish = value;
        Input = TimingInput.Finish;
    }
}
