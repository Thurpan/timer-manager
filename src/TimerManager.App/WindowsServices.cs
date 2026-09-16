using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Windows.AppNotifications;
using TimerManager.Core;

namespace TimerManager.App;

internal sealed class WindowsClock : ITimerClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeSpan AwakeTime => QueryUnbiasedInterruptTime(out var ticks)
        ? TimeSpan.FromTicks(checked((long)ticks)) : throw new Win32Exception(Marshal.GetLastWin32Error());

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
}

internal sealed class StartupService
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "TimerManager";
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, writable: true);
        if (enabled) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --tray", RegistryValueKind.String);
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return string.Equals(key?.GetValue(Name) as string, $"\"{Environment.ProcessPath}\" --tray", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class NotificationService(Action showWindow, Action<string> report) : IDisposable
{
    private bool registered;
    public void Register()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            registered = true;
        }
        catch (Exception ex) { report($"Windows notifications are unavailable: {ex.Message}. Finished timers and sound remain available."); }
    }

    public void Show(IReadOnlyList<TimerItem> timers)
    {
        try
        {
            if (registered)
            {
                var title = timers.Count == 1 ? "Timer finished" : $"{timers.Count} timers finished";
                var names = string.Join(", ", timers.Take(3).Select(timer => timer.Name));
                if (timers.Count > 3) names += $" and {timers.Count - 3} more";
                var xml = $"<toast><visual><binding template='ToastGeneric'><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(names)}</text></binding></visual><audio silent='true'/></toast>";
                AppNotificationManager.Default.Show(new AppNotification(xml));
            }
        }
        catch (Exception ex) { report($"The completion notification could not be sent: {ex.Message}"); }
        try { SystemSounds.Exclamation.Play(); }
        catch (Exception ex) { report($"The alert sound could not be played: {ex.Message}"); }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => showWindow();
    public void Dispose()
    {
        if (!registered) return;
        AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
        try { AppNotificationManager.Default.Unregister(); }
        catch (COMException) { }
    }
}

internal sealed class SingleInstance : IDisposable
{
    private readonly string name = "TimerManager-" + WindowsIdentity.GetCurrent().User!.Value;
    private readonly Mutex mutex;
    private readonly CancellationTokenSource cancellation = new();
    public bool IsOwner { get; }

    public SingleInstance()
    {
        mutex = new Mutex(initiallyOwned: false, @"Local\" + name);
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }

    public async Task SignalAsync()
    {
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000);
        await pipe.WriteAsync(new byte[] { 1 });
    }

    public async Task ListenAsync(Action show, Action<string> report)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation.Token);
                var buffer = new byte[1];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                try { if (await pipe.ReadAsync(buffer, timeout.Token) > 0) show(); }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
                catch (IOException) { /* A launcher can exit before its activation message arrives. */ }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { report($"Cannot receive activation from a second launch: {ex.Message}"); }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
        cancellation.Dispose();
    }
}
