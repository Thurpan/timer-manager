using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimerManager.Core;

public interface IStateStore { void Save(AppState state); }
public sealed record LoadResult(AppState State, string? Warning = null);
public sealed class UnsupportedStateVersionException(int version)
    : IOException($"Saved data uses version {version}. Use a newer Timer Manager; the files have not been changed.");

public sealed class JsonStateStore(string directory) : IStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };
    private readonly string path = Path.Combine(directory, "state.json");
    private bool quarantinePrimary;
    public string DirectoryPath => directory;

    public LoadResult Load()
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return new(new AppState());
        try { return new(Read(path)); }
        catch (UnsupportedStateVersionException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            try
            {
                var backup = Read(path + ".bak");
                quarantinePrimary = File.Exists(path);
                return new(backup, "Recovered the previous saved state from a backup. Some recent changes may be missing. The original file will be retained.");
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                throw new IOException($"Cannot read saved timers at {path}. No data has been overwritten. Primary: {ex.Message} Backup: {backupError.Message}", ex);
            }
        }
    }

    public void Save(AppState state)
    {
        Validate(state);
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, Options);
                stream.Flush(flushToDisk: true);
            }
            if (quarantinePrimary && File.Exists(path))
            {
                File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                quarantinePrimary = false;
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else File.Move(temporary, path);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { /* A leftover temporary file never replaces saved state. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static AppState Read(string file)
    {
        using var stream = File.OpenRead(file);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Saved state must be an object.");
        if (document.RootElement.TryGetProperty("Version", out var version))
        {
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
                throw new JsonException("Invalid state version.");
            if (number != 1) throw new UnsupportedStateVersionException(number);
        }
        var state = document.Deserialize<AppState>(Options) ?? throw new JsonException("Saved state is empty.");
        Validate(state);
        return state;
    }

    private static void Validate(AppState state)
    {
        if (state.Version != 1) throw new UnsupportedStateVersionException(state.Version);
        if (state.Timers is null || state.Timers.Any(timer => timer is null)) throw new JsonException("Invalid timer list.");
        if (state.Timers.Select(timer => timer.Id).Distinct().Count() != state.Timers.Length)
            throw new JsonException("Duplicate timer identifiers.");
        foreach (var timer in state.Timers)
        {
            TimerInput.Name(timer.Name);
            TimerInput.Duration(timer.Duration);
            if (timer.Id == Guid.Empty || timer.Tags is null || timer.Tags.Any(tag => string.IsNullOrWhiteSpace(tag))
                || !Enum.IsDefined(timer.Status) || timer.Remaining < TimeSpan.Zero
                || (timer.Status == TimerStatus.Running && !state.PreserveRemaining && timer.DeadlineUtc is null)
                || (timer.Status == TimerStatus.Finished && timer.Remaining != TimeSpan.Zero)
                || (timer.Status == TimerStatus.Paused && timer.Remaining <= TimeSpan.Zero))
                throw new JsonException("Invalid saved timer state.");
        }
    }
}
