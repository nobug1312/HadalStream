using System.Text.Json;
using HadalStream.Domain.Common;
using HadalStream.Domain.Conversions;
using HadalStream.Domain.Downloads;
using HadalStream.Domain.Videos;
using HadalStream.Infrastructure.Persistence;

namespace HadalStream.Tests;

public class DomainTests
{
    private static readonly VideoRef Video = new(Platform.Abyss, "K8R6OOjS7", "https://abysscdn.com/?v=K8R6OOjS7", null);

    [Fact]
    public void Result_CarriesValueOrError()
    {
        Result<int> ok = 2;
        Result<int> failed = new Error("gone");

        Assert.Equal(2, ok.Value);
        Assert.True(ok.Bind(_ => Result.Success()).IsSuccess);
        Assert.Throws<InvalidOperationException>(() => failed.Value);
        Assert.Equal("gone", failed.Bind(_ => Result.Success()).Error!.Message);
    }

    [Fact]
    public void DownloadJob_EnforcesTransitions()
    {
        var job = DownloadJob.Create(Video, " Title ", "720p", "C:\\out");
        Assert.Equal("Title", job.Title);

        Assert.False(job.Resume().IsSuccess);
        Assert.True(job.Pause().IsSuccess);
        Assert.True(job.Resume().IsSuccess);
        Assert.True(job.Cancel().IsSuccess);
        Assert.Equal(DownloadErrors.Unavailable, job.Cancel().Error);
        Assert.Equal(DownloadStatus.Canceled, job.Status);
    }

    [Fact]
    public void DownloadJob_PrepareKeepsProgressOnlyWhenTheFileMatches()
    {
        var job = DownloadJob.Create(Video, "t", "720p", "C:\\out");
        Assert.False(job.Prepare(5000, 3, 2000, resumable: true));
        job.MarkDone(0);
        job.MarkDone(2);

        Assert.True(job.Prepare(5000, 3, 2000, resumable: true));
        Assert.Equal(3000, job.DownloadedBytes);
        Assert.False(job.Prepare(6000, 3, 2000, resumable: true));
        Assert.Empty(job.DoneSegments);
    }

    // jobs.json is read back through the rehydration constructor; this guards that mapping.
    [Fact]
    public void DownloadJob_RoundTripsThroughJson()
    {
        var job = DownloadJob.Create(Video, "t", "720p", "C:\\out");
        job.Prepare(5000, 3, 2000, resumable: true);
        job.MarkDone(1);
        job.Fail("boom");

        var json = JsonSerializer.Serialize(job, JsonFile.Options);
        var copy = JsonSerializer.Deserialize<DownloadJob>(json, JsonFile.Options)!;

        Assert.Equal(json, JsonSerializer.Serialize(copy, JsonFile.Options));
        Assert.Equal((DownloadStatus.Failed, "boom"), (copy.Status, copy.Error));
        Assert.Equal([1], copy.DoneSegments);
        Assert.DoesNotContain("isPending", json);
    }

    [Fact]
    public void ConversionJob_RetriesOnlyAfterFailureOrCancel()
    {
        var job = new ConversionJob("C:\\a.mkv");
        Assert.False(job.Retry().IsSuccess);
        job.Start();
        job.Fail("x");
        Assert.True(job.Retry().IsSuccess);
        Assert.Equal((ConversionStatus.Queued, (string?)null), (job.Status, job.Error));
    }
}
