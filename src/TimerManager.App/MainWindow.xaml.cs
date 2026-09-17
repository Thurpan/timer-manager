using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using TimerManager.Core;

namespace TimerManager.App;

public partial class MainWindow : Window
{
    private readonly TimerCoordinator coordinator;
    private readonly StartupService startup = new();
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly HashSet<string> selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private string[] displayedTags = [];
    private bool rendering;
    private bool ready;
    public ObservableCollection<TimerRow> Rows { get; } = [];

    public MainWindow(TimerCoordinator coordinator, string dataDirectory, bool allowStartup)
    {
        this.coordinator = coordinator;
        InitializeComponent();
        DataContext = this;
        DataPathText.Text = dataDirectory;
        StartupToggle.IsEnabled = allowStartup;
        if (!allowStartup) StartupToggle.ToolTip = "Startup registration is disabled when using an isolated data directory.";
        rendering = true;
        PreserveToggle.IsChecked = coordinator.Engine.Snapshot.PreserveRemaining;
        StartupToggle.IsChecked = coordinator.Engine.Snapshot.StartWithWindows;
        rendering = false;
        ready = true;
        if (allowStartup && coordinator.Engine.Snapshot.StartWithWindows && !startup.IsEnabled())
            ShowError("Windows startup registration does not match this app location. Turn Start with Windows off and on to register this copy.");
        refresh.Tick += (_, _) => RefreshTimers();
        SourceInitialized += (_, _) =>
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
        };
        Closing += (_, e) => { if (!((App)Application.Current).IsExiting) { e.Cancel = true; Hide(); } };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public void Start() { RefreshTimers(); refresh.Start(); }
    public void Stop() => refresh.Stop();
    public void ShowError(string message) { ErrorText.Text = message; ErrorPanel.Visibility = Visibility.Visible; }
    public void RefreshTimers()
    {
        if (!ready) return;
        try { coordinator.Poll(); }
        catch (Exception ex) { ((App)Application.Current).Report($"Could not save timer state. Will retry: {ex.Message}"); }
        Render();
    }

