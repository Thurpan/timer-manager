using System.Text.Json.Serialization;

namespace TimerManager.Core;

public enum TimerStatus { Running, Paused, Finished }
public enum TimerSort { Remaining, Newest }

public sealed record TimerItem
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    [JsonRequired] public string[] Tags { get; init; } = [];
    [JsonRequired] public TimeSpan Duration { get; init; }
    [JsonRequired] public TimeSpan Remaining { get; init; }
    [JsonRequired] public DateTimeOffset CreatedUtc { get; init; }
    [JsonRequired] public DateTimeOffset? DeadlineUtc { get; init; }
    [JsonRequired] public TimerStatus Status { get; init; }
    [JsonRequired] public bool Acknowledged { get; init; }
    [JsonRequired] public bool AlertClaimed { get; init; }
}

public sealed record AppState
{
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public bool PreserveRemaining { get; init; }
    [JsonRequired] public bool StartWithWindows { get; init; }
    [JsonRequired] public TimerItem[] Timers { get; init; } = [];
}

public interface ITimerClock
{
    DateTimeOffset UtcNow { get; }
    TimeSpan AwakeTime { get; }
}

public static class TimerInput
{
    public static string Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Enter a timer name of 1 to 120 characters.");
        return name.Trim();
    }

    public static TimeSpan Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1) || duration.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new ArgumentException("Enter a positive duration in whole seconds.");
        return duration;
    }

    public static string[] Tags(IEnumerable<string> tags) => tags
        .Select(tag => tag.Trim()).Where(tag => tag.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

public static class TimerQueries
{
    public static IEnumerable<TimerItem> Select(IEnumerable<TimerItem> timers, TimerSort sort, IEnumerable<string> tags)
    {
        var selected = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        var filtered = timers.Where(timer => selected.Count == 0 || timer.Tags.Any(selected.Contains));
        return sort == TimerSort.Newest
            ? filtered.OrderByDescending(timer => timer.CreatedUtc).ThenBy(timer => timer.Id)
            : filtered.OrderBy(timer => timer.Remaining).ThenBy(timer => timer.CreatedUtc).ThenBy(timer => timer.Id);
    }
}
