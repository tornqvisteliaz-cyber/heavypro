using System.Windows;
using System.Windows.Threading;

namespace HeavyFeel.App;

public partial class App : System.Windows.Application
{
    public App()
    {
        InitializeComponent();
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ShowError(ex);
        };
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(
            ex.Message + Environment.NewLine + Environment.NewLine + ex,
            "HeavyPro",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
