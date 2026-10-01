using HadalStream.Domain.Common;
using HadalStream.Domain.Settings;
using HadalStream.Domain.Videos;
using HadalStream.Infrastructure;
using HadalStream.Infrastructure.Sources;

namespace HadalStream.Tests;

public class ParsingTests
{
    [Theory]
    [InlineData("K8R6OOjS7", Platform.Abyss, "K8R6OOjS7", null)]
    [InlineData("https://abysscdn.com/?v=K8R6OOjS7", Platform.Abyss, "K8R6OOjS7", "https://abysscdn.com/")]
    [InlineData("https://playhydrax.com/?v=Abc_12-x", Platform.Abyss, "Abc_12-x", "https://playhydrax.com/")]
    [InlineData("https://zplayer.io/?foo=1&v=XyZ123", Platform.Abyss, "XyZ123", "https://zplayer.io/")]
    [InlineData("https://short.ink/K8R6OOjS7", Platform.Abyss, "K8R6OOjS7", "https://short.ink/")]
    [InlineData("https://dood.wf/d/abc123def456", Platform.Dood, "abc123def456", null)]
    [InlineData("https://D000D.com/e/xyz789", Platform.Dood, "xyz789", null)]
    [InlineData("https://myvidplay.com/e/q1w2e3", Platform.Dood, "q1w2e3", null)]
    public void Detect_RecognisesSupportedInputs(string input, Platform platform, string id, string? referer)
    {
        var video = Assert.Single(LinkDetector.Detect(input));
        Assert.Equal((platform, id, referer), (video.Platform, video.Id, video.Referer));
    }

    [Fact]
    public void Detect_DoodUrlIsNormalisedToEmbed() =>
        Assert.Equal("https://d000d.com/e/xyz789", Assert.Single(LinkDetector.Detect("https://D000D.com/d/xyz789")).Url);

    [Fact]
    public void Detect_UnknownPageNeedsScan() => Assert.Empty(LinkDetector.Detect("https://example.com/watch/123"));

    [Fact]
    public void Find_ScansHtmlForEmbedsAndDeduplicates()
    {
        const string html = """
            <iframe src="https://playhydrax.com/?v=AAA111"></iframe>
            <script>var u = "https:\/\/short.ink\/AAA111"; var d = 'https://dood.li/e/zzz999';</script>
            """;

        var found = LinkDetector.Find(html, "https://site.example/");

        Assert.Equal(new[] { (Platform.Abyss, "AAA111"), (Platform.Dood, "zzz999") }, found.Select(v => (v.Platform, v.Id)));
        Assert.Equal("https://site.example/", found[0].Referer);
    }

    [Fact]
    public void DoodParsePlayer_ExtractsPassPathAndToken()
    {
        const string html = """
            <script>$.get('/pass_md5/12345-67-89-1700000000-abcdef/xyz789', function(data){ dsplayer.src(makePlay() + data); });
            function makePlay(){ for(...) {} return a + "?token=tok3n123&expiry=" + Date.now(); }</script>
            """;

        Assert.Equal(("/pass_md5/12345-67-89-1700000000-abcdef/xyz789", "?token=tok3n123&expiry="), DoodSource.ParsePlayer(html));
    }

    [Theory]
    [InlineData("My: Video?", "My_ Video_")]
    [InlineData("../..", "_")]
    [InlineData("..", "video")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("CON", "_CON")]
    [InlineData("  name.  ", "name")]
    public void Sanitize_ProducesSafeFileName(string input, string expected) => Assert.Equal(expected, FileName.Sanitize(input));

    [Fact]
    public void Unique_AppendsCounterWhenTaken()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "v.mp4"), "");
        Assert.Equal(Path.Combine(dir, "v (1).mp4"), FilePaths.Unique(dir, "v", ".mp4"));
    }

    [Fact]
    public void SettingsValidate_RejectsHeaderInjectionAndBadValues()
    {
        var ok = new AppSettings { DownloadDir = Path.GetTempPath() };
        Assert.True(ok.Validate().IsSuccess);
        Assert.False((ok with { Headers = new() { ["X-A"] = "1\r\nX-B: 2" } }).Validate().IsSuccess);
        Assert.False((ok with { Headers = new() { ["Bad Name"] = "1" } }).Validate().IsSuccess);
        Assert.False((ok with { DownloadDir = "relative/dir" }).Validate().IsSuccess);
        Assert.False((ok with { Connections = 0 }).Validate().IsSuccess);
        Assert.False((ok with { Proxy = "ftp://x:1" }).Validate().IsSuccess);
        Assert.True((ok with { Proxy = "socks5://127.0.0.1:1080" }).Validate().IsSuccess);
    }
}
