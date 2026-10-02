using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HadalStream.Crypto;

namespace HadalStream.Resolvers;

// Protocol ported from upstream AbyssVideoDownloader master (abdlhay), not the older local copy.
public static partial class AbyssResolver
{
    internal sealed record Source(string Label, long Size, string ResId, string Sub);

    internal sealed record Media(string Title, string Md5Id, string Domain, Source[] Sources);

    public static async Task<(string Title, Variant[] Variants)> DescribeAsync(HttpClientProvider http, VideoRef video, CancellationToken ct)
    {
        var media = await FetchAsync(http, video, ct);
        return (media.Title, [.. media.Sources.OrderByDescending(s => s.Size).Select(s => new Variant(s.Label, s.Size))]);
    }

    public static async Task<DownloadPlan> PlanAsync(HttpClientProvider http, VideoRef video, string quality, CancellationToken ct)
    {
        var media = await FetchAsync(http, video, ct);
        var source = media.Sources.FirstOrDefault(s => s.Label == quality)
            ?? throw new InvalidOperationException($"Quality {quality} is no longer available.");
        var host = $"https://{source.Sub}.{media.Domain[(media.Domain.IndexOf('.') + 1)..]}";
        const long segmentSize = DownloadPlan.DefaultSegmentSize;
        var count = (int)((source.Size + segmentSize - 1) / segmentSize);
        return new(media.Title, source.Size, segmentSize, count, true, i =>
        {
            var token = AbyssCrypto.SegmentToken($"/mp4/{media.Md5Id}/{source.ResId}/{source.Size}/{segmentSize}/{i}", source.Size);
            var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/sora/{source.Size}/{token}");
            request.Headers.Referrer = new Uri("https://abysscdn.com/");
            return request;
        });
    }

    private static async Task<Media> FetchAsync(HttpClientProvider http, VideoRef video, CancellationToken ct)
    {
        using var request = http.Page(video.Url, video.Referer);
        using var response = await http.Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    internal static Media Parse(string html)
    {
        var match = Datas().Match(html);
        if (!match.Success)
            throw new InvalidOperationException("Video data not found. The video may have been removed, or Abyss changed its player.");
        try
        {
            var encoded = match.Groups[1].Value;
            var datas = JsonNode.Parse(Encoding.Latin1.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))))!;
            var md5Id = datas["md5_id"]!.ToString();
            var key = AbyssCrypto.Md5Hex($"{datas["user_id"]}:{datas["slug"]}:{md5Id}");
            var mp4 = JsonNode.Parse(AbyssCrypto.DecryptMedia(datas["media"]!.GetValue<string>(), key))!["mp4"]!;
            // Labels can repeat (e.g. two 1080p encodes); the label doubles as the quality key, so make it unique.
            var seen = new Dictionary<string, int>();
            Source[] sources =
            [
                .. mp4["sources"]!.AsArray().Select(s =>
                {
                    var label = s!["label"]!.GetValue<string>();
                    var n = seen[label] = seen.GetValueOrDefault(label) + 1;
                    return new Source(n == 1 ? label : $"{label} ({n})", s["size"]!.GetValue<long>(), s["res_id"]!.ToString(), s["sub"]!.GetValue<string>());
                }),
            ];
            var title = WebUtility.HtmlDecode(Title().Match(html).Groups[1].Value).Trim();
            if (title.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) title = title[..^4];
            return new(title, md5Id, mp4["domains"]![0]!.GetValue<string>(), sources);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or NullReferenceException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("Could not decode the Abyss video data (the player may have changed).", e);
        }
    }

    [GeneratedRegex(@"const\s+datas\s*=\s*""([^""]*)""")]
    private static partial Regex Datas();

    [GeneratedRegex(@"<title>([^<]*)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex Title();
}
