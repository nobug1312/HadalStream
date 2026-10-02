using System.Diagnostics;
using System.Text.RegularExpressions;
using HadalStream.Resolvers;
using HadalStream.Settings;

namespace HadalStream.Downloads;

public enum JobStatus { Queued, Downloading, Paused, Completed, Failed, Canceled }

public sealed record AddJobRequest(VideoRef Video, string Title, string Quality);

public sealed record JobView(
    string Id, Platform Platform, string VideoId, string Title, string Quality, JobStatus Status, string? Error,
    string? FilePath, long TotalBytes, long DownloadedBytes, double Speed, DateTimeOffset CreatedAt);

public sealed class Job
{
    private readonly HashSet<int> done = [];
    private long downloaded;

    public required string Id { get; init; }
    public required VideoRef Video { get; init; }
    public required string Title { get; init; }
    public required string Quality { get; init; }
    public required string OutputDir { get; init; }
    public JobStatus Status { get; set; }
    public string? Error { get; set; }
    public string? FilePath { get; set; }
    public long TotalBytes { get; set; }
    public int SegmentCount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public long DownloadedBytes { get => Interlocked.Read(ref downloaded); set => Interlocked.Exchange(ref downloaded, value); }
    public int[] DoneSegments { get { lock (done) return [.. done]; } init => done = [.. value]; }

    internal double Speed { get; set; }
    internal CancellationTokenSource? Cts { get; set; }
    internal Task? Run { get; set; }
    // True from start until the run has released the part file; guarded by the manager lock.
    internal bool Active { get; set; }
    internal string PartPath => Path.Combine(OutputDir, $"hs-{Id}.part");

    internal void AddBytes(long count) => Interlocked.Add(ref downloaded, count);
    internal void MarkDone(int index) { lock (done) done.Add(index); }
    internal void ResetSegments() { lock (done) done.Clear(); DownloadedBytes = 0; }

    internal JobView View() => new(Id, Video.Platform, Video.Id, Title, Quality, Status, Error, FilePath, TotalBytes, DownloadedBytes,
        Status == JobStatus.Downloading ? Speed : 0, CreatedAt);
}

public sealed class DownloadManager
{
    private readonly string jobsFile;
    private readonly SettingsStore settings;
    private readonly VideoResolver resolver;
    private readonly HttpClientProvider http;
    private readonly List<Job> jobs;
    private readonly Lock gate = new();
    private volatile bool dirty;

    public DownloadManager(string dataDir, SettingsStore settings, VideoResolver resolver, HttpClientProvider http)
    {
        jobsFile = Path.Combine(dataDir, "jobs.json");
        this.settings = settings;
        this.resolver = resolver;
        this.http = http;
        jobs = AppJson.Load<List<Job>>(jobsFile) ?? [];
        foreach (var job in jobs.Where(j => j.Status is JobStatus.Queued or JobStatus.Downloading)) job.Status = JobStatus.Paused;
        _ = TickAsync();
    }

    // ("job" | "removed", snapshot); raised from background threads.
    public event Action<string, JobView>? Changed;

    public JobView[] List()
    {
        lock (gate) return [.. jobs.Select(j => j.View())];
    }

