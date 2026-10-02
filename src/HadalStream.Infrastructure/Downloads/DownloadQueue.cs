using System.Diagnostics;
using HadalStream.Application.Abstractions;
using HadalStream.Domain.Common;
using HadalStream.Domain.Downloads;
using HadalStream.Infrastructure.Http;
using HadalStream.Infrastructure.Persistence;

namespace HadalStream.Infrastructure.Downloads;

internal sealed class DownloadQueue : IDownloadQueue
{
    // Runtime state; only the job itself is saved.
    private sealed class Entry(DownloadJob job)
    {
        public DownloadJob Job { get; } = job;
        public double Speed { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        public Task? Run { get; set; }
        // True from start until the run has released the part file; guarded by the queue lock.
        public bool Active { get; set; }

        public DownloadView View() => DownloadView.From(Job, Speed);
    }

    private readonly string jobsFile;
    private readonly ISettingsStore settings;
    private readonly IEnumerable<IVideoSource> sources;
    private readonly HttpClientProvider http;
    private readonly IErrorLog log;
    private readonly List<Entry> entries;
    private readonly Lock gate = new();
    private volatile bool dirty;

    public DownloadQueue(AppPaths paths, ISettingsStore settings, IEnumerable<IVideoSource> sources, HttpClientProvider http, IErrorLog log)
    {
        jobsFile = Path.Combine(paths.DataDir, "jobs.json");
        this.settings = settings;
        this.sources = sources;
        this.http = http;
        this.log = log;
        entries = [.. (JsonFile.Load<List<DownloadJob>>(jobsFile) ?? []).Select(job =>
        {
            job.Interrupt();
            return new Entry(job);
        })];
        _ = TickAsync();
    }

    public event Action<DownloadView>? Changed;
    public event Action<DownloadView>? Removed;

    public DownloadView[] List()
    {
        lock (gate) return [.. entries.Select(e => e.View())];
    }

    public DownloadView[] Add(IEnumerable<NewDownload> items)
    {
        var dir = settings.Current.DownloadDir;
        List<Entry> added;
        lock (gate)
        {
            added = [.. items.Select(i => new Entry(DownloadJob.Create(i.Video, i.Title, i.Quality, dir)))];
            entries.AddRange(added);
            dirty = true;
        }
        added.ForEach(Publish);
        Pump();
        return [.. added.Select(e => e.View())];
    }

    public Result Pause(string id) => Update(id, job => job.Pause());

    public Result Resume(string id) => Update(id, job => job.Resume());

    public Result Cancel(string id) => Update(id, job => job.Cancel());

    public Result Remove(string id)
    {
        Entry? entry;
        lock (gate)
        {
            entry = entries.Find(e => e.Job.Id == id);
            if (entry is null) return DownloadErrors.NotFound;
            entries.Remove(entry);
            _ = entry.Job.Cancel(); // completed jobs keep their file
            Settle(entry);
            dirty = true;
        }
        Removed?.Invoke(entry.View());
        Pump();
        return Result.Success();
    }

    public Result<string> CompletedFile(string id)
    {
        lock (gate)
            return entries.Find(e => e.Job.Id == id && e.Job.Status == DownloadStatus.Completed)?.Job.FilePath is { } file
                ? file
                : DownloadErrors.NotFound;
    }

    public Result<string> OutputFolder(string id)
    {
        lock (gate) return entries.Find(e => e.Job.Id == id)?.Job.OutputDir is { } folder ? folder : DownloadErrors.NotFound;
    }

    // Pauses running jobs so they resume from their completed segments next launch.
    public async Task ShutdownAsync()
    {
        Task[] running;
        lock (gate)
        {
            foreach (var entry in entries)
            {
                entry.Job.Interrupt();
                entry.Cts?.Cancel();
            }
            running = [.. entries.Select(e => e.Run).OfType<Task>()];
        }
        await Task.WhenAny(Task.WhenAll(running), Task.Delay(TimeSpan.FromSeconds(5)));
        Save();
    }

    private Result Update(string id, Func<DownloadJob, Result> transition)
    {
        Entry? entry;
        lock (gate)
        {
            entry = entries.Find(e => e.Job.Id == id);
            if (entry is null) return DownloadErrors.NotFound;
            if (transition(entry.Job) is { IsSuccess: false } failed) return failed;
            Settle(entry);
            dirty = true;
        }
        Publish(entry);
        Pump();
        return Result.Success();
    }

    // Stops a run that may no longer download. A running job deletes its own part file once its handle is closed.
    private static void Settle(Entry entry)
    {
        if (entry.Job.Status is DownloadStatus.Queued or DownloadStatus.Downloading) return;
        entry.Cts?.Cancel();
        if (entry.Job.Status == DownloadStatus.Canceled && !entry.Active) FilePaths.TryDelete(PartPath(entry.Job));
    }

