using HadalStream.Application.Abstractions;
using HadalStream.Domain.Common;
using HadalStream.Domain.Videos;
using MediatR;

namespace HadalStream.Application.Links;

public sealed record LinkResult(string Input, VideoRef? Video, string? Title, Variant[] Variants, string? Error);

// Pasted text: links, embed URLs or bare Abyss IDs, one per line. Each line gets its own result or error.
public sealed record ResolveLinks(string Input) : IRequest<Result<LinkResult[]>>;

internal sealed class ResolveLinksHandler(IWebPageReader pages, IEnumerable<IVideoSource> sources, IErrorLog log)
    : IRequestHandler<ResolveLinks, Result<LinkResult[]>>
{
    private const int MaxLines = 50;

    // 50 pasted pages x N embeds each would otherwise hit the sites all at once. Only leaf requests hold a slot.
    private static readonly SemaphoreSlim Throttle = new(6);

    public async Task<Result<LinkResult[]>> Handle(ResolveLinks request, CancellationToken ct)
    {
        if (request.Input is not { Length: > 0 and <= 20_000 }) return new Error("Paste up to 20,000 characters.");
        var lines = request.Input.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Distinct().Take(MaxLines);
        var results = await Task.WhenAll(lines.Select(line => ResolveLineAsync(line, ct)));
        return results.SelectMany(r => r).ToArray();
    }

    private async Task<LinkResult[]> ResolveLineAsync(string line, CancellationToken ct)
    {
        var videos = LinkDetector.Detect(line);
        if (videos.Count == 0)
        {
            if (LinkDetector.Origin(line) is not { } origin) return [Failed(line, "Not a link or Abyss ID.")];
            try
            {
                // ponytail: only static HTML is scanned; players injected by JS need the embed link pasted directly.
                videos = LinkDetector.Find(await Throttled(() => pages.ReadAsync(line, null, ct), ct), origin);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                log.Write($"Could not open {line}", e.ToString());
                return [Failed(line, e.Message)];
            }
            if (videos.Count == 0) return [Failed(line, "No supported player here.")];
        }
        return await Task.WhenAll(videos.Select(video => DescribeAsync(line, video, ct)));
    }

    private async Task<LinkResult> DescribeAsync(string input, VideoRef video, CancellationToken ct)
    {
        try
        {
            var source = sources.First(s => s.Platform == video.Platform);
            var info = await Throttled(() => source.DescribeAsync(video, ct), ct);
            return new(input, video, info.Title, info.Variants, null);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.Write($"Could not analyze {input}", e.ToString());
            return new(input, video, null, [], e.Message);
        }
    }

    private static LinkResult Failed(string input, string error) => new(input, null, null, [], error);

    private static async Task<T> Throttled<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await Throttle.WaitAsync(ct);
        try
        {
            return await work();
        }
        finally
        {
            Throttle.Release();
        }
    }
}
