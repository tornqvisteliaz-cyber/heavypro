using System.Windows;

namespace HeavyFeel.App;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow(string version, string? whatsNew)
    {
        InitializeComponent();
        VersionText.Text = "Version " + version;
        if (whatsNew != null)
        {
            WhatsNewTitle.Text = "What's new in " + version;
            WhatsNewText.Text = whatsNew;
            WhatsNewPanel.Visibility = Visibility.Visible;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
