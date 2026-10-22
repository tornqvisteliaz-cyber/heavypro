using System.Windows;
using HeavyPro.Licensing;
using Rotaidem.Licensing;

namespace HeavyFeel.App;

/// <summary>
/// Asks for a licence key when this computer has none (for example when the
/// installer ran as another Windows user) or the saved one stopped working.
/// DialogResult is true once the key is activated and saved.
/// </summary>
public partial class LicenseWindow : Window
{
    public LicenseWindow(string? reason = null, string? key = null)
    {
        InitializeComponent();
        KeyBox.Text = key ?? "";
        if (!string.IsNullOrWhiteSpace(reason))
            ShowMessage(reason!);
        Loaded += (_, _) => KeyBox.Focus();
        HeavyProLicense.WakeServer();
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (!LicenseKey.IsWellFormed(KeyBox.Text, LicenseKey.HeavyProPrefix))
        {
            ShowMessage("That key has a typo. It should look like HPRO-XXXX-XXXX-XXXX.");
            return;
        }

        ActivateButton.IsEnabled = false;
        ActivateButton.Content = "Checking…";
        ShowMessage("Contacting the licence server. This can take up to a minute.");
        try
        {
            var result = await HeavyProLicense.ActivateAsync(KeyBox.Text);
            if (!result.IsValid)
            {
                ShowMessage(result.Message);
                return;
            }
            HeavyProLicense.Save(KeyBox.Text, result);
            Message.Visibility = Visibility.Collapsed;
            DialogResult = true;
        }
        finally
        {
            ActivateButton.IsEnabled = true;
            ActivateButton.Content = "Activate";
        }
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowMessage(string message)
    {
        Message.Text = message;
        Message.Visibility = Visibility.Visible;
    }
}
