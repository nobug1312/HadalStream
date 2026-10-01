using System.Text.RegularExpressions;
using HadalStream.Domain.Common;

namespace HadalStream.Domain.Settings;

public sealed partial record AppSettings
{
    private static readonly string[] Qualities = ["high", "medium", "low"];
    private static readonly string[] Themes = ["dark", "light"];
    private static readonly string[] ProxySchemes = ["http", "https", "socks4", "socks4a", "socks5"];

    public string DownloadDir { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int MaxConcurrentJobs { get; init; } = 2;
    public int Connections { get; init; } = 4;
    public string DefaultQuality { get; init; } = "high";
    public string? Proxy { get; init; }
    public Dictionary<string, string> Headers { get; init; } = [];
    public string Theme { get; init; } = "light";

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(DownloadDir) || !Path.IsPathFullyQualified(DownloadDir)) return new Error("Choose a full folder path.");
        if (MaxConcurrentJobs is < 1 or > 5) return new Error("Downloads at once: 1 to 5.");
        if (Connections is < 1 or > 16) return new Error("Connections: 1 to 16.");
        if (!Qualities.Contains(DefaultQuality)) return new Error("Unknown quality.");
        if (!Themes.Contains(Theme)) return new Error("Unknown theme.");
        if (!string.IsNullOrWhiteSpace(Proxy) && !(Uri.TryCreate(Proxy, UriKind.Absolute, out var proxy) && ProxySchemes.Contains(proxy.Scheme)))
            return new Error("Proxy must look like http://host:port.");
        if (Headers is null || Headers.Count > 30) return new Error("Up to 30 headers.");
        foreach (var (name, value) in Headers)
        {
            if (!HeaderName().IsMatch(name)) return new Error($"Bad header name: {name}");
            if (value is null || value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0) return new Error($"Bad header value: {name}");
        }
        return Result.Success();
    }

    [GeneratedRegex(@"^[!#$%&'*+.^_`|~0-9A-Za-z-]{1,100}$")]
    private static partial Regex HeaderName();
}
