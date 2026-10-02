using System.Text.RegularExpressions;

namespace HadalStream.Settings;

public sealed record AppSettings
{
    public string DownloadDir { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int MaxConcurrentJobs { get; init; } = 2;
    public int Connections { get; init; } = 4;
    public string DefaultQuality { get; init; } = "high";
    public string? Proxy { get; init; }
    public Dictionary<string, string> Headers { get; init; } = [];
    public string Theme { get; init; } = "dark";
}

public sealed partial class SettingsStore
{
    private static readonly string[] Qualities = ["high", "medium", "low"];
    private static readonly string[] Themes = ["dark", "light"];
    private static readonly string[] ProxySchemes = ["http", "https", "socks4", "socks4a", "socks5"];
    private readonly string file;

    public SettingsStore(string dataDir)
    {
        file = Path.Combine(dataDir, "settings.json");
        Current = AppJson.Load<AppSettings>(file) is { } loaded && Validate(loaded) is null ? loaded : new();
    }

    public AppSettings Current { get; private set; }

    // Returns an error message, or null when saved.
    public string? Update(AppSettings settings)
    {
        if (Validate(settings) is { } error) return error;
        try
        {
            Directory.CreateDirectory(settings.DownloadDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Could not create the download folder: {e.Message}";
        }
        AppJson.Save(file, settings);
        Current = settings;
        return null;
    }

    internal static string? Validate(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.DownloadDir) || !Path.IsPathFullyQualified(s.DownloadDir))
            return "Download folder must be a full path, e.g. C:\\Users\\you\\Downloads.";
        if (s.MaxConcurrentJobs is < 1 or > 5) return "Simultaneous downloads must be between 1 and 5.";
        if (s.Connections is < 1 or > 16) return "Connections per download must be between 1 and 16.";
        if (!Qualities.Contains(s.DefaultQuality)) return "Invalid default quality.";
        if (!Themes.Contains(s.Theme)) return "Invalid theme.";
        if (!string.IsNullOrWhiteSpace(s.Proxy) &&
            !(Uri.TryCreate(s.Proxy, UriKind.Absolute, out var proxy) && ProxySchemes.Contains(proxy.Scheme)))
            return "Proxy must look like http://host:port or socks5://host:port.";
        if (s.Headers is null || s.Headers.Count > 30) return "Up to 30 custom headers are allowed.";
        foreach (var (name, value) in s.Headers)
        {
            if (!HeaderName().IsMatch(name)) return $"Invalid header name: {name}";
            if (value is null || value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0) return $"Invalid header value for {name}.";
        }
        return null;
    }

    [GeneratedRegex(@"^[!#$%&'*+.^_`|~0-9A-Za-z-]{1,100}$")]
    private static partial Regex HeaderName();
}
