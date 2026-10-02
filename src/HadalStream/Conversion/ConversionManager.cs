using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using HadalStream.Downloads;
using HadalStream.Settings;

namespace HadalStream.Conversion;

public enum ConversionStatus { Queued, Converting, Completed, Failed, Canceled }

public sealed record ConversionView(
    string Id, string FileName, string Source, string? Output, ConversionStatus Status, string? Error,
    bool? Lossless, string? Details, double Progress, double Speed, double Duration);

// Converts one file at a time: remuxing is disk-bound and re-encoding already uses every CPU core.
public sealed class ConversionManager(string ffmpegPath, SettingsStore settings)
{
    private sealed class Item
    {
        public required string Id { get; init; }
        public required string Source { get; init; }
        public string? Output { get; set; }
        public ConversionStatus Status { get; set; }
        public string? Error { get; set; }
        public bool? Lossless { get; set; }
        public string? Details { get; set; }
        public double Progress { get; set; }
        public double Speed { get; set; }
        public double Duration { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        public Task? Run { get; set; }
        // True from start until the run has released its files; guarded by the manager lock.
        public bool Active { get; set; }

        public ConversionView View() =>
            new(Id, Path.GetFileName(Source), Source, Output, Status, Error, Lossless, Details, Progress, Speed, Duration);
    }

    private readonly List<Item> items = [];
    private readonly Lock gate = new();

    // ("conversion" | "conversionRemoved", snapshot); raised from background threads.
    public event Action<string, ConversionView>? Changed;

    public static bool IsSupported(string path) =>
        Ffmpeg.SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) && File.Exists(path);

    public ConversionView[] List()
    {
        lock (gate) return [.. items.Select(i => i.View())];
    }

