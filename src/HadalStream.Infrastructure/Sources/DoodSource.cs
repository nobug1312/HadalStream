using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HadalStream.Application.Abstractions;
using HadalStream.Domain.Videos;
using HadalStream.Infrastructure.Http;

namespace HadalStream.Infrastructure.Sources;

// Flow mirrors ResolveURL's DoodStream plugin: embed page -> /pass_md5 -> base URL + random suffix + token.
internal sealed partial class DoodSource(HttpClientProvider http) : IVideoSource
{
    private sealed record Direct(string Title, string Url, string Referer);

    public Platform Platform => Platform.Dood;

    public async Task<VideoInfo> DescribeAsync(VideoRef video, CancellationToken ct)
    {
        var direct = await FetchAsync(video, ct);
        var (_, size) = await ProbeAsync(direct, ct);
        return new(direct.Title, [new Variant("Original", size)]);
    }

    public async Task<DownloadPlan> PlanAsync(VideoRef video, string quality, CancellationToken ct)
    {
        var direct = await FetchAsync(video, ct);
        var (ranges, size) = await ProbeAsync(direct, ct);
        if (ranges && size is long total)
        {
            const long segmentSize = DownloadPlan.DefaultSegmentSize;
            return new(direct.Title, total, segmentSize, (int)((total + segmentSize - 1) / segmentSize), true,
                i => Request(direct, i * segmentSize, Math.Min(total, (i + 1) * segmentSize) - 1));
        }
        return new(direct.Title, size, size ?? long.MaxValue, 1, false, _ => Request(direct, null, null));
    }

    internal static (string PassPath, string TokenQuery) ParsePlayer(string html)
    {
        var pass = PassMd5().Match(html);
        var token = Token().Match(html);
        if (!pass.Success || !token.Success) throw new InvalidOperationException("Could not read the Dood player.");
        return (pass.Value, token.Groups[1].Value);
    }

    private async Task<Direct> FetchAsync(VideoRef video, CancellationToken ct)
    {
        using var pageRequest = http.Page(video.Url, video.Url);
        using var page = await http.Client.SendAsync(pageRequest, ct);
        page.EnsureSuccessStatusCode();
        var embed = page.RequestMessage!.RequestUri!;
        var html = await page.Content.ReadAsStringAsync(ct);
        var (passPath, tokenQuery) = ParsePlayer(html);

        var baseUrl = (await http.ReadAsync(new Uri(embed, passPath).ToString(), embed.ToString(), ct)).Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || !LinkDetector.IsHttp(baseUri))
            throw new InvalidOperationException("Dood is busy. Try again later.");

        var url = baseUrl.Contains("cloudflarestorage.")
            ? baseUrl
            : baseUrl + RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 10)
                + tokenQuery + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var title = WebUtility.HtmlDecode(Title().Match(html).Groups[1].Value).Replace(" - DoodStream", "").Trim();
        return new(title, url, embed.ToString());
    }

    private async Task<(bool Ranges, long? Size)> ProbeAsync(Direct direct, CancellationToken ct)
    {
        using var request = Request(direct, 0, 0);
        using var response = await http.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.Length is long length
            ? (true, length)
            : (false, response.Content.Headers.ContentLength);
    }

    private static HttpRequestMessage Request(Direct direct, long? from, long? to)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, direct.Url);
        request.Headers.Referrer = new Uri(direct.Referer);
        if (from is not null) request.Headers.Range = new RangeHeaderValue(from, to);
        return request;
    }

    [GeneratedRegex(@"/pass_md5/[^'""\s]+")]
    private static partial Regex PassMd5();

    [GeneratedRegex(@"function\s*makePlay[\s\S]+?return[^?]+(\?token=[^""']+)")]
    private static partial Regex Token();

    [GeneratedRegex(@"<title>([^<]*)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex Title();
}
