using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace ToolBox.Pages;

/// <summary>About: version, the Magestica brand, developer, links, licence and privacy.</summary>
public sealed class AboutPage : Page
{
    static readonly Color Burgundy = Color.FromArgb(255, 152, 31, 50);
    const string Repo = "https://github.com/smtanbin/tool-box", Site = "https://www.magestica.com/";

    public AboutPage() => Content = Ui.Page(Build());

    /// <summary>The About card: brand banner, version, developer, links. Used by the About page and at the bottom of Home.</summary>
    public static UIElement Build()
    {
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "2.2.0";

        // Brand banner in the Magestica burgundy: wordmark in tight, heavy type.
        var wordmark = new TextBlock { Text = "Magestica", FontSize = 44, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = -40, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        var tag = new TextBlock { Text = "Designed and developed by Tanbin", Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), FontSize = 14 };
        var visit = new HyperlinkButton { Content = "www.magestica.com", NavigateUri = new Uri(Site), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Padding = new Thickness(0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var banner = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(28, 24, 28, 22),
            Background = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1), GradientStops = { new GradientStop { Color = Burgundy, Offset = 0 }, new GradientStop { Color = Color.FromArgb(255, 94, 18, 33), Offset = 1 } } },
            Child = Ui.Stack(4, wordmark, tag, visit)
        };

        var head = new Grid { ColumnSpacing = 18 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new Image { Width = 72, Height = 72 };
        try { icon.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "icon-256.png"))); } catch { }
        head.Children.Add(icon);
        var titles = Ui.Stack(2, new TextBlock { Text = "My Tool Box", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] }, Ui.Hint($"Version {version}  ·  native Windows app (WinUI 3)"), Ui.Hint("Everyday IT tools for Windows."));
        titles.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(titles, 1); head.Children.Add(titles);

        FrameworkElement Link(string text, string url) => new HyperlinkButton { Content = text, NavigateUri = new Uri(url), Padding = new Thickness(0) };
        var rows = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        rows.ColumnDefinitions.Add(new ColumnDefinition());
        var items = new (string k, FrameworkElement v)[]
        {
            ("Developer", new TextBlock { Text = "Tanbin" }), ("Brand", Link("www.magestica.com", Site)), ("Source code", Link("github.com/smtanbin/tool-box", Repo)),
            ("License", Link("Apache 2.0 + Commons Clause", Repo + "/blob/main/LICENSE")), ("Privacy", Link("Privacy policy: nothing leaves this PC", Repo + "/blob/main/PRIVACY.md"))
        };
        for (var i = 0; i < items.Length; i++)
        {
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = Ui.Hint(items[i].k); k.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(k, i);
            Grid.SetRow(items[i].v, i); Grid.SetColumn(items[i].v, 1);
            rows.Children.Add(k); rows.Children.Add(items[i].v);
        }

        return Ui.Stack(16, banner, Ui.Card(Ui.Stack(14, head, rows)),
            Ui.Hint($"© {DateTime.Now.Year} Tanbin · www.magestica.com. Free for personal and business use; not for resale."));
    }
}
