using HadalStream.Domain.Common;
using HadalStream.Domain.Downloads;
using HadalStream.Domain.Videos;

namespace HadalStream.Application.Abstractions;

public sealed record NewDownload(VideoRef Video, string Title, string Quality);

public sealed record DownloadView(
    string Id, Platform Platform, string VideoId, string Title, string Quality, DownloadStatus Status, string? Error,
    string? FilePath, long TotalBytes, long DownloadedBytes, double Speed, DateTimeOffset CreatedAt)
{
    public static DownloadView From(DownloadJob job, double speed) => new(
        job.Id, job.Video.Platform, job.Video.Id, job.Title, job.Quality, job.Status, job.Error, job.FilePath,
        job.TotalBytes, job.DownloadedBytes, job.Status == DownloadStatus.Downloading ? speed : 0, job.CreatedAt);
}

// Events are raised from background threads.
public interface IDownloadQueue
{
    event Action<DownloadView>? Changed;
    event Action<DownloadView>? Removed;

    DownloadView[] List();
    DownloadView[] Add(IEnumerable<NewDownload> items);
    Result Pause(string id);
    Result Resume(string id);
    Result Cancel(string id);
    Result Remove(string id);
    Result<string> CompletedFile(string id);
    Result<string> OutputFolder(string id);
    Task ShutdownAsync();
}