    public JobView[] Add(IEnumerable<AddJobRequest> items)
    {
        var dir = settings.Current.DownloadDir;
        List<Job> added;
        lock (gate)
        {
            added = [.. items.Select(i => new Job
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Video = i.Video,
                Title = i.Title.Trim(),
                Quality = i.Quality,
                OutputDir = dir,
            })];
            jobs.AddRange(added);
            dirty = true;
        }
        added.ForEach(j => Publish(j));
        Pump();
        return [.. added.Select(j => j.View())];
    }

    public bool Pause(string id) => Update(id, j => j.Status is JobStatus.Queued or JobStatus.Downloading, j =>
    {
        j.Status = JobStatus.Paused;
        j.Cts?.Cancel();
    });

    public bool Resume(string id) => Update(id, j => j.Status is JobStatus.Paused or JobStatus.Failed or JobStatus.Canceled, j =>
    {
        j.Status = JobStatus.Queued;
        j.Error = null;
    });

    public bool Cancel(string id) => Update(id, j => j.Status is not (JobStatus.Completed or JobStatus.Canceled), StopAndDiscard);

    public bool Remove(string id)
    {
        Job? job;
        lock (gate)
        {
            job = jobs.Find(j => j.Id == id);
            if (job is null) return false;
            jobs.Remove(job);
            if (job.Status != JobStatus.Completed) StopAndDiscard(job);
            dirty = true;
        }
        Publish(job, "removed");
        Pump();
        return true;
    }

    public string? GetCompletedFile(string id)
    {
        lock (gate) return jobs.Find(j => j.Id == id && j.Status == JobStatus.Completed)?.FilePath;
    }

    public string? GetLogFile(string id)
    {
        lock (gate) return jobs.Find(j => j.Id == id) is { } job ? ErrorLog.PathIn(job.OutputDir) : null;
    }

    // Pauses running jobs so they resume from their completed segments next launch.
    public async Task ShutdownAsync()
    {
        Task[] running;
        lock (gate)
        {
            foreach (var job in jobs.Where(j => j.Status is JobStatus.Queued or JobStatus.Downloading))
            {
                job.Status = JobStatus.Paused;
                job.Cts?.Cancel();
            }
            running = [.. jobs.Select(j => j.Run).OfType<Task>()];
        }
        await Task.WhenAny(Task.WhenAll(running), Task.Delay(TimeSpan.FromSeconds(5)));
        Save();
    }

    private void StopAndDiscard(Job job)
    {
        job.Status = JobStatus.Canceled;
        job.ResetSegments();
        job.Cts?.Cancel();
        // A running job deletes its own part file once its handle is closed.
        if (!job.Active) TryDelete(job.PartPath);
    }

    private bool Update(string id, Func<Job, bool> allowed, Action<Job> change)
    {
        Job? job;
        lock (gate)
        {
            job = jobs.Find(j => j.Id == id);
            if (job is null || !allowed(job)) return false;
            change(job);
            dirty = true;
        }
        Publish(job);
        Pump();
        return true;
    }

    private void Pump()
    {
        lock (gate)
        {
            var running = jobs.Count(j => j.Active);
            // Skip jobs whose previous run is still releasing the part file.
            foreach (var job in jobs.Where(j => j.Status == JobStatus.Queued && !j.Active).ToList())
            {
                if (running++ >= settings.Current.MaxConcurrentJobs) break;
                job.Status = JobStatus.Downloading;
                job.Active = true;
                job.Cts = new CancellationTokenSource();
                var token = job.Cts.Token;
                job.Run = Task.Run(() => RunAsync(job, token));
            }
        }
    }

    private async Task RunAsync(Job job, CancellationToken ct)
    {
        Publish(job);
        try
        {
            var plan = await resolver.PlanAsync(job.Video, job.Quality, ct);
            Directory.CreateDirectory(job.OutputDir);
            if (!plan.Resumable || job.TotalBytes != (plan.TotalSize ?? 0) || job.SegmentCount != plan.SegmentCount)
            {
                job.ResetSegments();
                File.Delete(job.PartPath);
            }
            job.TotalBytes = plan.TotalSize ?? 0;
            job.SegmentCount = plan.SegmentCount;
            var skip = job.DoneSegments.ToHashSet();
            job.DownloadedBytes = plan.TotalSize is long total ? skip.Sum(i => Math.Min(plan.SegmentSize, total - i * plan.SegmentSize)) : 0;
            EnsureFreeSpace(job.OutputDir, job.TotalBytes - job.DownloadedBytes);

            await SegmentDownloader.DownloadAsync(http.Client, plan, job.PartPath, skip, settings.Current.Connections,
                job.AddBytes, i => { job.MarkDone(i); dirty = true; }, ct);

            var name = FileNames.Sanitize(new[] { job.Title, plan.Title, $"{job.Video.Id}_{job.Quality}" }.First(t => !string.IsNullOrWhiteSpace(t)));
            lock (gate)
            {
                // Cancel/Remove may have landed while the last segment finished.
                ct.ThrowIfCancellationRequested();
                var final = FileNames.Unique(job.OutputDir, name, ".mp4");
                File.Move(job.PartPath, final);
                job.FilePath = final;
                job.Status = JobStatus.Completed;
                job.ResetSegments();
                job.DownloadedBytes = job.TotalBytes;
            }
        }
        catch (Exception e)
        {
            bool failed;
            lock (gate)
            {
                // Cancellation means Pause/Cancel/Remove/Shutdown already set the status.
                failed = !ct.IsCancellationRequested;
                if (failed)
                {
                    job.Status = JobStatus.Failed;
                    job.Error = e.Message;
                }
            }
            if (failed)
                ErrorLog.Write(job.OutputDir,
                    $"Download failed: \"{job.Title}\" ({job.Video.Platform} {job.Video.Id}, {job.Quality}, {job.Video.Url})", e.ToString());
        }
        finally
        {
            bool listed, discard;
            lock (gate)
            {
                job.Active = false;
                job.Speed = 0;
                discard = job.Status == JobStatus.Canceled;
                listed = jobs.Contains(job);
            }
            if (discard) TryDelete(job.PartPath);
            dirty = true;
            if (listed) Publish(job);
            Pump();
        }
    }

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
        if (free < needed) throw new IOException($"Not enough free disk space: {needed / 1048576} MB needed, {free / 1048576} MB available.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }

    private void Publish(Job job, string type = "job") => Changed?.Invoke(type, job.View());

    private void Save()
    {
        lock (gate)
        {
            dirty = false;
            AppJson.Save(jobsFile, jobs);
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
                Job[] running;
                lock (gate) running = [.. jobs.Where(j => j.Status == JobStatus.Downloading)];
                var now = Stopwatch.GetTimestamp();
                foreach (var job in running)
                {
                    var bytes = job.DownloadedBytes;
                    if (last.TryGetValue(job.Id, out var prev))
                    {
                        var instant = Math.Max(0, bytes - prev.Bytes) / Stopwatch.GetElapsedTime(prev.Time, now).TotalSeconds;
                        job.Speed = job.Speed == 0 ? instant : job.Speed * 0.7 + instant * 0.3;
                    }
                    last[job.Id] = (bytes, now);
                    Publish(job);
                }
                foreach (var id in last.Keys.Except(running.Select(j => j.Id)).ToList()) last.Remove(id);
                if (tick % 4 == 0 && dirty) Save();
            }
            catch (Exception e)
            {
                // Keep ticking: a dead loop would freeze progress and stop persistence. Disk errors just retry.
                if (e is not (IOException or UnauthorizedAccessException))
                    ErrorLog.Write(settings.Current.DownloadDir, "Progress loop error", e.ToString());
            }
        }
    }
}

internal static partial class FileNames
{
    public static string Sanitize(string name)
    {
        var cleaned = new string([.. name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c)]).Trim('.', ' ');
        if (cleaned.Length > 150) cleaned = cleaned[..150].Trim('.', ' ');
        if (cleaned.Length == 0) return "video";
        return Reserved().IsMatch(cleaned) ? "_" + cleaned : cleaned;
    }

    public static string Unique(string dir, string baseName, string extension)
    {
        var path = Path.Combine(dir, baseName + extension);
        for (var n = 1; File.Exists(path); n++) path = Path.Combine(dir, $"{baseName} ({n}){extension}");
        return path;
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();
}
