using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TimerManager.Core;

namespace TimerManager.App;

public partial class TimerDialog : Window
{
    private readonly Func<string, string[], TimeSpan?, DateTimeOffset?, bool> save;
    private readonly TimerItem? timer;
    private readonly TimerTimingDraft draft;
    private readonly DispatcherTimer preview = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimingInput? activeInput;
    private bool updating;
    private bool ready;
    private bool timingEdited;
    private bool inputValid = true;

    public TimerDialog(TimerItem? timer, bool preserve, Func<string, string[], TimeSpan?, DateTimeOffset?, bool> save)
    {
        this.save = save;
        this.timer = timer;
        draft = new TimerTimingDraft(new WindowsClock(), timer, preserve);
        activeInput = draft.Input;
        InitializeComponent();
        if (timer is not null)
        {
            Heading.Text = "Edit timer";
            Title = $"Edit ‘{timer.Name}’";
            SaveButton.Content = "Save changes";
            NameInput.Text = timer.Name;
            TagsInput.Text = string.Join(", ", timer.Tags);
            CreateTimingChoice.Visibility = Visibility.Collapsed;
            DurationHeading.Text = "Total duration";
            TimingStatus.Visibility = Visibility.Visible;
            var elapsed = timer.Duration - TimerTiming.WholeSeconds(timer.Remaining);
            TimingStatus.Text = $"When opened: {timer.Status} · {TimerDisplay.Duration(elapsed)} counted · {TimerDisplay.Duration(timer.Remaining)} remaining";
            if (timer.Status == TimerStatus.Paused) FinishHeading.Text = "Estimated finish · if resumed now";
            else if (preserve) FinishHeading.Text = "Estimated finish · local time";
        }
        DurationHint.Text = timer?.Status switch
        {
            TimerStatus.Paused => "Duration includes time already counted. This timer stays paused; finish assumes you resume now.",
            TimerStatus.Finished => "Extending this timer into the future starts it again.",
            TimerStatus.Running => "Duration includes time already counted. Changing either value updates the other.",
            _ => "The timer starts when you select Start timer."
        };
        if (preserve) DurationHint.Text += " Finish is an estimate while preserve mode is on.";
        SetDurationFields(draft.Values.Duration);
        SetFinishFields(draft.Values.FinishUtc);
        UpdateEditableFields();
        ready = true;
        RefreshPreview();
        preview.Tick += (_, _) => RefreshPreview();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, fitDialog: true);
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); preview.Start(); };
        Closed += (_, _) => preview.Stop();
    }

    private void UpdateEditableFields()
    {
        DurationGroup.Visibility = timer is not null || activeInput == TimingInput.Duration ? Visibility.Visible : Visibility.Collapsed;
        FinishGroup.Visibility = timer is not null || activeInput == TimingInput.Finish ? Visibility.Visible : Visibility.Collapsed;
        DerivedTiming.Visibility = timer is null ? Visibility.Visible : Visibility.Collapsed;
        foreach (var field in new[] { DaysInput, HoursInput, MinutesInput, SecondsInput })
        {
            field.IsReadOnly = timer is null && activeInput != TimingInput.Duration;
            field.IsTabStop = !field.IsReadOnly;
        }
        foreach (var field in new[] { FinishDateInput, FinishTimeInput })
        {
            field.IsReadOnly = timer is null && activeInput != TimingInput.Finish;
            field.IsTabStop = !field.IsReadOnly;
        }
    }

    private void TimingMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || updating) return;
        activeInput = ByFinish.IsChecked == true ? TimingInput.Finish : TimingInput.Duration;
        try
        {
            draft.Select(activeInput.Value);
            inputValid = true;
            updating = true;
            SetDurationFields(draft.Values.Duration);
            SetFinishFields(draft.Values.FinishUtc);
            ClearTimingValidation();
            ChoiceError.Text = "";
            ChoiceBorder.BorderBrush = Brushes.Transparent;
            AutomationProperties.SetHelpText(ByDuration, "");
            AutomationProperties.SetHelpText(ByFinish, "");
            RefreshPreview();
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { ShowTimingValidation(ex); }
        finally { updating = false; UpdateEditableFields(); }
    }

    private TimeSpan ReadDuration()
    {
        var days = Parse(DaysInput, "days", int.MaxValue);
        var hours = Parse(HoursInput, "hours", 23);
        var minutes = Parse(MinutesInput, "minutes", 59);
        var seconds = Parse(SecondsInput, "seconds", 59);
        return TimerInput.Duration(TimeSpan.FromTicks(checked(((long)days * 86400 + hours * 3600 + minutes * 60 + seconds) * TimeSpan.TicksPerSecond)));
    }

    private DateTimeOffset ReadFinish()
    {
        if (!DateTime.TryParseExact(FinishDateInput.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new FieldException(FinishDateInput, "Enter a finish date as YYYY-MM-DD.");
        if (!DateTime.TryParseExact(FinishTimeInput.Text.Trim(), "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new FieldException(FinishTimeInput, "Enter a finish time as HH:MM:SS (24-hour).");
        return TimerTiming.ParseLocalFinish(FinishDateInput.Text, FinishTimeInput.Text, TimeZoneInfo.Local);
    }

    private void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (!ready || NameError.Text.Length == 0) return;
        try { TimerInput.Name(NameInput.Text); NameError.Text = ""; ClearField(NameInput); }
        catch (ArgumentException) { }
    }

    private void Duration_Changed(object sender, TextChangedEventArgs e) => ReadTimingInput(TimingInput.Duration);
    private void Finish_Changed(object sender, TextChangedEventArgs e) => ReadTimingInput(TimingInput.Finish);

    private void ReadTimingInput(TimingInput input)
    {
        if (!ready || updating) return;
        activeInput = input;
        timingEdited = true;
        try
        {
            if (input == TimingInput.Duration) draft.SetDuration(ReadDuration());
            else draft.SetFinish(ReadFinish());
            inputValid = true;
            ClearTimingValidation();
            RefreshPreview();
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            inputValid = false;
            ShowTimingValidation(ex);
        }
    }

    private void RefreshPreview()
    {
        if (!inputValid) return;
        updating = true;
        try
        {
            var values = draft.Values;
            if (activeInput == TimingInput.Finish) SetDurationFields(values.Duration);
            else SetFinishFields(values.FinishUtc);
            DerivedTiming.Text = activeInput switch
            {
                TimingInput.Duration => $"Finishes {TimerDisplay.Finish(values.FinishUtc, DateTimeOffset.Now, includeSeconds: true)}",
                TimingInput.Finish => values.FinishUtc <= DateTimeOffset.UtcNow ? "This finish time has passed." : $"Duration · {TimerDisplay.Duration(values.Duration)} from now",
                _ => $"Choose a method to edit. Duration · {TimerDisplay.Duration(values.Duration)}; finishes {TimerDisplay.Finish(values.FinishUtc, DateTimeOffset.Now, includeSeconds: true)}."
            };
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { ShowTimingValidation(ex); }
        finally { updating = false; }
    }

    private void SetDurationFields(TimeSpan duration)
    {
        duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        DaysInput.Text = duration.Days.ToString(CultureInfo.InvariantCulture);
        HoursInput.Text = duration.Hours.ToString(CultureInfo.InvariantCulture);
        MinutesInput.Text = duration.Minutes.ToString(CultureInfo.InvariantCulture);
        SecondsInput.Text = duration.Seconds.ToString(CultureInfo.InvariantCulture);
    }

    private void SetFinishFields(DateTimeOffset finish)
    {
        var local = finish.ToLocalTime();
        FinishDateInput.Text = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        FinishTimeInput.Text = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name;
        try { name = TimerInput.Name(NameInput.Text); }
        catch (ArgumentException ex)
        {
            NameError.Text = ex.Message;
            MarkField(NameInput, ex.Message, focus: true);
            return;
        }
        if (activeInput is null)
        {
            ChoiceError.Text = "Choose duration or finish time.";
            ChoiceBorder.BorderBrush = (Brush)FindResource("ErrorBrush");
            AutomationProperties.SetHelpText(ByDuration, ChoiceError.Text);
            AutomationProperties.SetHelpText(ByFinish, ChoiceError.Text);
            ByDuration.Focus();
            ByDuration.BringIntoView();
            return;
        }
        try
        {
            TimeSpan? duration = null;
            DateTimeOffset? finish = null;
            if (timer is null || timingEdited)
            {
                if (activeInput == TimingInput.Duration)
                {
                    duration = ReadDuration();
                    if (timer is not null && duration == timer.Duration) duration = null;
                }
                else finish = ReadFinish();
            }
            if (save(name, TimerInput.Tags(TagsInput.Text.Split(',')), duration, finish)) DialogResult = true;
            else
            {
                SaveError.Text = "The change could not be saved. Check the message in the main window and try again.";
                SaveError.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { ShowTimingValidation(ex, focus: true); }
    }

    private void ShowTimingValidation(Exception ex, bool focus = false)
    {
        ClearTimingValidation();
        var message = ex is OverflowException ? "This duration is too large." : ex.Message;
        var field = ex is FieldException fieldError ? fieldError.Field : activeInput == TimingInput.Finish ? FinishTimeInput : DaysInput;
        (activeInput == TimingInput.Finish ? FinishError : DurationError).Text = message;
        MarkField(field, message, focus);
        if (timer is null) DerivedTiming.Text = "Complete a valid time to see the calculated value.";
    }

    private static void MarkField(TextBox field, string message, bool focus)
    {
        field.Tag = "Invalid";
        AutomationProperties.SetHelpText(field, message);
        if (focus) { field.Focus(); field.BringIntoView(); }
    }

    private static void ClearField(TextBox field)
    {
        field.Tag = null;
        AutomationProperties.SetHelpText(field, "");
    }

    private void ClearTimingValidation()
    {
        DurationError.Text = FinishError.Text = "";
        foreach (var field in new[] { DaysInput, HoursInput, MinutesInput, SecondsInput, FinishDateInput, FinishTimeInput }) ClearField(field);
        SaveError.Visibility = Visibility.Collapsed;
    }

    private sealed class FieldException(TextBox field, string message) : ArgumentException(message)
    {
        public TextBox Field { get; } = field;
    }

    private static int Parse(TextBox field, string name, int maximum)
    {
        if (!int.TryParse(field.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > maximum)
            throw new FieldException(field, $"Enter {name} between 0 and {maximum}.");
        return value;
    }
}
