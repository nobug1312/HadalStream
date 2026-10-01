using System.Net;
using HadalStream.Application.Abstractions;
using HadalStream.Infrastructure.Downloads;

namespace HadalStream.Tests;

public class SegmentDownloaderTests
{
    private const int SegmentSize = 1000;
    private static readonly byte[] Source = [.. Enumerable.Range(0, 5 * SegmentSize + 123).Select(i => (byte)(i * 31))];

    private sealed class FakeServer(Func<int, int, HttpResponseMessage?>? fault = null) : HttpMessageHandler
    {
        private readonly Dictionary<int, int> attempts = [];
        public List<int> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var index = int.Parse(request.RequestUri!.Segments[^1]);
            int attempt;
            lock (attempts)
            {
                attempt = attempts[index] = attempts.GetValueOrDefault(index) + 1;
                Requested.Add(index);
            }
            var start = index * SegmentSize;
            return Task.FromResult(fault?.Invoke(index, attempt) ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Source[start..Math.Min(start + SegmentSize, Source.Length)]),
            });
        }
    }

    private static DownloadPlan Plan() => new("t", Source.Length, SegmentSize, 6, true,
        i => new HttpRequestMessage(HttpMethod.Get, $"http://test/{i}"));

    private static Task Run(FakeServer server, string path, IReadOnlySet<int>? skip = null, List<int>? done = null)
    {
        done ??= [];
        return SegmentDownloader.DownloadAsync(new HttpClient(server), Plan(), path, skip ?? new HashSet<int>(), 3,
            _ => { }, i => { lock (done) done.Add(i); }, CancellationToken.None, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task DownloadsAllSegmentsIntoPlace()
    {
        var path = Path.GetTempFileName();
        var done = new List<int>();

        await Run(new FakeServer(), path, done: done);

        Assert.Equal(Source, File.ReadAllBytes(path));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, done.Order());
    }

    [Fact]
    public async Task RetriesFailedSegment()
    {
        var path = Path.GetTempFileName();
        var server = new FakeServer((index, attempt) => index == 2 && attempt == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : null);

        await Run(server, path);

        Assert.Equal(Source, File.ReadAllBytes(path));
        Assert.Equal(2, server.Requested.Count(i => i == 2));
    }

    [Fact]
    public async Task ResumeSkipsCompletedSegments()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, Source[..(2 * SegmentSize)]);
        var server = new FakeServer();

        await Run(server, path, skip: new HashSet<int> { 0, 1 });

        Assert.Equal(Source, File.ReadAllBytes(path));
        Assert.DoesNotContain(0, server.Requested);
        Assert.DoesNotContain(1, server.Requested);
    }

    [Fact]
    public async Task FailsWhenSegmentLengthIsWrong()
    {
        var server = new FakeServer((index, _) => index == 3
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) }
            : null);

        await Assert.ThrowsAsync<IOException>(() => Run(server, Path.GetTempFileName()));
        Assert.Equal(5, server.Requested.Count(i => i == 3));
    }
}
