using HadalStream.Settings;

namespace HadalStream.Resolvers;

public sealed class VideoResolver(HttpClientProvider http, SettingsStore settings)
{
    private const int MaxLines = 50;

    // 50 pasted pages x N embeds each would otherwise hit the sites all at once. Only leaf requests hold a slot.
    private readonly SemaphoreSlim throttle = new(6);

    public async Task<ResolveResult[]> ResolveAsync(string input, CancellationToken ct)
    {
        var lines = input.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Distinct().Take(MaxLines);
        var results = await Task.WhenAll(lines.Select(line => ResolveLineAsync(line, ct)));
        return [.. results.SelectMany(r => r)];
    }

    public Task<DownloadPlan> PlanAsync(VideoRef video, string quality, CancellationToken ct) => video.Platform switch
    {
        Platform.Abyss => AbyssResolver.PlanAsync(http, video, quality, ct),
        _ => DoodResolver.PlanAsync(http, video, ct),
    };

    private async Task<ResolveResult[]> ResolveLineAsync(string line, CancellationToken ct)
    {
        IReadOnlyList<VideoRef> videos;
        try
        {
            videos = LinkDetector.Detect(line);
            if (videos.Count == 0)
            {
                var origin = LinkDetector.Origin(line) ?? throw new ArgumentException("Not a valid link or Abyss ID.");
                var html = await Throttled(async () =>
                {
                    using var request = http.Page(line, null);
                    using var response = await http.Client.SendAsync(request, ct);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(ct);
                }, ct);
                // ponytail: only static HTML is scanned; players injected by JS/ajax need the embed link pasted directly.
                videos = LinkDetector.Find(html, origin);
                if (videos.Count == 0) throw new InvalidOperationException("No Abyss, HydraX or DoodStream player found on this page.");
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Log(line, e);
            return [new(line, null, null, [], e.Message)];
        }
        return await Task.WhenAll(videos.Select(video => DescribeAsync(line, video, ct)));
    }

    private async Task<ResolveResult> DescribeAsync(string input, VideoRef video, CancellationToken ct)
    {
        try
        {
            var (title, variants) = await Throttled(() => video.Platform == Platform.Abyss
                ? AbyssResolver.DescribeAsync(http, video, ct)
                : DoodResolver.DescribeAsync(http, video, ct), ct);
            return new(input, video, title, variants, null);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Log(input, e);
            return new(input, video, null, [], e.Message);
        }
    }

    private async Task<T> Throttled<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await throttle.WaitAsync(ct);
        try
        {
            return await work();
        }
        finally
        {
            throttle.Release();
        }
    }

    private void Log(string input, Exception e)
    {
        if (e is not ArgumentException) ErrorLog.Write(settings.Current.DownloadDir, $"Could not analyze {input}", e.ToString());
    }
}
