using Microsoft.UI.Xaml;

namespace ToolBox;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => { Log(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception); e.SetObserved(); };
    }

    /// <summary>Crashes are written here instead of closing the window silently.</summary>
    public static void Log(Exception? e)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "toolbox-crash.log"), $"[{DateTime.Now:HH:mm:ss}] {e}{Environment.NewLine}{Environment.NewLine}"); } catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
