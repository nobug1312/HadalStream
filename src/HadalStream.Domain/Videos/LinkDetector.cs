using System.Text.RegularExpressions;

namespace HadalStream.Domain.Videos;

public static partial class LinkDetector
{
    [GeneratedRegex(@"https?://(?:www\.)?(?:abysscdn\.com|playhydrax\.com|zplayer\.io)/[^\s""'<>]*?[?&](?:amp;)?v=([\w-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AbyssUrl();

    [GeneratedRegex(@"https?://short\.ink/([\w@.-]*[\w@-])", RegexOptions.IgnoreCase)]
    private static partial Regex ShortInk();

    // Domain family taken from ResolveURL's DoodStream plugin; Dood rotates domains often.
    [GeneratedRegex(@"https?://(?:www\.)?((?:do*0*o*0*ds?(?:tream|ter|cdn)?|ds[2v](?:play|video)|(?:my)?v*id(?:pla?y|e0)|all3do|d-s|do(?:7go|ply)|playmogo)\.(?:[cit]om?|watch|s[ho]|cx|l[ai]|w[sf]|pm|re|yt|stream|pro|work|net))/[de]/([0-9a-zA-Z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DoodUrl();

    [GeneratedRegex(@"^[\w-]{5,40}$")]
    private static partial Regex BareId();

    // A bare ID is treated as Abyss; Dood needs a full URL.
    public static IReadOnlyList<VideoRef> Detect(string input)
    {
        input = input.Trim();
        return BareId().IsMatch(input) ? [Abyss(input, null)] : Find(input, Origin(input));
    }

    public static IReadOnlyList<VideoRef> Find(string text, string? referer)
    {
        text = text.Replace(@"\/", "/");
        IEnumerable<VideoRef> found =
        [
            .. AbyssUrl().Matches(text).Select(m => Abyss(m.Groups[1].Value, referer)),
            .. ShortInk().Matches(text).Select(m => Abyss(m.Groups[1].Value, referer)),
            .. DoodUrl().Matches(text).Select(m => new VideoRef(
                Platform.Dood, m.Groups[2].Value, $"https://{m.Groups[1].Value.ToLowerInvariant()}/e/{m.Groups[2].Value}", null)),
        ];
        return [.. found.DistinctBy(v => (v.Platform, v.Id))];
    }

    public static string? Origin(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsHttp(uri) ? $"{uri.Scheme}://{uri.Authority}/" : null;

    public static bool IsHttp(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    // Only URLs the detector itself produces are accepted back, so callers can't make the app fetch arbitrary hosts.
    public static bool IsGenuine(VideoRef video) =>
        Find(video.Url ?? "", null).Any(v => v.Platform == video.Platform && v.Id == video.Id && v.Url == video.Url)
        && (video.Referer is null || Origin(video.Referer) is not null);

    private static VideoRef Abyss(string id, string? referer) => new(Platform.Abyss, id, $"https://abysscdn.com/?v={id}", referer);
}
