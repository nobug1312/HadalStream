using HadalStream.Domain.Common;
using HadalStream.Domain.Conversions;

namespace HadalStream.Application.Abstractions;

public sealed record ConversionView(
    string Id, string FileName, string Source, string? Output, ConversionStatus Status, string? Error,
    bool? Lossless, string? Details, double Progress, double Speed, double Duration)
{
    public static ConversionView From(ConversionJob job) => new(
        job.Id, Path.GetFileName(job.Source), job.Source, job.Output, job.Status, job.Error,
        job.Lossless, job.Details, job.Progress, job.Speed, job.Duration);
}

// Events are raised from background threads.
public interface IConversionQueue
{
    event Action<ConversionView>? Changed;
    event Action<ConversionView>? Removed;

    ConversionView[] List();
    ConversionView[] Add(IEnumerable<string> files);
    Result Cancel(string id);
    Result Retry(string id);
    Result Remove(string id);
    Result<string> Output(string id);
    Task ShutdownAsync();
}