    private void Pump()
    {
        lock (gate)
        {
            var running = entries.Count(e => e.Active);
            // Skip jobs whose previous run is still releasing the part file.
            foreach (var entry in entries.Where(e => e.Job.Status == DownloadStatus.Queued && !e.Active).ToList())
            {
                if (running++ >= settings.Current.MaxConcurrentJobs) break;
                entry.Job.Start();
                entry.Active = true;
                entry.Cts = new CancellationTokenSource();
                var token = entry.Cts.Token;
                entry.Run = Task.Run(() => RunAsync(entry, token));
            }
        }
    }

    private async Task RunAsync(Entry entry, CancellationToken ct)
    {
        var job = entry.Job;
        Publish(entry);
        try
        {
            await DownloadAsync(job, ct);
        }
        catch (Exception e)
        {
            bool failed;
            lock (gate)
            {
                // Cancellation means Pause/Cancel/Remove/Shutdown already set the status.
                failed = !ct.IsCancellationRequested;
                if (failed) job.Fail(e.Message);
            }
            if (failed)
                log.Write($"Download failed: \"{job.Title}\" ({job.Video.Platform} {job.Video.Id}, {job.Quality}, {job.Video.Url})",
                    e.ToString(), job.OutputDir);
        }
        finally
        {
            bool listed, discard;
            lock (gate)
            {
                entry.Active = false;
                entry.Cts?.Dispose();
                entry.Cts = null;
                entry.Speed = 0;
                discard = job.Status == DownloadStatus.Canceled;
                listed = entries.Contains(entry);
            }
            if (discard) FilePaths.TryDelete(PartPath(job));
            dirty = true;
            if (listed) Publish(entry);
            Pump();
        }
    }

    private async Task DownloadAsync(DownloadJob job, CancellationToken ct)
    {
        var plan = await sources.First(s => s.Platform == job.Video.Platform).PlanAsync(job.Video, job.Quality, ct);
        var part = PartPath(job);
        Directory.CreateDirectory(job.OutputDir);
        if (!job.Prepare(plan.TotalSize, plan.SegmentCount, plan.SegmentSize, plan.Resumable)) File.Delete(part);
        EnsureFreeSpace(job.OutputDir, job.TotalBytes - job.DownloadedBytes);

        await SegmentDownloader.DownloadAsync(http.Client, plan, part, job.DoneSegments.ToHashSet(), settings.Current.Connections,
            job.AddBytes, i =>
            {
                job.MarkDone(i);
                dirty = true;
            }, ct);

        var name = FileName.Sanitize(new[] { job.Title, plan.Title, $"{job.Video.Id}_{job.Quality}" }.First(t => !string.IsNullOrWhiteSpace(t)));
        lock (gate)
        {
            // Cancel/Remove may have landed while the last segment finished.
            ct.ThrowIfCancellationRequested();
            var final = FilePaths.Unique(job.OutputDir, name, ".mp4");
            File.Move(part, final);
            job.Complete(final);
        }
    }

    private static string PartPath(DownloadJob job) => Path.Combine(job.OutputDir, $"hs-{job.Id}.part");

    private static void EnsureFreeSpace(string dir, long needed)
    {
        long free;
        try
        {
            free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            return; // UNC/unknown drives: let the write fail naturally.
        }
        if (free < needed) throw new IOException($"Not enough disk space. Need {needed / 1048576} MB.");
    }

    private void Publish(Entry entry) => Changed?.Invoke(entry.View());

    private void Save()
    {
        lock (gate)
        {
            dirty = false;
            JsonFile.Save(jobsFile, entries.Select(e => e.Job).ToList());
        }
    }

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        var last = new Dictionary<string, (long Bytes, long Time)>();
        for (var tick = 1; await timer.WaitForNextTickAsync(); tick++)
        {
            try
            {
                Entry[] running;
                lock (gate) running = [.. entries.Where(e => e.Job.Status == DownloadStatus.Downloading)];
                var now = Stopwatch.GetTimestamp();
                foreach (var entry in running)
                {
                    var bytes = entry.Job.DownloadedBytes;
                    if (last.TryGetValue(entry.Job.Id, out var prev))
                    {
                        var instant = Math.Max(0, bytes - prev.Bytes) / Stopwatch.GetElapsedTime(prev.Time, now).TotalSeconds;
                        entry.Speed = entry.Speed == 0 ? instant : entry.Speed * 0.7 + instant * 0.3;
                    }
                    last[entry.Job.Id] = (bytes, now);
                    Publish(entry);
                }
                foreach (var id in last.Keys.Except(running.Select(e => e.Job.Id)).ToList()) last.Remove(id);
                if (tick % 4 == 0 && dirty) Save();
            }
            catch (Exception e)
            {
                // Keep ticking: a dead loop would freeze progress and stop saving. Disk errors just retry.
                if (e is not (IOException or UnauthorizedAccessException)) log.Write("Progress loop error", e.ToString());
            }
        }
    }
}
