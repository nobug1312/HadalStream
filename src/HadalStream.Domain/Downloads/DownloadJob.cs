using HadalStream.Domain.Common;
using HadalStream.Domain.Videos;

namespace HadalStream.Domain.Downloads;

public enum DownloadStatus { Queued, Downloading, Paused, Completed, Failed, Canceled }

public static class DownloadErrors
{
    public static readonly Error NotFound = new("Download not found.");
    public static readonly Error Unavailable = new("Not available right now.");
}

// The constructor rehydrates a saved job; new jobs come from Create.
public sealed class DownloadJob(
    string id, VideoRef video, string title, string quality, string outputDir, DownloadStatus status, string? error,
    string? filePath, long totalBytes, int segmentCount, DateTimeOffset createdAt, long downloadedBytes, int[]? doneSegments)
{
    private readonly HashSet<int> done = [.. doneSegments ?? []];
    private long downloaded = downloadedBytes;

    public string Id { get; } = id;
    public VideoRef Video { get; } = video;
    public string Title { get; } = title;
    public string Quality { get; } = quality;
    public string OutputDir { get; } = outputDir;
    public DownloadStatus Status { get; private set; } = status;
    public string? Error { get; private set; } = error;
    public string? FilePath { get; private set; } = filePath;
    public long TotalBytes { get; private set; } = totalBytes;
    public int SegmentCount { get; private set; } = segmentCount;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public long DownloadedBytes => Interlocked.Read(ref downloaded);
    public int[] DoneSegments { get { lock (done) return [.. done]; } }

    private bool IsPending => Status is DownloadStatus.Queued or DownloadStatus.Downloading;

    public static DownloadJob Create(VideoRef video, string title, string quality, string outputDir) =>
        new(Guid.NewGuid().ToString("N")[..12], video, title.Trim(), quality, outputDir,
            DownloadStatus.Queued, null, null, 0, 0, DateTimeOffset.Now, 0, null);

    public Result Pause()
    {
        if (!IsPending) return DownloadErrors.Unavailable;
        Status = DownloadStatus.Paused;
        return Result.Success();
    }

    public Result Resume()
    {
        if (Status is not (DownloadStatus.Paused or DownloadStatus.Failed or DownloadStatus.Canceled)) return DownloadErrors.Unavailable;
        Status = DownloadStatus.Queued;
        Error = null;
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status is DownloadStatus.Completed or DownloadStatus.Canceled) return DownloadErrors.Unavailable;
        Status = DownloadStatus.Canceled;
        ResetSegments();
        return Result.Success();
    }

    // Shutdown or restart: running work pauses and later resumes from the saved segments.
    public void Interrupt()
    {
        if (IsPending) Status = DownloadStatus.Paused;
    }

    public void Start() => Status = DownloadStatus.Downloading;

    // False when saved progress no longer matches the remote file, so the part file must be discarded.
    public bool Prepare(long? totalSize, int segmentCount, long segmentSize, bool resumable)
    {
        var keep = resumable && TotalBytes == (totalSize ?? 0) && SegmentCount == segmentCount;
        if (!keep) ResetSegments();
        TotalBytes = totalSize ?? 0;
        SegmentCount = segmentCount;
        Interlocked.Exchange(ref downloaded, totalSize is long total ? DoneSegments.Sum(i => Math.Min(segmentSize, total - i * segmentSize)) : 0);
        return keep;
    }

    public void AddBytes(long count) => Interlocked.Add(ref downloaded, count);

    public void MarkDone(int segment)
    {
        lock (done) done.Add(segment);
    }

    public void Complete(string filePath)
    {
        FilePath = filePath;
        Status = DownloadStatus.Completed;
        ResetSegments();
        Interlocked.Exchange(ref downloaded, TotalBytes);
    }

    public void Fail(string error)
    {
        Status = DownloadStatus.Failed;
        Error = error;
    }

    private void ResetSegments()
    {
        lock (done) done.Clear();
        Interlocked.Exchange(ref downloaded, 0);
    }
}