    private void Render()
    {
        var state = coordinator.Engine.Snapshot;
        SummaryText.Visibility = state.Timers.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        SummaryText.Text = $"{state.Timers.Count(t => t.Status == TimerStatus.Running)} running · {state.Timers.Count(t => t.Status == TimerStatus.Paused)} paused · {state.Timers.Count(t => t.Status == TimerStatus.Finished)} finished";
        var tags = state.Timers.SelectMany(timer => timer.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!tags.SequenceEqual(displayedTags))
        {
            displayedTags = tags;
            selectedTags.IntersectWith(tags);
            TagFilters.Children.Clear();
            foreach (var tag in tags)
            {
                var check = new CheckBox { Content = tag, IsChecked = selectedTags.Contains(tag), Margin = new Thickness(0, 0, 12, 4) };
                check.Checked += (_, _) => { selectedTags.Add(tag); Render(); };
                check.Unchecked += (_, _) => { selectedTags.Remove(tag); Render(); };
                TagFilters.Children.Add(check);
            }
        }
        var visible = TimerQueries.Select(state.Timers, SortRemaining.IsChecked == true ? TimerSort.Remaining : TimerSort.Newest, selectedTags).ToArray();
        var ids = visible.Select(timer => timer.Id).ToHashSet();
        for (var i = Rows.Count - 1; i >= 0; i--) if (!ids.Contains(Rows[i].Id)) Rows.RemoveAt(i);
        for (var i = 0; i < visible.Length; i++)
        {
            var row = Rows.FirstOrDefault(item => item.Id == visible[i].Id);
            if (row is null) { row = new TimerRow(visible[i]); Rows.Insert(i, row); }
            else { row.Update(visible[i]); if (Rows.IndexOf(row) != i) Rows.Move(Rows.IndexOf(row), i); }
        }
        EmptyPanel.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = state.Timers.Length == 0 ? "No timers" : "No matching timers";
        EmptySubtitle.Visibility = state.Timers.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool Execute(Action<TimerEngine> action, bool returnValidationToEditor = false)
    {
        try { coordinator.Execute(action); RefreshTimers(); return true; }
        catch (ArgumentException) when (returnValidationToEditor) { Render(); throw; }
        catch (Exception ex) { ((App)Application.Current).Report($"The change could not be saved: {ex.Message}"); Render(); return false; }
    }

    private static Guid Id(object sender) => (Guid)((Button)sender).Tag;
    private void Add_Click(object sender, RoutedEventArgs e) => new TimerDialog(null, coordinator.Engine.Snapshot.PreserveRemaining, (name, tags, duration, finish) =>
        Execute(engine =>
        {
            if (finish is { } target) engine.CreateUntil(name, target, tags);
            else engine.Create(name, duration!.Value, tags);
        }, returnValidationToEditor: true)) { Owner = this }.ShowDialog();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        RefreshTimers();
        var timer = coordinator.Engine.Snapshot.Timers.First(item => item.Id == Id(sender));
        new TimerDialog(timer, coordinator.Engine.Snapshot.PreserveRemaining,
            (name, tags, duration, finish) => Execute(engine => engine.Edit(timer.Id, name, tags, duration, finish), returnValidationToEditor: true)) { Owner = this }.ShowDialog();
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => Execute(engine => engine.PauseOrResume(Id(sender)));
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        var timer = coordinator.Engine.Snapshot.Timers.First(item => item.Id == Id(sender));
        if (timer.Status != TimerStatus.Finished && MessageBox.Show(this, $"Restart ‘{timer.Name}’ from its full duration?", "Restart timer", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Execute(engine => engine.Restart(timer.Id));
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Delete this timer?", "Delete timer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Execute(engine => engine.Delete(Id(sender)));
    }
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Execute(engine => engine.Dismiss(Id(sender)));
    private void DismissError_Click(object sender, RoutedEventArgs e) => ErrorPanel.Visibility = Visibility.Collapsed;
    private void Exit_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).RequestExit();
    private void Sort_Changed(object sender, RoutedEventArgs e) { if (ready) Render(); }
    private void Preserve_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || rendering) return;
        Execute(engine => engine.SetPreserveRemaining(PreserveToggle.IsChecked == true));
        rendering = true;
        PreserveToggle.IsChecked = coordinator.Engine.Snapshot.PreserveRemaining;
        rendering = false;
    }
    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || rendering) return;
        var previous = coordinator.Engine.Snapshot.StartWithWindows;
        try
        {
            var enabled = StartupToggle.IsChecked == true;
            startup.SetEnabled(enabled);
            if (!Execute(engine => engine.SetStartWithWindows(enabled))) startup.SetEnabled(previous);
        }
        catch (Exception ex) { ShowError($"Could not update Windows startup: {ex.Message}"); }
        rendering = true;
        StartupToggle.IsChecked = coordinator.Engine.Snapshot.StartWithWindows;
        rendering = false;
    }
}

public sealed class TimerRow(TimerItem timer) : INotifyPropertyChanged
{
    private TimerItem item = timer;
    public Guid Id => item.Id;
    public string Name => item.Name;
    public string Tags => string.Join("  ·  ", item.Tags);
    public bool CanPause => item.Status != TimerStatus.Finished;
    public bool NeedsAttention => item.Status == TimerStatus.Finished && !item.Acknowledged;
    public string PauseLabel => item.Status == TimerStatus.Paused ? "Resume" : "Pause";
    public string AccessibleCountdown => $"{Name}: {Countdown} remaining";
    public string StatusText => item.Status switch
    {
        TimerStatus.Paused => "Paused",
        TimerStatus.Finished => item.Acknowledged ? "Finished · dismissed" : "Finished",
        _ => "Running"
    };
    public string Countdown
    {
        get
        {
            var seconds = (long)Math.Ceiling(item.Remaining.TotalSeconds);
            var duration = TimeSpan.FromSeconds(seconds);
            return duration.Days > 0 ? $"{duration.Days}d {duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(TimerItem value)
    {
        if (item == value) return;
        var oldCountdown = Countdown;
        var onlyTime = item with { Remaining = value.Remaining } == value;
        item = value;
        if (!onlyTime) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        else if (oldCountdown != Countdown)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Countdown)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleCountdown)));
        }
    }
}
