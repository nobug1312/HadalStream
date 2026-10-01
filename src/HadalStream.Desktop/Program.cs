using System.Runtime.InteropServices;
using System.Windows;
using HadalStream.Application;
using HadalStream.Application.Abstractions;
using HadalStream.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace HadalStream.Desktop;

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
        using var services = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(new AppPaths(dataDir, Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe")))
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();
        var log = services.GetRequiredService<IErrorLog>();
        var settings = services.GetRequiredService<ISettingsStore>();

        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write("Unhandled exception", e.ExceptionObject.ToString() ?? "");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Write("Unobserved task exception", e.Exception.ToString());
            e.SetObserved();
        };

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            log.Write("UI exception", e.Exception.ToString());
            MessageBox.Show($"Something went wrong.\n{e.Exception.Message}\n\nLog: {log.FileIn(settings.Current.DownloadDir)}",
                Title, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        app.Run(services.GetRequiredService<MainWindow>());

        // Off the (now dead) dispatcher context: awaits inside must not try to resume on the UI thread.
        var downloads = services.GetRequiredService<IDownloadQueue>();
        var conversions = services.GetRequiredService<IConversionQueue>();
        Task.Run(() => Task.WhenAll(downloads.ShutdownAsync(), conversions.ShutdownAsync())).GetAwaiter().GetResult();
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
