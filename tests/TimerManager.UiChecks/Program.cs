using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using TimerManager.App;
using TimerManager.Core;

// Load the production UI without starting tray, storage, single-instance or notification services.
internal sealed class CheckApp : App { protected override void OnStartup(StartupEventArgs e) { } }
internal sealed class Clock : ITimerClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    public TimeSpan AwakeTime { get; set; }
}
internal sealed class Store : IStateStore { public void Save(AppState state) { } }

internal static class Program
{
    private static int checks;
    private static string output = "";
    private static T Find<T>(FrameworkElement view, string name) where T : class => (T)view.FindName(name);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
    }
    private static void WaitFor(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        poll.Tick += (_, _) => { if (condition() || elapsed.Elapsed.TotalSeconds > 2) frame.Continue = false; };
        poll.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { poll.Stop(); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(System.IO.Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }
    private static void CaptureWindow(Window window, string name)
    {
        Drain();
        var content = (FrameworkElement)window.Content;
        window.UpdateLayout(); Drain();
        var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(System.IO.Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }
    private static void RunModal(Window dialog, Action action)
    {
        Exception? failure = null;
        dialog.Left = -10000;
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.ContentRendered += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { if (dialog.IsVisible) dialog.Close(); }
        }), DispatcherPriority.ApplicationIdle);
        dialog.ShowDialog();
        if (failure is not null) throw failure;
    }
    private static void Save(TimerDialog dialog) => Find<Button>(dialog, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Invoke(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        Drain();
    }
    private static void CheckSelectedText(TextBox field)
    {
        field.Focus(); field.SelectAll(); Drain();
        var start = field.GetRectFromCharacterIndex(0);
        var end = field.GetRectFromCharacterIndex(field.Text.Length - 1, trailingEdge: true);
        var bitmap = new RenderTargetBitmap((int)field.ActualWidth, (int)field.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen()) draw.DrawRectangle(new VisualBrush(field), null, new Rect(0, 0, field.ActualWidth, field.ActualHeight));
        bitmap.Render(visual);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var dark = 0;
        var lime = 0;
        for (var y = (int)start.Top + 2; y < (int)start.Bottom - 2; y++)
        for (var x = (int)start.Left + 2; x < (int)end.Left - 2; x++)
        {
            var offset = (y * bitmap.PixelWidth + x) * 4;
            if (pixels[offset] < 70 && pixels[offset + 1] < 70 && pixels[offset + 2] < 70) dark++;
            if (pixels[offset + 1] > 200 && pixels[offset + 2] > 180) lime++;
        }
        Check(dark > 20 && lime > 20, "Selected text remains visibly charcoal on lime");
    }
    private static void EditorChecks()
    {
        var saved = false;
        var create = new TimerDialog(null, false, (_, _, duration, finish) =>
        {
            Check(duration == TimeSpan.FromMinutes(15) && finish is null, "Duration creation submits the chosen total");
            saved = true;
            return true;
        });
        RunModal(create, () =>
        {
            Check(!Find<StackPanel>(create, "DurationGroup").IsVisible && !Find<StackPanel>(create, "FinishGroup").IsVisible, "No editable timing fields before choosing a method");
            CaptureWindow(create, "create-choice");
            Save(create);
            var name = Find<TextBox>(create, "NameInput");
            Check(name.IsKeyboardFocused && Find<TextBlock>(create, "NameError").Text.Contains("timer name"), "Missing name has an adjacent error and receives focus");
            Check((string)name.Tag == "Invalid", "Invalid name is marked");
            CaptureWindow(create, "name-error");
            name.Text = "Duration check";
            Check(name.Tag is null && Find<TextBlock>(create, "NameError").Text == "", "Correcting the name clears its error");
            Save(create);
            var choice = Find<RadioButton>(create, "ByDuration");
            Check(choice.IsKeyboardFocused && Find<TextBlock>(create, "ChoiceError").Text.Contains("Choose"), "Missing timing choice receives focus and an adjacent error");
            choice.IsChecked = true;
            create.UpdateLayout();
            Check(Find<StackPanel>(create, "DurationGroup").IsVisible && !Find<StackPanel>(create, "FinishGroup").IsVisible, "Duration mode displays only duration inputs");
            Check(Find<TextBlock>(create, "DerivedTiming").Text.StartsWith("Finishes "), "Derived finish is labelled text");
            var hours = Find<TextBox>(create, "HoursInput");
            hours.Text = "24";
            Save(create);
            Check(hours.IsKeyboardFocused && Find<TextBlock>(create, "DurationError").Text.Contains("hours"), "Invalid hours receive focus and a local error");
            hours.Text = "0";
            Find<TextBox>(create, "MinutesInput").Text = "15";
            Check(Find<TextBlock>(create, "DurationError").Text == "", "Correcting timing clears validation");
            name.Focus(); name.SelectAll();
            CaptureWindow(create, "create-duration");
            Check(create.ResizeMode == ResizeMode.NoResize && create.ActualHeight < 680, "Creation fits its content without resize controls");
            Save(create);
        });
        Check(saved, "Creation completed");

        var now = DateTimeOffset.UtcNow;
        var target = now.AddMinutes(15);
        target = target.AddTicks(-(target.Ticks % TimeSpan.TicksPerSecond));
        var engine = new TimerEngine(new Clock { UtcNow = now }, new AppState());
        var finishDialog = new TimerDialog(null, false, (name, tags, _, finish) => { engine.CreateUntil(name, finish!.Value, tags); return true; });
        RunModal(finishDialog, () =>
        {
            Find<TextBox>(finishDialog, "NameInput").Text = "Finish check";
            Find<RadioButton>(finishDialog, "ByFinish").IsChecked = true;
            finishDialog.UpdateLayout();
            Check(!Find<StackPanel>(finishDialog, "DurationGroup").IsVisible && Find<StackPanel>(finishDialog, "FinishGroup").IsVisible, "Finish mode displays only finish inputs");
            var date = Find<TextBox>(finishDialog, "FinishDateInput");
            var time = Find<TextBox>(finishDialog, "FinishTimeInput");
            date.Text = "2026-02-30";
            Save(finishDialog);
            Check(date.IsKeyboardFocused && Find<TextBlock>(finishDialog, "FinishError").Text.Contains("date"), "Invalid date receives focus");
            date.Text = target.ToLocalTime().ToString("yyyy-MM-dd");
            time.Text = "25:00:00";
            Save(finishDialog);
            Check(time.IsKeyboardFocused && Find<TextBlock>(finishDialog, "FinishError").Text.Contains("HH:MM:SS"), "Invalid time receives focus");
            date.Text = now.AddDays(-1).ToLocalTime().ToString("yyyy-MM-dd");
            time.Text = "12:00:00";
            Save(finishDialog);
            Check(Find<TextBlock>(finishDialog, "FinishError").Text.Contains("future"), "Engine rejection of past finish is shown at the finish fields");
            date.Text = target.ToLocalTime().ToString("yyyy-MM-dd");
            time.Text = target.ToLocalTime().ToString("HH:mm:ss");
            Check(Find<TextBlock>(finishDialog, "DerivedTiming").Text.Contains("from now"), "Calculated duration explains its meaning");
            CaptureWindow(finishDialog, "create-finish");
            Save(finishDialog);
            Check(!finishDialog.IsVisible, $"Valid finish saves: {Find<TextBlock>(finishDialog, "FinishError").Text} {Find<TextBlock>(finishDialog, "DurationError").Text}");
        });
        Check(engine.Snapshot.Timers.Single().DeadlineUtc == target, "Finish creation retains the exact target");

        var timer = new TimerItem { Name = "Write project proposal", Tags = ["Work"], Duration = TimeSpan.FromHours(1), Remaining = TimeSpan.FromMinutes(45), CreatedUtc = now.AddMinutes(-15), DeadlineUtc = now.AddMinutes(45), Status = TimerStatus.Running };
        var edit = new TimerDialog(timer, false, (_, _, duration, finish) => { Check(duration == TimeSpan.FromMinutes(70) && finish is null, "Running edit submits total duration"); return true; });
        RunModal(edit, () =>
        {
            var status = Find<TextBlock>(edit, "TimingStatus").Text;
            Check(status.Contains("When opened: Running") && status.Contains("15m counted") && status.Contains("45m remaining"), "Running edit explains a consistent counted and remaining snapshot");
            Check(Find<StackPanel>(edit, "DurationGroup").IsVisible && Find<StackPanel>(edit, "FinishGroup").IsVisible, "Editing retains both input groups");
            Find<TextBox>(edit, "MinutesInput").Text = "10";
            Check(Find<TextBox>(edit, "FinishTimeInput").Text == timer.DeadlineUtc!.Value.AddMinutes(10).ToLocalTime().ToString("HH:mm:ss"), "Editing total duration preserves counted time");
            CheckSelectedText(Find<TextBox>(edit, "NameInput"));
            CaptureWindow(edit, "edit-running");
            Save(edit);
        });
        var paused = new TimerDialog(timer with { Status = TimerStatus.Paused, DeadlineUtc = null }, true, (_, _, duration, finish) => { Check(duration is null && finish is null, "Name-only edit retains timing"); return true; });
        RunModal(paused, () =>
        {
            Check(Find<TextBlock>(paused, "FinishHeading").Text.Contains("if resumed now"), "Paused finish is explicitly conditional");
            Check(Find<TextBlock>(paused, "TimingStatus").Text.Contains("Paused"), "Paused state is visible in editor");
            Find<TextBox>(paused, "NameInput").Text = "Renamed paused timer";
            CaptureWindow(paused, "edit-paused");
            paused.MaxHeight = 480;
            paused.UpdateLayout();
            var scroll = (ScrollViewer)paused.Content;
            Check(scroll.ScrollableHeight > 0, "Editor scrolls within a short work area");
            Find<Button>(paused, "SaveButton").BringIntoView(); Drain();
            Check(scroll.VerticalOffset > 0, "Buttons remain reachable in a short work area");
            Save(paused);
        });
        var failure = new TimerDialog(timer, false, (_, _, _, _) => false);
        RunModal(failure, () => { Save(failure); Check(Find<TextBlock>(failure, "SaveError").IsVisible && failure.IsVisible, "Failed persistence leaves the editor open with a message"); });
    }

    private static void DashboardChecks(CheckApp app)
    {
        var clock = new Clock();
        var coordinator = new TimerCoordinator(clock, new Store(), new AppState(), _ => { });
        var running = coordinator.Engine.Create("Write project proposal", TimeSpan.FromHours(1), ["Work", "Writing"]);
        var paused = coordinator.Engine.Create("Laundry", TimeSpan.FromHours(1), ["Home"]);
        clock.UtcNow += TimeSpan.FromMinutes(22);
        coordinator.Engine.PauseOrResume(paused);
        var finished = coordinator.Engine.Create("Tea", TimeSpan.FromSeconds(1), ["Home"]);
        var dismissed = coordinator.Engine.Create("Stretch break", TimeSpan.FromSeconds(1), ["Personal"]);
        coordinator.Engine.Create("Review next week's schedule and prepare the meeting notes", TimeSpan.FromDays(3), ["Planning", "Work"]);
        clock.UtcNow += TimeSpan.FromSeconds(2);
        coordinator.Poll();
        coordinator.Engine.Dismiss(dismissed);
        var window = new MainWindow(coordinator, output, false) { Left = -10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        app.MainWindow = window;
        window.Show(); window.RefreshTimers(); Drain();
        var list = Find<ItemsControl>(window, "TimerList");
        Button ActionButton(Guid id) => Descendants<Button>(list).Single(button => button.DataContext is TimerRow row && row.Id == id);
        Check(Descendants<Button>(list).Count() == 5, "One action button per timer");
        Check(window.Rows.Single(row => row.Id == finished).NeedsAttention && !window.Rows.Single(row => row.Id == dismissed).NeedsAttention, "Dismissed and unattended finished timers are distinct");
        Check(window.Rows.Single(row => row.Id == paused).IsMuted && !window.Rows.Single(row => row.Id == running).IsMuted, "Paused countdowns are muted; running countdowns remain prominent");
        Check(window.Rows.Single(row => row.Id == paused).TimingDetail == "38m left of 1h", "Paused row includes remaining and total");
        Check(window.Rows.Single(row => row.Id == running).TimingDetail.StartsWith("finishes "), "Running row shows finish information");
        Check(window.Rows.Single(row => row.Id == finished).AccessibleCountdown.Contains("Finished"), "Countdown accessibility label includes timer state");
        var heights = Enumerable.Range(0, list.Items.Count).Select(index => ((ContentPresenter)list.ItemContainerGenerator.ContainerFromIndex(index)).ActualHeight).ToArray();
        Check(heights.All(height => height <= 74), "Rows remain compact including six-pixel spacing");
        CaptureWindow(window, "dashboard");

        var button = ActionButton(running);
        button.Focus();
        Check(button.IsKeyboardFocused, "Action button accepts keyboard focus");
        Invoke(button);
        var menu = button.ContextMenu;
        menu.UpdateLayout();
        Drain();
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Check(menu.IsOpen && window.Rows.Single(row => row.Id == running).IsMenuOpen, "Opening menu highlights its source row");
        Check(items.All(item => item.Tag is Guid id && id == running), "Every menu action targets its own timer");
        Check(items[0].IsKeyboardFocused, $"Opening menu moves keyboard focus to its first action (focused: {Keyboard.FocusedElement}, focusable: {items[0].Focusable}, visible: {items[0].IsVisible}, enabled: {items[0].IsEnabled})");
        Check(menu.Items.OfType<Separator>().Count() == 1, "Delete is separated from editing");
        Capture(menu, "actions-menu");
        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(coordinator.Engine.Snapshot.Timers.Single(timer => timer.Id == running).Status == TimerStatus.Paused, "Pause affects the selected timer");
        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(coordinator.Engine.Snapshot.Timers.Single(timer => timer.Id == running).Status == TimerStatus.Running, "Resume affects the selected timer");
        menu.IsOpen = false; WaitFor(() => !window.Rows.Single(row => row.Id == running).IsMenuOpen);
        Check(!window.Rows.Single(row => row.Id == running).IsMenuOpen, "Closing menu clears the source highlight");
        Check(button.IsKeyboardFocused, "Closing menu restores button focus");

        var filters = Find<WrapPanel>(window, "TagFilters");
        filters.Children.OfType<CheckBox>().Single(check => (string)check.Content == "Home").IsChecked = true;
        Check(window.Rows.Count == 2 && Find<TextBlock>(window, "SummaryText").Text.StartsWith("Showing 2 of 5"), "Filtered summary names visible and total counts");
        Check(Find<Button>(window, "ClearFiltersButton").IsVisible, "Filtering offers Clear");
        CaptureWindow(window, "filtered");
        Find<Button>(window, "ClearFiltersButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(window.Rows.Count == 5 && filters.Children.OfType<CheckBox>().All(check => check.IsChecked == false), "Clear restores all timers and resets checkboxes");
        Check(!Find<Button>(window, "ClearFiltersButton").IsVisible, "Clear hides when no filter is active");
        window.Width = window.MinWidth; window.UpdateLayout();
        Check(Descendants<Button>(list).All(item => item.ActualWidth == 36), "Minimum width retains action targets");
        var nameBlock = Descendants<TextBlock>(list).Single(block => block.Text.StartsWith("Review next") && block.ToolTip is string);
        Check((string)nameBlock.ToolTip == nameBlock.Text && nameBlock.TextTrimming == TextTrimming.CharacterEllipsis, "Long names keep ellipsis and full-name tooltip");
        CaptureWindow(window, "dashboard-narrow");
        var manyTags = Enumerable.Range(1, 20).Select(index => $"Tag {index:00}").ToArray();
        coordinator.Engine.Edit(running, "Write project proposal", manyTags, null);
        window.RefreshTimers(); window.UpdateLayout();
        Check(filters.ActualHeight > 32 && Descendants<CheckBox>(filters).Count() >= 20, "Many tags wrap and remain present");
        Check(Find<DockPanel>(window, "FilterControls").ActualHeight <= 110, "Tag filters leave room for timers");
        CaptureWindow(window, "many-tags");
        var filterScroll = Descendants<ScrollViewer>(Find<DockPanel>(window, "FilterControls")).Single();
        var filterBar = Descendants<ScrollBar>(filterScroll).Single(bar => bar.Orientation == Orientation.Vertical);
        ScrollBar.PageDownCommand.Execute(null, filterBar); Drain();
        Check(filterScroll.VerticalOffset > 0, "The themed scrollbar pages through tag filters");
        filterScroll.ScrollToTop();
        Find<TabControl>(window, "Tabs").SelectedIndex = 1; window.UpdateLayout();
        CaptureWindow(window, "settings");
        var toggle = Find<CheckBox>(window, "PreserveToggle");
        toggle.IsChecked = true;
        Check(coordinator.Engine.Snapshot.PreserveRemaining, "The themed checkbox still changes preserve mode");
        var glyph = (Border)toggle.Template.FindName("Glyph", toggle);
        Check(glyph.Background == app.FindResource("AccentBrush"), "Checked glyph uses accent fill");
        toggle.IsThreeState = true; toggle.IsChecked = null;
        Check(((Rectangle)toggle.Template.FindName("Mixed", toggle)).Visibility == Visibility.Visible, "Indeterminate checkbox has a distinct mark");
        toggle.IsChecked = false;
        Check(glyph.Background == Brushes.Transparent, "Unchecked glyph has transparent fill");
        Find<TabControl>(window, "Tabs").SelectedIndex = 0;
        foreach (var timer in coordinator.Engine.Snapshot.Timers) coordinator.Engine.Delete(timer.Id);
        window.RefreshTimers(); window.UpdateLayout();
        Check(!Find<StackPanel>(window, "SortControls").IsVisible && !Find<DockPanel>(window, "FilterControls").IsVisible, "Empty dashboard hides sorting and filters");
        CaptureWindow(window, "empty");
        window.Hide();
    }

    private static void ConfirmationChecks()
    {
        var dialog = new ConfirmationDialog("Delete timer", "Delete ‘Tea’? This cannot be undone.", "Delete");
        RunModal(dialog, () =>
        {
            var cancel = Find<Button>(dialog, "CancelButton");
            Check(cancel.IsDefault && cancel.IsCancel && cancel.IsKeyboardFocused, "Confirmation opens with Cancel as default and initial focus");
            Check(!Find<Button>(dialog, "ConfirmButton").IsDefault, "Destructive action is not the default");
            CaptureWindow(dialog, "delete-confirmation");
            Invoke(cancel);
        });
        Check(dialog.DialogResult != true, "Cancel does not confirm deletion");
        var restart = new ConfirmationDialog("Restart timer", "Restart ‘Laundry’ from its full duration?", "Restart");
        RunModal(restart, () => Invoke(Find<Button>(restart, "ConfirmButton")));
        Check(restart.DialogResult == true, "Explicit confirmation returns approval");
    }

    [STAThread]
    public static int Main(string[] args)
    {
        output = args.Length > 0 ? System.IO.Path.GetFullPath(args[0]) : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TimerManager-ui-checks");
        Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new CheckApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var source = XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "ApplicationResources.xaml"));
            var resources = new XElement(ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), source.Root!.Element(ns + "Application.Resources")!.Elements());
            app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
            Check(TimerDisplay.Duration(TimeSpan.FromSeconds(59.1)) == "1m", "Display rounds fractional remaining seconds up");
            Check(TimerDisplay.Duration(TimeSpan.Zero) == "0s", "Zero duration is explicit");
            EditorChecks();
            DashboardChecks(app);
            ConfirmationChecks();
            Console.WriteLine($"Passed {checks} WPF UI checks. Rendered views: {output}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Shutdown(); }
    }
}
