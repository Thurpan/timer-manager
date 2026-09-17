using System;
using System.Collections.Generic;
using System.Globalization;
using TimerManager.Core;

namespace TimerManager.App;

public static class TimerDisplay
{
    public static string Duration(TimeSpan value)
    {
        value = value > TimeSpan.Zero ? TimerTiming.WholeSeconds(value) : TimeSpan.Zero;
        var parts = new List<string>();
        if (value.Days > 0) parts.Add($"{value.Days}d");
        if (value.Hours > 0) parts.Add($"{value.Hours}h");
        if (value.Minutes > 0) parts.Add($"{value.Minutes}m");
        if (value.Seconds > 0 || parts.Count == 0) parts.Add($"{value.Seconds}s");
        return string.Join(" ", parts);
    }

    public static string Finish(DateTimeOffset finish, DateTimeOffset now, bool includeSeconds = false)
    {
        var local = finish.ToLocalTime();
        var time = local.ToString(includeSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
        return local.Date == now.ToLocalTime().Date ? $"{time} today" : $"{local:ddd d MMM} {time}";
    }
}
