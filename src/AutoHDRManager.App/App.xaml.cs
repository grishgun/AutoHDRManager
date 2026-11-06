using Microsoft.UI.Xaml;
using AutoHDRManager.Windows;

namespace AutoHDRManager.App;

public partial class App : Application
{
    private readonly SingleInstance instance;
    private readonly string[] args;
    private MainWindow? window;
    public App(SingleInstance instance, string[] args)
    {
        this.instance = instance; this.args = args; InitializeComponent();
        DebugSettings.BindingFailed += (_, e) => new Core.JsonEventLog(Path.Combine(Core.AppDataPath.Resolve(args), "Logs")).Write("XamlBindingFailed", new { e.Message });
        UnhandledException += (_, e) =>
        {
            var root = Path.Combine(Core.AppDataPath.Resolve(args), "Logs");
            new Core.JsonEventLog(root).Write("UnhandledException", new { e.Exception.Message, e.Exception.StackTrace });
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow(instance, this.args);
        instance.Activated += () => window.DispatcherQueue.TryEnqueue(() => { window.ShowWindow(); });
        instance.LaunchRequested = window.EnqueueLaunch;
        instance.VideoRequested = window.EnqueueVideo;
        instance.StartListening();
        window.Start();
    }
}
