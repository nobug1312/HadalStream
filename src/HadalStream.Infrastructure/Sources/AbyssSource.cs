using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HadalStream.Application.Abstractions;
using HadalStream.Domain.Videos;
using HadalStream.Infrastructure.Http;

namespace HadalStream.Infrastructure.Sources;

// Protocol ported from upstream AbyssVideoDownloader master (abdlhay).
internal sealed partial class AbyssSource(HttpClientProvider http) : IVideoSource
{
    internal sealed record Source(string Label, long Size, string ResId, string Sub);

    internal sealed record Media(string Title, string Md5Id, string Domain, Source[] Sources);

    public Platform Platform => Platform.Abyss;

    public async Task<VideoInfo> DescribeAsync(VideoRef video, CancellationToken ct)
    {
        var media = Parse(await http.ReadAsync(video.Url, video.Referer, ct));
        return new(media.Title, [.. media.Sources.OrderByDescending(s => s.Size).Select(s => new Variant(s.Label, s.Size))]);
    }

    public async Task<DownloadPlan> PlanAsync(VideoRef video, string quality, CancellationToken ct)
    {
        var media = Parse(await http.ReadAsync(video.Url, video.Referer, ct));
        var source = media.Sources.FirstOrDefault(s => s.Label == quality)
            ?? throw new InvalidOperationException($"{quality} is no longer available.");
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

    internal static Media Parse(string html)
    {
        var match = Datas().Match(html);
        if (!match.Success) throw new InvalidOperationException("Video not found or removed.");
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
            throw new InvalidOperationException("Could not read Abyss data.", e);
        }
    }

    [GeneratedRegex(@"const\s+datas\s*=\s*""([^""]*)""")]
    private static partial Regex Datas();

    [GeneratedRegex(@"<title>([^<]*)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex Title();
}
