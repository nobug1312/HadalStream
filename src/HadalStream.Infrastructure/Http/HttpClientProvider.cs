using System.Net;
using HadalStream.Application.Abstractions;

namespace HadalStream.Infrastructure.Http;

internal sealed class HttpClientProvider(ISettingsStore settings) : IWebPageReader
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

    private readonly Lock gate = new();
    private HttpClient? client;
    private string? proxy;

    // ponytail: a proxy change builds a new client and leaves the old one to in-flight downloads (never disposed).
    public HttpClient Client
    {
        get
        {
            lock (gate)
            {
                var current = settings.Current.Proxy;
                if (client is null || proxy != current)
                {
                    client = Create(current);
                    proxy = current;
                }
                return client;
            }
        }
    }

    // Page/metadata request: carries the user's custom headers. CDN segment requests must not.
    public HttpRequestMessage Page(string url, string? referer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (referer is not null) request.Headers.Referrer = new Uri(referer);
        foreach (var (name, value) in settings.Current.Headers)
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
        return request;
    }

    public async Task<string> ReadAsync(string url, string? referer, CancellationToken ct)
    {
        using var request = Page(url, referer);
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static HttpClient Create(string? proxy)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // Null keeps the system proxy.
            Proxy = string.IsNullOrWhiteSpace(proxy) ? null : new WebProxy(proxy),
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }
}
