using Microsoft.Win32.SafeHandles;
using HadalStream.Resolvers;

namespace HadalStream.Downloads;

public static class SegmentDownloader
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    // Writes every segment straight into its offset in `path`, so there is no merge step.
    public static async Task DownloadAsync(
        HttpClient client,
        DownloadPlan plan,
        string path,
        IReadOnlySet<int> skip,
        int connections,
        Action<long> onBytes,
        Action<int> onSegmentDone,
        CancellationToken ct,
        TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? TimeSpan.FromSeconds(1);
        using var file = File.OpenHandle(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, FileOptions.Asynchronous);
        if (plan.TotalSize is long total) RandomAccess.SetLength(file, total);
        var pending = Enumerable.Range(0, plan.SegmentCount).Where(i => !skip.Contains(i));
        var options = new ParallelOptions { MaxDegreeOfParallelism = connections, CancellationToken = ct };
        await Parallel.ForEachAsync(pending, options, async (index, token) =>
        {
            for (var attempt = 1; ; attempt++)
            {
                long written = 0;
                try
                {
                    await DownloadSegmentAsync(client, plan, file, index, n => { written += n; onBytes(n); }, token);
                    onSegmentDone(index);
                    return;
                }
                catch (Exception) when (!token.IsCancellationRequested && attempt < MaxAttempts)
                {
                    onBytes(-written);
                    await Task.Delay(delay * Math.Pow(2, attempt - 1), token);
                }
            }
        });
    }

    private static async Task DownloadSegmentAsync(
        HttpClient client, DownloadPlan plan, SafeFileHandle file, int index, Action<long> progress, CancellationToken ct)
    {
        var offset = index * plan.SegmentSize;
        long? expected = plan.TotalSize is long total ? Math.Min(plan.SegmentSize, total - offset) : null;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(IdleTimeout);
        using var request = plan.CreateRequest(index);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(idle.Token);
        var buffer = new byte[81920];
        long written = 0;
        while (true)
        {
            idle.CancelAfter(IdleTimeout);
            var read = await stream.ReadAsync(buffer, idle.Token);
            if (read == 0) break;
            if (written + read > expected) throw new IOException($"Segment {index} is longer than expected ({expected} bytes).");
            await RandomAccess.WriteAsync(file, buffer.AsMemory(0, read), offset + written, ct);
            written += read;
            progress(read);
        }
        if (expected is long length && written != length)
            throw new IOException($"Segment {index} is incomplete: {written}/{length} bytes.");
    }
}
