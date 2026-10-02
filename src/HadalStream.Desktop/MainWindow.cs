using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using HadalStream.Application.Abstractions;
using HadalStream.Domain.Downloads;
using HadalStream.Infrastructure;
using MediatR;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HadalStream.Desktop;

// Native window hosting the React UI in WebView2. No local server: the UI is served from wwwroot via a virtual host.
internal sealed partial class MainWindow : Window
{
    private const string AssetHost = "appassets.example";
    private const string AppOrigin = $"https://{AssetHost}/";

    // Set by the "dev" launch profile so the UI comes from the Vite dev server.
    private static readonly string? DevUrl = Environment.GetEnvironmentVariable("HADAL_DEV_URL");

    private readonly WebView2 web = new();
    private readonly AppPaths paths;
    private readonly ISender mediator;
    private readonly ISettingsStore settings;
    private readonly IDownloadQueue downloads;
    private readonly IConversionQueue conversions;
    private readonly Dictionary<string, DownloadView> downloading = [];
    private bool closed;

    public MainWindow(AppPaths paths, ISender mediator, ISettingsStore settings, IDownloadQueue downloads, IConversionQueue conversions)
    {
        this.paths = paths;
        this.mediator = mediator;
        this.settings = settings;
        this.downloads = downloads;
        this.conversions = conversions;

        Title = Program.Title;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/app.ico"));
        Width = 1240;
        Height = 800;
        MinWidth = 960;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TaskbarItemInfo = new TaskbarItemInfo();
        Content = web;

        ApplyTheme();
        SourceInitialized += (_, _) => ApplyTheme();
        Loaded += async (_, _) => await InitializeWebViewAsync();
        downloads.Changed += OnDownloadChanged;
        downloads.Removed += OnDownloadRemoved;
        conversions.Changed += OnConversionChanged;
        conversions.Removed += OnConversionRemoved;
        Closed += (_, _) =>
        {
            closed = true;
            downloads.Changed -= OnDownloadChanged;
            downloads.Removed -= OnDownloadRemoved;
            conversions.Changed -= OnConversionChanged;
            conversions.Removed -= OnConversionRemoved;
        };
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(paths.DataDir, "WebView2"));
            await web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "HadalStream needs the Microsoft Edge WebView2 Runtime.\nGet it at https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                Program.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        var core = web.CoreWebView2;
        var dev = DevUrl is not null;
        core.Settings.AreDevToolsEnabled = dev;
        core.Settings.AreDefaultContextMenusEnabled = dev;
        core.Settings.AreBrowserAcceleratorKeysEnabled = dev;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.SetVirtualHostNameToFolderMapping(AssetHost, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Deny);
        core.NavigationStarting += (_, e) => e.Cancel = !IsTrusted(e.Uri);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += OnWebMessage;
        core.Navigate($"{DevUrl ?? AppOrigin + "index.html"}?theme={settings.Current.Theme}");
    }

    private static bool IsTrusted(string uri) => uri.StartsWith(DevUrl ?? AppOrigin, StringComparison.OrdinalIgnoreCase);

    private void OnDownloadChanged(DownloadView job) => OnDownload("job", job);

    private void OnDownloadRemoved(DownloadView job) => OnDownload("removed", job);

    private void OnDownload(string type, DownloadView job) => Dispatcher.InvokeAsync(() =>
    {
        Post(new { @event = type, data = job });
        if (type == "job" && job.Status == DownloadStatus.Downloading) downloading[job.Id] = job;
        else downloading.Remove(job.Id);
        UpdateTaskbar();
    });

    private void OnConversionChanged(ConversionView item) => Dispatcher.InvokeAsync(() => Post(new { @event = "conversion", data = item }));

    private void OnConversionRemoved(ConversionView item) => Dispatcher.InvokeAsync(() => Post(new { @event = "conversionRemoved", data = item }));

    private void UpdateTaskbar()
    {
        var total = downloading.Values.Sum(j => j.TotalBytes);
        TaskbarItemInfo.ProgressState = downloading.Count == 0 ? TaskbarItemProgressState.None
            : total == 0 ? TaskbarItemProgressState.Indeterminate
            : TaskbarItemProgressState.Normal;
        if (total > 0) TaskbarItemInfo.ProgressValue = (double)downloading.Values.Sum(j => j.DownloadedBytes) / total;
    }

    // Native chrome matches the UI's sidebar so the title bar blends into the app (Windows 11).
    private void ApplyTheme()
    {
        var night = settings.Current.Theme == "dark";
        var (chrome, text) = night ? (0x161B29, 0xEFE8D8) : (0xEEF4EC, 0x2F3B33);
        Background = new SolidColorBrush(Color.FromRgb((byte)(chrome >> 16), (byte)(chrome >> 8), (byte)chrome));
        web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(unchecked((int)0xFF000000) | chrome);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        SetDwmAttribute(hwnd, 20, night ? 1 : 0); // DWMWA_USE_IMMERSIVE_DARK_MODE
        SetDwmAttribute(hwnd, 34, ToColorRef(chrome)); // DWMWA_BORDER_COLOR
        SetDwmAttribute(hwnd, 35, ToColorRef(chrome)); // DWMWA_CAPTION_COLOR
        SetDwmAttribute(hwnd, 36, ToColorRef(text)); // DWMWA_TEXT_COLOR
    }

    private static int ToColorRef(int rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    private static void SetDwmAttribute(nint hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
