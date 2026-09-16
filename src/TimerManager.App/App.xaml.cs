using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using TimerManager.Core;
using Forms = System.Windows.Forms;

namespace TimerManager.App;

public partial class App : Application
{
    private SingleInstance? instance;
    private NotificationService? notifications;
    private Forms.NotifyIcon? tray;
    private MainWindow? window;
    private TimerCoordinator? coordinator;
    private StreamWriter? log;
    private bool exiting;
    internal bool IsExiting => exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new SingleInstance();
        if (!instance.IsOwner)
        {
            try { await instance.SignalAsync(); }
            catch (Exception ex) { MessageBox.Show($"Timer Manager is already running. Open it from the system tray.\n\n{ex.Message}", "Timer Manager"); }
            Shutdown();
            return;
        }
        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimerManager");
            var overrideIndex = Array.IndexOf(e.Args, "--data-directory");
            if (overrideIndex >= 0)
            {
                if (overrideIndex + 1 >= e.Args.Length) throw new ArgumentException("--data-directory requires a path.");
                dataDirectory = Path.GetFullPath(e.Args[overrideIndex + 1]);
            }
            var store = new JsonStateStore(dataDirectory);
            var loaded = store.Load();
            Directory.CreateDirectory(dataDirectory);
            var logPath = Path.Combine(dataDirectory, "app.log");
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 1_000_000) File.Move(logPath, logPath + ".old", overwrite: true);
            log = new StreamWriter(logPath, append: true) { AutoFlush = true };
            notifications = new NotificationService(ShowWindow, Report);
            coordinator = new TimerCoordinator(new WindowsClock(), store, loaded.State, timers => notifications.Show(timers));
            window = new MainWindow(coordinator, dataDirectory, overrideIndex < 0);
            MainWindow = window;
            tray = new Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "timer.ico")),
                Text = "Timer Manager", Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip()
            };
            tray.ContextMenuStrip.Items.Add("Open Timer Manager", null, (_, _) => ShowWindow());
            tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(RequestExit));
            tray.DoubleClick += (_, _) => ShowWindow();
            _ = instance.ListenAsync(ShowWindow, Report);
            notifications.Register();
            if (loaded.Warning is not null) Report(loaded.Warning);
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SessionEnding += OnSessionEnding;
            if (!e.Args.Contains("--tray")) window.Show();
            window.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Timer Manager could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal void ShowWindow() => Dispatcher.Invoke(() =>
    {
        if (window is null || exiting) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    });

    internal void Report(string message) => Dispatcher.Invoke(() =>
    {
        try { log?.WriteLine($"{DateTimeOffset.UtcNow:O} {message}"); }
        catch (IOException) { }
        window?.ShowError(message);
    });

    internal void RequestExit()
    {
        try { coordinator?.Checkpoint(); }
        catch (Exception ex)
        {
            ShowWindow();
            Report($"Could not save timers: {ex.Message}");
            if (MessageBox.Show(window, "The latest timer state could not be saved. Exit anyway?", "Unsaved timers", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }
        exiting = true;
        Shutdown();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.Invoke(() =>
    {
        try
        {
            if (e.Mode == PowerModes.Suspend) coordinator?.Checkpoint();
            else if (e.Mode == PowerModes.Resume) window?.RefreshTimers();
        }
        catch (Exception ex) { Report($"Could not save timers during a power change: {ex.Message}"); }
    });

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        try { coordinator?.Checkpoint(); }
        catch (Exception ex) { Report($"Could not save before Windows ended the session: {ex.Message}"); }
        exiting = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        window?.Stop();
        tray?.Dispose();
        notifications?.Dispose();
        instance?.Dispose();
        log?.Dispose();
        base.OnExit(e);
    }
}
