using HadalStream.Domain.Common;

namespace HadalStream.Domain.Conversions;

public enum ConversionStatus { Queued, Converting, Completed, Failed, Canceled }

public static class ConversionErrors
{
    public static readonly Error NotFound = new("Conversion not found.");
    public static readonly Error Unavailable = new("Not available right now.");
    public static readonly Error Unsupported = new("Only FLV, MOV or MKV files.");
    public static readonly Error NoMedia = new("No video or audio found.");
}

public sealed class ConversionJob(string source)
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public string Source { get; } = source;
    public string? Output { get; private set; }
    public ConversionStatus Status { get; private set; }
    public string? Error { get; private set; }
    public bool? Lossless { get; private set; }
    public string? Details { get; private set; }
    public double Progress { get; private set; }
    public double Speed { get; private set; }
    public double Duration { get; private set; }

    public bool IsPending => Status is ConversionStatus.Queued or ConversionStatus.Converting;

    public Result Cancel()
    {
        if (!IsPending) return ConversionErrors.Unavailable;
        Status = ConversionStatus.Canceled;
        return Result.Success();
    }

    public Result Retry()
    {
        if (Status is not (ConversionStatus.Failed or ConversionStatus.Canceled)) return ConversionErrors.Unavailable;
        Status = ConversionStatus.Queued;
        Error = null;
        Progress = 0;
        return Result.Success();
    }

    public void Start() => Status = ConversionStatus.Converting;

    public void Planned(IReadOnlyList<PlannedStream> plan, TimeSpan? duration)
    {
        Lossless = Mp4Plan.IsLossless(plan);
        Details = Mp4Plan.Describe(plan);
        Duration = duration?.TotalSeconds ?? 0;
    }

    public void ReportPosition(double seconds)
    {
        if (Duration > 0) Progress = Math.Clamp(seconds / Duration, 0, 1);
    }

    public void ReportSpeed(double speed) => Speed = speed;

    public void Stopped() => Speed = 0;

    public void Complete(string output)
    {
        Output = output;
        Status = ConversionStatus.Completed;
        Progress = 1;
    }

    public void Fail(string error)
    {
        Status = ConversionStatus.Failed;
        Error = error;
    }
}
