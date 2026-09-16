using System;
using System.Globalization;
using System.Windows;
using TimerManager.Core;

namespace TimerManager.App;

public partial class TimerDialog : Window
{
    private readonly Func<string, string[], TimeSpan?, bool> save;
    private readonly bool editableDuration;
    private readonly TimeSpan? originalDuration;
    public TimerDialog(TimerItem? timer, Func<string, string[], TimeSpan?, bool> save)
    {
        this.save = save;
        editableDuration = timer is null || timer.Status == TimerStatus.Paused;
        originalDuration = timer?.Duration;
        InitializeComponent();
        if (timer is not null)
        {
            Heading.Text = "Edit timer";
            SaveButton.Content = "Save changes";
            NameInput.Text = timer.Name;
            TagsInput.Text = string.Join(", ", timer.Tags);
            var duration = timer.Duration;
            DaysInput.Text = duration.Days.ToString(CultureInfo.InvariantCulture);
            HoursInput.Text = duration.Hours.ToString(CultureInfo.InvariantCulture);
            MinutesInput.Text = duration.Minutes.ToString(CultureInfo.InvariantCulture);
            SecondsInput.Text = duration.Seconds.ToString(CultureInfo.InvariantCulture);
            DurationHint.Text = editableDuration ? "Changing duration replaces the remaining time and keeps this timer paused."
                : "Pause this timer to change its duration. Names and tags can be edited at any time.";
        }
        DurationInputs.IsEnabled = editableDuration;
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = TimerInput.Name(NameInput.Text);
            TimeSpan? duration = null;
            if (editableDuration)
            {
                var days = Parse(DaysInput.Text, "days", int.MaxValue);
                var hours = Parse(HoursInput.Text, "hours", 23);
                var minutes = Parse(MinutesInput.Text, "minutes", 59);
                var seconds = Parse(SecondsInput.Text, "seconds", 59);
                duration = TimerInput.Duration(TimeSpan.FromSeconds(checked((long)days * 86400 + hours * 3600 + minutes * 60 + seconds)));
                if (duration == originalDuration) duration = null;
            }
            if (save(name, TimerInput.Tags(TagsInput.Text.Split(',')), duration)) DialogResult = true;
            else ValidationText.Text = "The change could not be saved. Check the message in the main window and try again.";
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        { ValidationText.Text = ex is OverflowException ? "This duration is too large." : ex.Message; }
    }

    private static int Parse(string text, string name, int maximum)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > maximum)
            throw new ArgumentException($"Enter {name} between 0 and {maximum}.");
        return value;
    }
}
