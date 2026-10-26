using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ToolBox.Pages;

namespace ToolBox;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        try { Logo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "icon-256.png"))); } catch { }
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")); } catch { }
        foreach (var it in Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>())
            if (it.Icon is FontIcon fi && it.Tag is string tag) fi.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(HomePage.ColorOf(tag));
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        // The app follows the Windows light / dark setting. TOOLBOX_THEME=light|dark forces one for testing.
        if (Environment.GetEnvironmentVariable("TOOLBOX_THEME") is { } th && Content is FrameworkElement root)
            root.RequestedTheme = th.Equals("light", StringComparison.OrdinalIgnoreCase) ? ElementTheme.Light : th.Equals("dark", StringComparison.OrdinalIgnoreCase) ? ElementTheme.Dark : ElementTheme.Default;
        Nav.ActualThemeChanged += (_, _) =>
        {
            try { if (PageFrame.Content is Page p) PageFrame.Content = (Page)Activator.CreateInstance(p.GetType())!; } catch { }
        };
        if (Diagnostics.SelfTest.Requested) { _ = Diagnostics.SelfTest.RunAndExitAsync(); return; }
        OpenStartPage();
    }

    /// <summary>Shows the start page: --tool=<tag> on the command line, TOOLBOX_START, or Home.</summary>
    void OpenStartPage()
    {
        try
        {
            Loading.Visibility = Visibility.Collapsed;
            GoTo(Environment.GetEnvironmentVariable("TOOLBOX_START") ?? CmdTool() ?? "home");
        }
        catch (Exception e) { LoadingText.Text = e.Message; Loading.Visibility = Visibility.Visible; }
    }

    /// <summary>A tool named on the command line (--tool=hosts), used when the app restarts itself as administrator.</summary>
    static string? CmdTool() => Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => a.StartsWith("--tool="))?[7..];

    /// <summary>Opens a tool by its tag, as if its item in the menu was clicked.</summary>
    public void GoTo(string tag)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (item != null) Nav.SelectedItem = item;
    }

    // Search box in the navigation pane: hides tools whose name does not match, and group headers with no tools left
    void ToolSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var q = sender.Text.Trim();
        NavigationViewItemHeader? head = null;
        bool any = false;
        foreach (var obj in Nav.MenuItems)
        {
            if (obj is NavigationViewItemHeader h)
            {
                if (head != null) head.Visibility = any || q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                head = h;
                any = false;
            }
            else if (obj is NavigationViewItem it)
            {
                bool show = q.Length == 0 || (it.Content as string ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
                it.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                any |= show;
            }
        }
        if (head != null) head.Visibility = any || q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    /// <summary>In the narrow (minimal) layout the menu button floats over the page, so push the page down below it.</summary>
    void Nav_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) =>
        PageFrame.Margin = new Thickness(0, args.DisplayMode == NavigationViewDisplayMode.Minimal ? 44 : 0, 0, 0);

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = (args.SelectedItem as NavigationViewItem)?.Tag switch
        {
            "home" => typeof(HomePage),
            "hw" => typeof(HardwarePage),
            "foldersize" => typeof(FolderSizePage),
            "env" => typeof(EnvPage),
            "trace" => typeof(TracePage),
            "subnet" => typeof(SubnetPage),
            "ipscan" => typeof(IpScanPage),
            "telnet" => typeof(TelnetPage),
            "crypt" => typeof(CryptPage),
            "img64" => typeof(Img64Page),
            "ico" => typeof(IcoPage),
            "temps" => typeof(TempsPage),
            "proc" => typeof(ProcessPage),
            "disk" => typeof(DiskPage),
            "hosts" => typeof(HostsPage),
            "adapters" => typeof(AdaptersPage),
            "dns" => typeof(DnsPage),
            "ping" => typeof(PingPage),
            "hash" => typeof(HashPage),
            "token" => typeof(TokenPage),
            "encode" => typeof(EncodePage),
            "json" => typeof(JsonPage),
            "jwt" => typeof(JwtPage),
            "time" => typeof(TimePage),
            "base" => typeof(BasePage),
            _ => null
        };
        HomePage.Record((args.SelectedItem as NavigationViewItem)?.Tag as string ?? "");
        if (page == null) return;
        try { PageFrame.Content = (Page)Activator.CreateInstance(page)!; }
        catch (Exception e)
        {
            App.Log(e);
            PageFrame.Content = new TextBlock { Text = "This page could not be opened: " + (e.InnerException ?? e).Message, Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap };
        }
    }
}
