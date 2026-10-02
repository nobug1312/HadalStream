using System.Globalization;
using HadalStream.Application.Abstractions;
using HadalStream.Domain.Common;
using HadalStream.Domain.Conversions;

namespace HadalStream.Infrastructure.Conversions;

// One conversion at a time: remuxing is disk-bound and re-encoding already uses every CPU core.
internal sealed class ConversionQueue(AppPaths paths, IErrorLog log) : IConversionQueue
{
    private sealed class Entry(ConversionJob job)
    {
        public ConversionJob Job { get; } = job;
        public CancellationTokenSource? Cts { get; set; }
        public Task? Run { get; set; }
        // True from start until the run has released its files; guarded by the queue lock.
        public bool Active { get; set; }
    }

    private readonly List<Entry> entries = [];
    private readonly Lock gate = new();

    public event Action<ConversionView>? Changed;
    public event Action<ConversionView>? Removed;

    public ConversionView[] List()
    {
        lock (gate) return [.. entries.Select(e => ConversionView.From(e.Job))];
    }

    public ConversionView[] Add(IEnumerable<string> files)
    {
        List<Entry> added = [];
        lock (gate)
        {
            foreach (var file in files.Where(f => Mp4Plan.Accepts(f) && File.Exists(f)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (entries.Any(e => e.Job.IsPending && string.Equals(e.Job.Source, file, StringComparison.OrdinalIgnoreCase))) continue;
                added.Add(new Entry(new ConversionJob(file)));
            }
            entries.AddRange(added);
        }
        added.ForEach(Publish);
        Pump();
        return [.. added.Select(e => ConversionView.From(e.Job))];
    }

    public Result Cancel(string id) => Update(id, job => job.Cancel());

    public Result Retry(string id) => Update(id, job => job.Retry());

    public Result Remove(string id)
    {
        Entry? entry;
        lock (gate)
        {
            entry = entries.Find(e => e.Job.Id == id);
            if (entry is null) return ConversionErrors.NotFound;
            entries.Remove(entry);
            _ = entry.Job.Cancel(); // finished items simply leave the list
            entry.Cts?.Cancel();
        }
        Removed?.Invoke(ConversionView.From(entry.Job));
        Pump();
        return Result.Success();
    }

    public Result<string> Output(string id)
    {
        lock (gate)
            return entries.Find(e => e.Job.Id == id && e.Job.Status == ConversionStatus.Completed)?.Job.Output is { } output
                ? output
                : ConversionErrors.NotFound;
    }

    public async Task ShutdownAsync()
    {
        Task[] running;
        lock (gate)
        {
            foreach (var entry in entries)
            {
                _ = entry.Job.Cancel();
                entry.Cts?.Cancel();
            }
            running = [.. entries.Select(e => e.Run).OfType<Task>()];
        }
        await Task.WhenAny(Task.WhenAll(running), Task.Delay(TimeSpan.FromSeconds(5)));
    }

    private Result Update(string id, Func<ConversionJob, Result> transition)
    {
        Entry? entry;
        lock (gate)
        {
            entry = entries.Find(e => e.Job.Id == id);
            if (entry is null) return ConversionErrors.NotFound;
            if (transition(entry.Job) is { IsSuccess: false } failed) return failed;
            if (!entry.Job.IsPending) entry.Cts?.Cancel();
        }
        Publish(entry);
        Pump();
        return Result.Success();
    }

    private void Pump()
    {
        lock (gate)
        {
            if (entries.Any(e => e.Active)) return;
            var next = entries.Find(e => e.Job.Status == ConversionStatus.Queued);
            if (next is null) return;
            next.Job.Start();
            next.Active = true;
            next.Cts = new CancellationTokenSource();
            var token = next.Cts.Token;
            next.Run = Task.Run(() => RunAsync(next, token));
        }
    }

    private async Task RunAsync(Entry entry, CancellationToken ct)
    {
        Publish(entry);
        try
        {
            if ((await ConvertAsync(entry, ct)).Error is { } error) Fail(entry.Job, error.Message, ct);
        }
        catch (Exception e)
        {
            if (Fail(entry.Job, e.Message, ct)) log.Write($"Conversion failed: {entry.Job.Source}", e.ToString());
        }
        finally
        {
            bool listed;
            lock (gate)
            {
                entry.Active = false;
                entry.Cts?.Dispose();
                entry.Cts = null;
                entry.Job.Stopped();
                listed = entries.Contains(entry);
            }
            if (listed) Publish(entry);
            Pump();
        }
    }

    private async Task<Result> ConvertAsync(Entry entry, CancellationToken ct)
    {
        var job = entry.Job;
        // `ffmpeg -i` with no output always exits 1; the stream listing on stderr is what we want.
        var info = Ffmpeg.ParseProbe(await Ffmpeg.RunAsync(paths.Ffmpeg, ["-hide_banner", "-nostdin", "-i", job.Source], null, ct, allowFailure: true));
        var plan = Mp4Plan.For(info);
        if (!plan.IsSuccess) return plan;
        job.Planned(plan.Value, info.Duration);
        Publish(entry);

        var dir = Path.GetDirectoryName(job.Source)!;
        var name = Path.GetFileNameWithoutExtension(job.Source);
        var temp = Path.Combine(dir, $"{name}.hs-{job.Id}.part");
        try
        {
            await Ffmpeg.RunAsync(paths.Ffmpeg, Ffmpeg.Arguments(job.Source, temp, plan.Value), line => OnProgress(entry, line), ct);
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                var output = FilePaths.Unique(dir, name, ".mp4");
                File.Move(temp, output);
                job.Complete(output);
            }
            return Result.Success();
        }
        finally
        {
            FilePaths.TryDelete(temp);
        }
    }

    // False when Cancel/Remove/Shutdown already set the status.
    private bool Fail(ConversionJob job, string message, CancellationToken ct)
    {
        lock (gate)
        {
            if (ct.IsCancellationRequested) return false;
            job.Fail(message);
            return true;
        }
    }

    // Parses `-progress pipe:1` key=value lines; a "progress=" line closes each update block.
    private void OnProgress(Entry entry, string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0) return;
        var value = line.AsSpan(eq + 1).Trim();
        switch (line.AsSpan(0, eq))
        {
            case "out_time_us" when long.TryParse(value, out var micros):
                entry.Job.ReportPosition(micros / 1e6);
                break;
            case "speed" when double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed):
                entry.Job.ReportSpeed(speed);
                break;
            case "progress":
                Publish(entry);
                break;
        }
    }

    private void Publish(Entry entry) => Changed?.Invoke(ConversionView.From(entry.Job));
}
