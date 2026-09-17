using System.Windows;

namespace TimerManager.App;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(string title, string message, string action)
    {
        InitializeComponent();
        Title = Heading.Text = title;
        Message.Text = message;
        ConfirmButton.Content = action;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, fitDialog: true);
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
