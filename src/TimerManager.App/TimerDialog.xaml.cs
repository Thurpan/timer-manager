using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
            SaveButton.Content = "Save changes";
            NameInput.Text = timer.Name;
            TagsInput.Text = string.Join(", ", timer.Tags);
            CreateTimingChoice.Visibility = Visibility.Collapsed;
        }
        DurationHint.Text = timer?.Status switch
        {
            TimerStatus.Paused => "Duration includes time already counted. This timer stays paused; finish assumes you resume now.",
            TimerStatus.Finished => "Extending this timer into the future starts it again.",
            TimerStatus.Running => "Duration includes time already counted. Changing either value updates the other.",
            _ => "Choose duration or finish time. The timer starts when you select Start timer."
        };
        if (preserve) DurationHint.Text += " Finish is an estimate while preserve mode is on.";
        SetDurationFields(draft.Values.Duration);
        SetFinishFields(draft.Values.FinishUtc);
        UpdateEditableFields();
        ready = true;
        preview.Tick += (_, _) => RefreshPreview();
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); preview.Start(); };
        Closed += (_, _) => preview.Stop();
    }

    private void UpdateEditableFields()
    {
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
            ValidationText.Text = "";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { ShowValidation(ex); }
        finally { updating = false; UpdateEditableFields(); }
    }

    private TimeSpan ReadDuration()
    {
        var days = Parse(DaysInput.Text, "days", int.MaxValue);
        var hours = Parse(HoursInput.Text, "hours", 23);
        var minutes = Parse(MinutesInput.Text, "minutes", 59);
        var seconds = Parse(SecondsInput.Text, "seconds", 59);
        return TimerInput.Duration(TimeSpan.FromTicks(checked(((long)days * 86400 + hours * 3600 + minutes * 60 + seconds) * TimeSpan.TicksPerSecond)));
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
            else draft.SetFinish(TimerTiming.ParseLocalFinish(FinishDateInput.Text, FinishTimeInput.Text, TimeZoneInfo.Local));
            inputValid = true;
            ValidationText.Text = "";
            RefreshPreview();
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            inputValid = false;
            ShowValidation(ex);
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
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { ShowValidation(ex); }
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
        try
        {
            var name = TimerInput.Name(NameInput.Text);
            if (activeInput is null) throw new ArgumentException("Choose duration or finish time.");
            TimeSpan? duration = null;
            DateTimeOffset? finish = null;
            if (timer is null || timingEdited)
            {
                if (activeInput == TimingInput.Duration)
                {
                    duration = ReadDuration();
                    if (timer is not null && duration == timer.Duration) duration = null;
                }
                else finish = TimerTiming.ParseLocalFinish(FinishDateInput.Text, FinishTimeInput.Text, TimeZoneInfo.Local);
            }
            if (save(name, TimerInput.Tags(TagsInput.Text.Split(',')), duration, finish)) DialogResult = true;
            else ValidationText.Text = "The change could not be saved. Check the message in the main window and try again.";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) { ShowValidation(ex); }
    }

    private void ShowValidation(Exception ex) => ValidationText.Text = ex is OverflowException ? "This duration is too large." : ex.Message;

    private static int Parse(string text, string name, int maximum)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > maximum)
            throw new ArgumentException($"Enter {name} between 0 and {maximum}.");
        return value;
    }
}
