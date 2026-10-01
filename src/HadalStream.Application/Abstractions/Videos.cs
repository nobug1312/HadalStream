using HadalStream.Domain.Videos;

namespace HadalStream.Application.Abstractions;

public sealed record VideoInfo(string Title, Variant[] Variants);

// Segment i covers bytes [i * SegmentSize, min((i + 1) * SegmentSize, TotalSize)) of the output file.
public sealed record DownloadPlan(
    string Title,
    long? TotalSize,
    long SegmentSize,
    int SegmentCount,
    bool Resumable,
    Func<int, HttpRequestMessage> CreateRequest)
{
    public const long DefaultSegmentSize = 2_097_152;
}

public interface IVideoSource
{
    Platform Platform { get; }

    Task<VideoInfo> DescribeAsync(VideoRef video, CancellationToken ct);

    Task<DownloadPlan> PlanAsync(VideoRef video, string quality, CancellationToken ct);
}

public interface IWebPageReader
{
    Task<string> ReadAsync(string url, string? referer, CancellationToken ct);
}
