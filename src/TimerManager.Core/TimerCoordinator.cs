namespace TimerManager.Core;

// All calls are serialised by the app's dispatcher; persistence precedes external alerts.
public sealed class TimerCoordinator(ITimerClock clock, IStateStore store, AppState initial, Action<IReadOnlyList<TimerItem>> alert)
{
    public TimerEngine Engine { get; private set; } = new(clock, initial);
    private TimeSpan lastSave = clock.AwakeTime;
    private TimeSpan? failedAt;

    public void Execute(Action<TimerEngine> action)
    {
        Engine.Advance();
        var before = Engine.Snapshot;
        try
        {
            action(Engine);
            Save();
        }
        catch
        {
            Engine = new TimerEngine(clock, before);
            throw;
        }
    }

    public void Poll()
    {
        Engine.Advance();
        if (failedAt is { } failure && clock.AwakeTime - failure < TimeSpan.FromSeconds(5)) return;
        var before = Engine.Snapshot;
        var pending = Engine.ClaimAlerts();
        var checkpoint = before.PreserveRemaining && before.Timers.Any(timer => timer.Status == TimerStatus.Running)
            && clock.AwakeTime - lastSave >= TimeSpan.FromSeconds(5);
        if (pending.Length == 0 && !checkpoint) return;
        try { Save(); }
        catch
        {
            Engine = new TimerEngine(clock, before);
            failedAt = clock.AwakeTime;
            throw;
        }
        // Alert failures cannot roll back a durable claim or duplicate a sound next time.
        if (pending.Length > 0) alert(pending);
    }

    public void Checkpoint()
    {
        Engine.Advance();
        Save();
    }

    private void Save()
    {
        store.Save(Engine.Snapshot);
        lastSave = clock.AwakeTime;
        failedAt = null;
    }
}
