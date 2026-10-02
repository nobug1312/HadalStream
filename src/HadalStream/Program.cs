using System.Runtime.InteropServices;
using System.Windows;
using Velopack;
using HadalStream.Conversion;
using HadalStream.Downloads;
using HadalStream.Resolvers;
using HadalStream.Settings;

namespace HadalStream;

public static partial class Program
{
    public const string Title = "HadalStream";

    [STAThread]
    public static void Main()
    {
        // Must run first: handles Velopack install/uninstall hooks and exits early when invoked by the installer.
        VelopackApp.Build().Run();

        // Two instances would fight over jobs.json and the .part files.
        using var instance = new Mutex(true, "HadalStream.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            ActivateExistingWindow();
            return;
        }

        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HadalStream");
        var settings = new SettingsStore(dataDir);
        void LogCrash(string context, object error) => ErrorLog.Write(settings.Current.DownloadDir, context, error.ToString() ?? "");

        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("Unhandled exception", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        var http = new HttpClientProvider(settings);
        var resolver = new VideoResolver(http, settings);
        var manager = new DownloadManager(dataDir, settings, resolver, http);
        var converter = new ConversionManager(Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe"), settings);

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            LogCrash("UI exception", e.Exception);
            MessageBox.Show($"Something went wrong:\n{e.Exception.Message}\n\nDetails were written to {ErrorLog.PathIn(settings.Current.DownloadDir)}",
                Title, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        app.Run(new MainWindow(dataDir, settings, resolver, manager, converter));

        // Off the (now dead) dispatcher context: awaits inside must not try to resume on the UI thread.
        Task.Run(() => Task.WhenAll(manager.ShutdownAsync(), converter.ShutdownAsync())).GetAwaiter().GetResult();
    }

    private static void ActivateExistingWindow()
    {
        var hwnd = FindWindow(null, Title);
        if (hwnd == 0) return;
        ShowWindow(hwnd, 9); // SW_RESTORE
        SetForegroundWindow(hwnd);
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string? className, string windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);
}
