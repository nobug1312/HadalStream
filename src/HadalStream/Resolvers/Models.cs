namespace HadalStream.Resolvers;

public enum Platform { Abyss, Dood }

public sealed record VideoRef(Platform Platform, string Id, string Url, string? Referer);

public sealed record Variant(string Label, long? Size);

public sealed record ResolveResult(string Input, VideoRef? Video, string? Title, IReadOnlyList<Variant> Variants, string? Error);

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
