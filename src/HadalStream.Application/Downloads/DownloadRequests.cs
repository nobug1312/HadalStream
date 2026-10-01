using HadalStream.Application.Abstractions;
using HadalStream.Domain.Common;
using HadalStream.Domain.Videos;
using MediatR;

namespace HadalStream.Application.Downloads;

public sealed record ListDownloads : IRequest<Result<DownloadView[]>>;

public sealed record AddDownloads(NewDownload[] Items) : IRequest<Result<DownloadView[]>>;

public sealed record PauseDownload(string Id) : IRequest<Result>;

public sealed record ResumeDownload(string Id) : IRequest<Result>;

public sealed record CancelDownload(string Id) : IRequest<Result>;

public sealed record RemoveDownload(string Id) : IRequest<Result>;

public sealed record OpenDownload(string Id) : IRequest<Result>;

public sealed record RevealDownload(string Id) : IRequest<Result>;

public sealed record RevealDownloadLog(string Id) : IRequest<Result>;

internal sealed class DownloadHandlers(IDownloadQueue queue, IShell shell, IErrorLog log) :
    IRequestHandler<ListDownloads, Result<DownloadView[]>>,
    IRequestHandler<AddDownloads, Result<DownloadView[]>>,
    IRequestHandler<PauseDownload, Result>,
    IRequestHandler<ResumeDownload, Result>,
    IRequestHandler<CancelDownload, Result>,
    IRequestHandler<RemoveDownload, Result>,
    IRequestHandler<OpenDownload, Result>,
    IRequestHandler<RevealDownload, Result>,
    IRequestHandler<RevealDownloadLog, Result>
{
    public Task<Result<DownloadView[]>> Handle(ListDownloads request, CancellationToken ct) => Views(queue.List());

    public Task<Result<DownloadView[]>> Handle(AddDownloads request, CancellationToken ct) =>
        Views(request.Items is { Length: > 0 and <= 100 } items && items.All(IsValid)
            ? queue.Add(request.Items)
            : new Error("Invalid download."));

    public Task<Result> Handle(PauseDownload request, CancellationToken ct) => Done(queue.Pause(request.Id));

    public Task<Result> Handle(ResumeDownload request, CancellationToken ct) => Done(queue.Resume(request.Id));

    public Task<Result> Handle(CancelDownload request, CancellationToken ct) => Done(queue.Cancel(request.Id));

    public Task<Result> Handle(RemoveDownload request, CancellationToken ct) => Done(queue.Remove(request.Id));

    public Task<Result> Handle(OpenDownload request, CancellationToken ct) => Done(queue.CompletedFile(request.Id).Bind(shell.Open));

    public Task<Result> Handle(RevealDownload request, CancellationToken ct) => Done(queue.CompletedFile(request.Id).Bind(shell.Reveal));

    public Task<Result> Handle(RevealDownloadLog request, CancellationToken ct) =>
        Done(queue.OutputFolder(request.Id).Bind(folder => shell.Reveal(log.FileIn(folder))));

    // Only links the detector itself produced are accepted, so the UI can't point the app at arbitrary hosts.
    private static bool IsValid(NewDownload item) =>
        item is { Video: { } video, Title.Length: <= 300, Quality.Length: > 0 and <= 50 } && LinkDetector.IsGenuine(video);

    private static Task<Result<DownloadView[]>> Views(Result<DownloadView[]> result) => Task.FromResult(result);

    private static Task<Result> Done(Result result) => Task.FromResult(result);
}
