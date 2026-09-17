using System.Text.Json;
using TimerManager.Core;
using Xunit;

namespace TimerManager.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TimerManager.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void JsonRoundTripRetainsTimersAndSettings()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new() { PreserveRemaining = true, StartWithWindows = true });
        engine.Create("Tea", TimeSpan.FromMinutes(2), ["Kitchen"]);
        clock.Advance(4.5);
        engine.Advance();
        var store = new JsonStateStore(directory);
        store.Save(engine.Snapshot);
        var loaded = store.Load().State;
        Assert.True(loaded.PreserveRemaining);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(TimeSpan.FromSeconds(115.5), loaded.Timers[0].Remaining);
        Assert.Equal("Tea", loaded.Timers[0].Name);
        Assert.Equal(["Kitchen"], loaded.Timers[0].Tags);
    }

    [Fact]
    public void EditedFinishAndCompletedAnchorSurviveRecovery()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var start = clock.UtcNow;
        var id = engine.Create("A", TimeSpan.FromMinutes(10), []);
        clock.Advance(120);
        engine.Edit(id, "A", [], null, start.AddMinutes(5));
        var store = new JsonStateStore(directory);
        store.Save(engine.Snapshot);
        clock.Advance(240);
        engine = new TimerEngine(clock, store.Load().State);
        engine.Advance();
        Assert.Equal(TimerStatus.Finished, engine.Snapshot.Timers[0].Status);
        store.Save(engine.Snapshot);
        engine = new TimerEngine(clock, store.Load().State);
        engine.Edit(id, "A", [], TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(4), engine.Snapshot.Timers[0].Remaining);
        Assert.Equal(start.AddMinutes(10), engine.Snapshot.Timers[0].DeadlineUtc);
    }

    [Fact]
    public void LegacyCompletedTimersWithoutAFinishStillLoadAndCanBeExtended()
    {
        var clock = new FakeClock();
        var engine = new TimerEngine(clock, new());
        var id = engine.Create("A", TimeSpan.FromMinutes(1), []);
        clock.Advance(60);
        engine.Advance();
        var legacy = engine.Snapshot with { Timers = [engine.Snapshot.Timers[0] with { DeadlineUtc = null }] };
        var store = new JsonStateStore(directory);
        store.Save(legacy);
        engine = new TimerEngine(clock, store.Load().State);
        engine.Edit(id, "A", [], TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(1), engine.Snapshot.Timers[0].Remaining);
    }

    [Fact]
    public void CorruptPrimaryRecoversBackupAndRetainsDamagedFile()
    {
        var store = new JsonStateStore(directory);
        store.Save(new AppState());
        store.Save(new AppState { PreserveRemaining = true });
        File.WriteAllText(Path.Combine(directory, "state.json"), "broken");
        var recovered = store.Load();
        Assert.NotNull(recovered.Warning);
        Assert.False(recovered.State.PreserveRemaining);
        store.Save(recovered.State);
        Assert.Single(Directory.GetFiles(directory, "*.corrupt-*"));
        Assert.False(store.Load().State.PreserveRemaining);
    }

    [Fact]
    public void UnrecoverableAndFutureStateAreNeverSilentlyReset()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state.json");
        File.WriteAllText(path, "broken");
        Assert.Throws<IOException>(() => new JsonStateStore(directory).Load());
        Assert.Equal("broken", File.ReadAllText(path));
        File.WriteAllText(path, "{\"Version\":99}");
        Assert.Throws<UnsupportedStateVersionException>(() => new JsonStateStore(directory).Load());
    }

    [Fact]
    public void InvalidStateIsRejectedBeforeReplacingGoodData()
    {
        var store = new JsonStateStore(directory);
        store.Save(new AppState());
        Assert.Throws<JsonException>(() => store.Save(new AppState { Timers = [new TimerItem
        { Name = "Broken", Duration = TimeSpan.FromSeconds(10), Remaining = TimeSpan.FromSeconds(10), Status = TimerStatus.Running }] }));
        Assert.Empty(store.Load().State.Timers);
    }

    [Fact]
    public void MissingFieldsDoNotInventAnEmptyState()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "state.json"), "{}");
        Assert.Throws<IOException>(() => new JsonStateStore(directory).Load());
    }

    [Fact]
    public void BlockedStorageReportsFailure()
    {
        Directory.CreateDirectory(directory);
        var blocked = Path.Combine(directory, "file");
        File.WriteAllText(blocked, "retain me");
        Assert.Throws<IOException>(() => new JsonStateStore(blocked).Save(new AppState()));
        Assert.Equal("retain me", File.ReadAllText(blocked));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

public class CoordinatorTests
{
    private sealed class Store : IStateStore
    {
        public AppState State { get; private set; } = new();
        public bool Fail { get; set; }
        public int Writes { get; private set; }
        public void Save(AppState state)
        {
            if (Fail) throw new IOException("Disk unavailable");
            State = state; Writes++;
        }
    }

    [Fact]
    public void ClaimsAreDurableBeforeDeliveryAndNotRepeatedAfterRestart()
    {
        var clock = new FakeClock();
        var store = new Store();
        var alerts = 0;
        var coordinator = new TimerCoordinator(clock, store, new(), timers =>
        {
            Assert.All(store.State.Timers, timer => Assert.True(timer.AlertClaimed));
            alerts += timers.Count;
        });
        coordinator.Execute(engine => engine.Create("A", TimeSpan.FromSeconds(1), []));
        clock.Advance(2);
        coordinator.Poll();
        coordinator.Poll();
        new TimerCoordinator(clock, store, store.State, _ => alerts++).Poll();
        Assert.Equal(1, alerts);
    }

    [Fact]
    public void FailedSaveWithholdsAlertsAndRetriesAfterRecovery()
    {
        var clock = new FakeClock();
        var store = new Store();
        var alerts = 0;
        var coordinator = new TimerCoordinator(clock, store, new(), _ => alerts++);
        coordinator.Execute(engine => engine.Create("A", TimeSpan.FromSeconds(1), []));
        clock.Advance(2);
        store.Fail = true;
        Assert.Throws<IOException>(coordinator.Poll);
        Assert.Equal(0, alerts);
        Assert.False(coordinator.Engine.Snapshot.Timers[0].AlertClaimed);
        coordinator.Poll();
        store.Fail = false;
        clock.Advance(5);
        coordinator.Poll();
        Assert.Equal(1, alerts);
    }

    [Fact]
    public void FailedCommandRollsBackItsChanges()
    {
        var store = new Store { Fail = true };
        var coordinator = new TimerCoordinator(new FakeClock(), store, new(), _ => { });
        Assert.Throws<IOException>(() => coordinator.Execute(engine => engine.Create("A", TimeSpan.FromSeconds(10), [])));
        Assert.Empty(coordinator.Engine.Snapshot.Timers);
    }

    [Fact]
    public void PreserveModeCheckpointsEveryFiveSecondsAndRecoversAfterCrash()
    {
        var clock = new FakeClock();
        var store = new Store();
        var coordinator = new TimerCoordinator(clock, store, new() { PreserveRemaining = true }, _ => { });
        coordinator.Execute(engine => engine.Create("A", TimeSpan.FromSeconds(30), []));
        clock.Advance(4);
        coordinator.Poll();
        Assert.Equal(1, store.Writes);
        clock.Advance(1);
        coordinator.Poll();
        Assert.Equal(2, store.Writes);
        clock.Advance(100);
        var restored = new TimerCoordinator(clock, store, store.State, _ => { });
        restored.Poll();
        Assert.Equal(TimeSpan.FromSeconds(25), restored.Engine.Snapshot.Timers[0].Remaining);
    }

    [Fact]
    public void NotificationFailureDoesNotUndoClaims()
    {
        var clock = new FakeClock();
        var store = new Store();
        var coordinator = new TimerCoordinator(clock, store, new(), _ => throw new InvalidOperationException("Unavailable"));
        coordinator.Execute(engine => engine.Create("A", TimeSpan.FromSeconds(1), []));
        clock.Advance(2);
        Assert.Throws<InvalidOperationException>(coordinator.Poll);
        Assert.True(store.State.Timers[0].AlertClaimed);
        coordinator.Poll();
    }
}
