using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HeavyFeel.App.Services;

namespace HeavyFeel.App;

/// <summary>
/// The Terms of Service. On first start (or after the terms change) it must be accepted
/// to use HeavyPro; from the footer it only shows them.
/// </summary>
public partial class TermsWindow : Window
{
    private readonly bool _mustAccept;

    public TermsWindow(bool mustAccept)
    {
        InitializeComponent();
        _mustAccept = mustAccept;
        Intro.Text = mustAccept
            ? "Please read and accept the terms to use HeavyPro."
            : "Last updated " + Terms.Version + ".";
        if (!mustAccept)
        {
            DeclineButton.Visibility = Visibility.Collapsed;
            AcceptButton.Content = "Close";
        }
        Render(Terms.Text);
    }

    /// <summary>Draws the Markdown subset TERMS.md uses: headings, bullets and paragraphs.</summary>
    private void Render(string markdown)
    {
        var muted = (Brush)FindResource("BrushMuted");
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim().Replace("**", "");
            if (line.Length == 0)
                continue;
            if (line.StartsWith("# "))
            {
                Body.Children.Add(new TextBlock { Text = line.Substring(2), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
            }
            else if (line.StartsWith("## "))
            {
                Body.Children.Add(new TextBlock { Text = line.Substring(3).ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = muted, Margin = new Thickness(0, 16, 0, 6), TextWrapping = TextWrapping.Wrap });
            }
            else if (line.StartsWith("- "))
            {
                var row = new DockPanel { Margin = new Thickness(6, 2, 0, 2) };
                var dot = new TextBlock { Text = "•", Width = 14, Foreground = muted };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = line.Substring(2), TextWrapping = TextWrapping.Wrap, LineHeight = 19, Foreground = (Brush)FindResource("BrushText") });
                Body.Children.Add(row);
            }
            else
            {
                Body.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 2, 0, 6),
                    Foreground = line.StartsWith("Last updated") ? muted : (Brush)FindResource("BrushText") });
            }
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (_mustAccept)
        {
            try
            {
                Terms.Accept();
            }
            catch (Exception)
            {
                // Not saved: HeavyPro asks again next start, which is harmless.
            }
        }
        DialogResult = true;
    }

    private void Decline_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
