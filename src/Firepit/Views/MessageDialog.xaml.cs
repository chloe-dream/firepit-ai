using System.Windows;
using Firepit.Native;

namespace Firepit.Views;

public partial class MessageDialog : Window
{
    private MessageDialog(string title, string message, string primaryLabel, string? secondaryLabel)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryLabel;
        if (secondaryLabel is null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.IsCancel = true;
        }
        else
        {
            SecondaryButton.Content = secondaryLabel;
        }
        if (TryFindResource("DialogCaptionPixelHeight") is double capH)
        {
            CaptionRow.Height = new GridLength(capH);
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome is not null) chrome.CaptionHeight = capH;
        }
        SourceInitialized += (_, _) => WindowDarkMode.EnableForWindow(this);
    }

    public static bool Show(
        Window? owner,
        string title,
        string message,
        string primaryLabel = "OK",
        string? secondaryLabel = null)
    {
        var dialog = new MessageDialog(title, message, primaryLabel, secondaryLabel);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        return dialog.ShowDialog() == true;
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