    // Paths come from the native file dialog or WebView2 drop objects, never from page script.
    public ConversionView[] Add(IEnumerable<string> paths)
    {
        List<Item> added = [];
        lock (gate)
        {
            foreach (var path in paths.Where(IsSupported).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (items.Any(i => i.Status is ConversionStatus.Queued or ConversionStatus.Converting
                                   && string.Equals(i.Source, path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                added.Add(new Item { Id = Guid.NewGuid().ToString("N")[..12], Source = path });
            }
            items.AddRange(added);
        }
        added.ForEach(i => Publish(i));
        Pump();
        return [.. added.Select(i => i.View())];
    }

    public bool Cancel(string id) => Update(id, i => i.Status is ConversionStatus.Queued or ConversionStatus.Converting, i =>
    {
        i.Status = ConversionStatus.Canceled;
        i.Cts?.Cancel();
    });

    public bool Retry(string id) => Update(id, i => i.Status is ConversionStatus.Failed or ConversionStatus.Canceled, i =>
    {
        i.Status = ConversionStatus.Queued;
        i.Error = null;
        i.Progress = 0;
    });

    public bool Remove(string id)
    {
        Item? item;
        lock (gate)
        {
            item = items.Find(i => i.Id == id);
            if (item is null) return false;
            items.Remove(item);
            if (item.Status is ConversionStatus.Queued or ConversionStatus.Converting) item.Status = ConversionStatus.Canceled;
            item.Cts?.Cancel();
        }
        Publish(item, "conversionRemoved");
        Pump();
        return true;
    }

    public string? GetOutput(string id)
    {
        lock (gate) return items.Find(i => i.Id == id && i.Status == ConversionStatus.Completed)?.Output;
    }

    public async Task ShutdownAsync()
    {
        Task[] running;
        lock (gate)
        {
            foreach (var item in items.Where(i => i.Status is ConversionStatus.Queued or ConversionStatus.Converting))
            {
                item.Status = ConversionStatus.Canceled;
                item.Cts?.Cancel();
            }
            running = [.. items.Select(i => i.Run).OfType<Task>()];
        }
        await Task.WhenAny(Task.WhenAll(running), Task.Delay(TimeSpan.FromSeconds(5)));
    }

    private bool Update(string id, Func<Item, bool> allowed, Action<Item> change)
    {
        Item? item;
        lock (gate)
        {
            item = items.Find(i => i.Id == id);
            if (item is null || !allowed(item)) return false;
            change(item);
        }
        Publish(item);
        Pump();
        return true;
    }

    private void Pump()
    {
        lock (gate)
        {
            if (items.Any(i => i.Active)) return;
            var next = items.Find(i => i.Status == ConversionStatus.Queued);
            if (next is null) return;
            next.Status = ConversionStatus.Converting;
            next.Active = true;
            next.Cts = new CancellationTokenSource();
            var token = next.Cts.Token;
            next.Run = Task.Run(() => RunAsync(next, token));
        }
    }

    private async Task RunAsync(Item item, CancellationToken ct)
    {
        Publish(item);
        string? temp = null;
        try
        {
            // `ffmpeg -i` with no output always exits 1; the stream listing on stderr is what we want.
            var info = Ffmpeg.ParseProbe(await RunFfmpegAsync(["-hide_banner", "-nostdin", "-i", item.Source], null, ct, allowFailure: true));
            var plan = Ffmpeg.Plan(info);
            item.Lossless = Ffmpeg.IsLossless(plan);
            item.Details = Ffmpeg.Describe(plan);
            item.Duration = info.Duration?.TotalSeconds ?? 0;
            Publish(item);

            var dir = Path.GetDirectoryName(item.Source)!;
            var name = Path.GetFileNameWithoutExtension(item.Source);
            temp = Path.Combine(dir, $"{name}.hs-{item.Id}.part");
            await RunFfmpegAsync(Ffmpeg.Arguments(item.Source, temp, plan), line => OnProgress(item, line), ct);

            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                var output = FileNames.Unique(dir, name, ".mp4");
                File.Move(temp, output);
                item.Output = output;
                item.Status = ConversionStatus.Completed;
                item.Progress = 1;
                item.Speed = 0;
            }
        }
        catch (Exception e)
        {
            bool failed;
            lock (gate)
            {
                // Cancellation means Cancel/Remove/Shutdown already set the status.
                failed = !ct.IsCancellationRequested;
                if (failed)
                {
                    item.Status = ConversionStatus.Failed;
                    item.Error = e.Message;
                }
            }
            if (failed) ErrorLog.Write(settings.Current.DownloadDir, $"Conversion failed: {item.Source}", e.ToString());
        }
        finally
        {
            if (temp is not null && File.Exists(temp)) TryDelete(temp);
            bool listed;
            lock (gate)
            {
                item.Active = false;
                item.Speed = 0;
                listed = items.Contains(item);
            }
            if (listed) Publish(item);
            Pump();
        }
    }

    private async Task<string> RunFfmpegAsync(IEnumerable<string> args, Action<string>? onOutput, CancellationToken ct, bool allowFailure = false)
    {
        if (!File.Exists(ffmpegPath)) throw new FileNotFoundException("FFmpeg is missing from the app folder. Please reinstall the app.");
        var start = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        using var kill = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception) { } // already exited
        });
        // Drain stderr concurrently so a full pipe can never stall ffmpeg.
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line) onOutput?.Invoke(line);
        await process.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();

        var stderr = await errors;
        if (process.ExitCode != 0 && !allowFailure)
        {
            var reason = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            throw new InvalidOperationException($"FFmpeg could not convert this file: {reason ?? $"exit code {process.ExitCode}"}");
        }
        return stderr;
    }

    // Parses `-progress pipe:1` key=value lines; a "progress=" line closes each update block.
    private void OnProgress(Item item, string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0) return;
        var value = line.AsSpan(eq + 1).Trim();
        switch (line.AsSpan(0, eq))
        {
            case "out_time_us" when item.Duration > 0 && long.TryParse(value, out var micros):
                item.Progress = Math.Clamp(micros / 1e6 / item.Duration, 0, 1);
                break;
            case "speed" when double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed):
                item.Speed = speed;
                break;
            case "progress":
                Publish(item);
                break;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }

    private void Publish(Item item, string type = "conversion") => Changed?.Invoke(type, item.View());
}
