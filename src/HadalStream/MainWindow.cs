using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using HadalStream.Conversion;
using HadalStream.Downloads;
using HadalStream.Resolvers;
using HadalStream.Settings;

namespace HadalStream;

// Native window hosting the React UI in WebView2. The UI talks to C# through postMessage, no local server.
internal sealed partial class MainWindow : Window
{
    private const string AssetHost = "appassets.example";
    private const string AppOrigin = $"https://{AssetHost}/";

    // Set by the "dev" launch profile so the UI comes from the Vite dev server.
    private static readonly string? DevUrl = Environment.GetEnvironmentVariable("HADAL_DEV_URL");

    private readonly WebView2 web = new();
    private readonly string dataDir;
    private readonly SettingsStore settings;
    private readonly VideoResolver resolver;
    private readonly DownloadManager manager;
    private readonly ConversionManager converter;
    private readonly Dictionary<string, JobView> downloading = [];
    private bool closed;

    private sealed record Request(int Id, string Method, JsonElement Params);

    public MainWindow(string dataDir, SettingsStore settings, VideoResolver resolver, DownloadManager manager, ConversionManager converter)
    {
        this.dataDir = dataDir;
        this.settings = settings;
        this.resolver = resolver;
        this.manager = manager;
        this.converter = converter;

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
        manager.Changed += OnJobChanged;
        converter.Changed += OnConversionChanged;
        Closed += (_, _) =>
        {
            closed = true;
            manager.Changed -= OnJobChanged;
            converter.Changed -= OnConversionChanged;
        };
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataDir, "WebView2"));
            await web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "Microsoft Edge WebView2 Runtime is required.\nReinstall the app, or install the runtime from https://go.microsoft.com/fwlink/p/?LinkId=2124703",
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

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsTrusted(e.Source)) return;
        Request? request = null;
        try
        {
            request = JsonSerializer.Deserialize<Request>(e.WebMessageAsJson, AppJson.Options)!;
            // Dropped files arrive as WebView2 file objects carrying real paths; page script can't forge them.
            var dropped = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToArray() ?? [];
            Post(new { id = request.Id, result = await HandleAsync(request.Method, request.Params, dropped) });
        }
        catch (Exception ex)
        {
            // ArgumentException = invalid input; the message is meant for the user, not the log.
            if (ex is not ArgumentException)
                ErrorLog.Write(settings.Current.DownloadDir, $"Command '{request?.Method}' failed", ex.ToString());
            Post(new { id = request?.Id, error = ex.Message });
        }
    }

    private async Task<object?> HandleAsync(string method, JsonElement args, string[] dropped)
    {
        switch (method)
        {
            case "resolve":
            {
                var input = args.GetProperty("input").GetString() ?? "";
                if (input.Length is 0 or > 20_000) throw new ArgumentException("Paste between 1 and 20,000 characters.");
                return await resolver.ResolveAsync(input, CancellationToken.None);
            }
            case "jobs.list":
                return manager.List();
            case "jobs.add":
            {
                var items = args.Deserialize<AddJobRequest[]>(AppJson.Options) ?? [];
                if (items.Length is 0 or > 100 || !items.All(IsValid)) throw new ArgumentException("Invalid download request.");
                return manager.Add(items);
            }
            case "jobs.pause": return Ensure(manager.Pause(JobId(args)));
            case "jobs.resume": return Ensure(manager.Resume(JobId(args)));
            case "jobs.cancel": return Ensure(manager.Cancel(JobId(args)));
            case "jobs.remove": return Ensure(manager.Remove(JobId(args)));
            case "jobs.open": return Ensure(Open(manager.GetCompletedFile(JobId(args))));
            case "jobs.reveal": return Ensure(Reveal(manager.GetCompletedFile(JobId(args))));
            case "jobs.revealLog": return Ensure(Reveal(manager.GetLogFile(JobId(args))));
            case "settings.get":
                return settings.Current;
            case "settings.save":
            {
                var error = settings.Update(args.Deserialize<AppSettings>(AppJson.Options) ?? throw new ArgumentException("Invalid settings."));
                if (error is not null) throw new ArgumentException(error);
                ApplyTheme();
                return settings.Current;
            }
            case "settings.pickFolder":
            {
                var dialog = new OpenFolderDialog { Title = "Choose download folder", InitialDirectory = settings.Current.DownloadDir };
                return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
            }
            case "convert.list":
                return converter.List();
            case "convert.pick":
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Choose videos to convert",
                    Filter = "Videos (*.flv;*.mov;*.mkv)|*.flv;*.mov;*.mkv",
                    Multiselect = true,
                };
                return dialog.ShowDialog(this) == true ? converter.Add(dialog.FileNames) : Array.Empty<ConversionView>();
            }
            case "convert.drop":
            {
                var added = converter.Add(dropped);
                if (added.Length == 0) throw new ArgumentException("Drop FLV, MOV or MKV files to convert them to MP4.");
                return added;
            }
            case "convert.cancel": return Ensure(converter.Cancel(JobId(args)));
            case "convert.retry": return Ensure(converter.Retry(JobId(args)));
            case "convert.remove": return Ensure(converter.Remove(JobId(args)));
            case "convert.open": return Ensure(Open(converter.GetOutput(JobId(args))));
            case "convert.reveal": return Ensure(Reveal(converter.GetOutput(JobId(args))));
            case "app.info":
                return new { version = typeof(Program).Assembly.GetName().Version?.ToString(3) };
            default:
                throw new ArgumentException($"Unknown command '{method}'.");
        }
    }

    // Only re-accept URLs the resolver itself produces, so the UI can't be used to fetch arbitrary hosts.
    private static bool IsValid(AddJobRequest r) =>
        r.Video is not null
        && r.Title is { Length: <= 300 }
        && r.Quality is { Length: > 0 and <= 50 }
        && LinkDetector.Find(r.Video.Url ?? "", null).Any(v => v.Platform == r.Video.Platform && v.Id == r.Video.Id && v.Url == r.Video.Url)
        && (r.Video.Referer is null || LinkDetector.Origin(r.Video.Referer) is not null);

    private static string JobId(JsonElement args) => args.GetProperty("id").GetString() ?? throw new ArgumentException("Missing job id.");

    private static object? Ensure(bool done) => done ? null : throw new ArgumentException("That action is no longer available.");

    private void Post(object message)
    {
        if (closed) return;
        web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, AppJson.Options));
    }

    private void OnConversionChanged(string type, ConversionView item) => Dispatcher.InvokeAsync(() => Post(new { @event = type, data = item }));

    private void OnJobChanged(string type, JobView job) => Dispatcher.InvokeAsync(() =>
    {
        Post(new { @event = type, data = job });
        if (type == "job" && job.Status == JobStatus.Downloading) downloading[job.Id] = job;
        else downloading.Remove(job.Id);
        UpdateTaskbar();
    });

    private void UpdateTaskbar()
    {
        var total = downloading.Values.Sum(j => j.TotalBytes);
        TaskbarItemInfo.ProgressState = downloading.Count == 0 ? TaskbarItemProgressState.None
            : total == 0 ? TaskbarItemProgressState.Indeterminate
            : TaskbarItemProgressState.Normal;
        if (total > 0) TaskbarItemInfo.ProgressValue = (double)downloading.Values.Sum(j => j.DownloadedBytes) / total;
    }

    // Native chrome matches the UI's sidebar color so the title bar blends into the app (Windows 11).
    private void ApplyTheme()
    {
        var dark = settings.Current.Theme == "dark";
        var (chrome, text) = dark ? (0x0C0C0E, 0xEDEDF0) : (0xF4F4F6, 0x18181B);
        Background = new SolidColorBrush(Color.FromRgb((byte)(chrome >> 16), (byte)(chrome >> 8), (byte)chrome));
        web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(unchecked((int)0xFF000000) | chrome);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0) return;
        SetDwmAttribute(hwnd, 20, dark ? 1 : 0); // DWMWA_USE_IMMERSIVE_DARK_MODE
        SetDwmAttribute(hwnd, 34, ToColorRef(chrome)); // DWMWA_BORDER_COLOR
        SetDwmAttribute(hwnd, 35, ToColorRef(chrome)); // DWMWA_CAPTION_COLOR
        SetDwmAttribute(hwnd, 36, ToColorRef(text)); // DWMWA_TEXT_COLOR
    }

    private static int ToColorRef(int rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    private static void SetDwmAttribute(nint hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private static bool Reveal(string? path)
    {
        if (path is null || !File.Exists(path)) return false;
        Process.Start("explorer.exe", $"/select,\"{path}\"");
        return true;
    }

    private static bool Open(string? path)
    {
        if (path is null || !File.Exists(path)) return false;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
    }
}
