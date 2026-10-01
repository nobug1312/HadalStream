using System.Text;
using HadalStream.Application;
using HadalStream.Application.Abstractions;
using HadalStream.Application.Links;
using HadalStream.Infrastructure;
using HadalStream.Infrastructure.Http;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace HadalStream.Tests;

// Hits the real services. Run with: $env:HADAL_LIVE="1"; dotnet test --filter Category=Live
// Optional: HADAL_LIVE_ABYSS (ID or URL, default K8R6OOjS7), HADAL_LIVE_DOOD (Dood URL).
[Trait("Category", "Live")]
public class LiveTests(ITestOutputHelper output)
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("HADAL_LIVE") == "1";

    private static ServiceProvider Services() => new ServiceCollection()
        .AddApplication()
        .AddInfrastructure(new AppPaths(Directory.CreateTempSubdirectory().FullName, ""))
        .BuildServiceProvider();

    [Fact]
    public async Task Abyss_ResolvesAndFirstSegmentIsMp4() =>
        await CheckFirstSegment(Environment.GetEnvironmentVariable("HADAL_LIVE_ABYSS") ?? "K8R6OOjS7", expectFtyp: true);

    [Fact]
    public async Task Dood_ResolvesAndFirstSegmentDownloads()
    {
        var url = Environment.GetEnvironmentVariable("HADAL_LIVE_DOOD");
        if (url is null) return;
        await CheckFirstSegment(url, expectFtyp: false);
    }

    private async Task CheckFirstSegment(string input, bool expectFtyp)
    {
        if (!Enabled) return;
        using var services = Services();

        var result = Assert.Single((await services.GetRequiredService<ISender>().Send(new ResolveLinks(input))).Value);
        output.WriteLine($"{result.Video} title='{result.Title}' error={result.Error}");
        foreach (var v in result.Variants) output.WriteLine($"  {v.Label}: {v.Size}");
        Assert.Null(result.Error);

        var smallest = result.Variants.OrderBy(v => v.Size).First();
        var source = services.GetServices<IVideoSource>().First(s => s.Platform == result.Video!.Platform);
        var plan = await source.PlanAsync(result.Video!, smallest.Label, CancellationToken.None);
        output.WriteLine($"plan: size={plan.TotalSize} segments={plan.SegmentCount} resumable={plan.Resumable}");

        using var request = plan.CreateRequest(0);
        output.WriteLine($"segment 0: {request.RequestUri}");
        using var response = await services.GetRequiredService<HttpClientProvider>().Client.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        output.WriteLine($"status={(int)response.StatusCode} length={bytes.Length} head={Convert.ToHexString(bytes.AsSpan(0, Math.Min(16, bytes.Length)))}");

        response.EnsureSuccessStatusCode();
        Assert.Equal(Math.Min(plan.SegmentSize, plan.TotalSize ?? bytes.Length), bytes.Length);
        if (expectFtyp) Assert.Equal("ftyp", Encoding.ASCII.GetString(bytes, 4, 4));
    }
}
