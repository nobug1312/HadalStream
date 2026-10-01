using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using HadalStream.Domain.Conversions;

namespace HadalStream.Infrastructure.Conversions;

internal static partial class Ffmpeg
{
    // Parses the stream listing ffmpeg prints for `ffmpeg -i <file>` (stable format across releases).
    public static MediaInfo ParseProbe(string stderr)
    {
        var duration = DurationLine().Match(stderr) is { Success: true } d
            ? new TimeSpan(0, int.Parse(d.Groups[1].Value), int.Parse(d.Groups[2].Value), 0)
                + TimeSpan.FromSeconds(double.Parse(d.Groups[3].Value, CultureInfo.InvariantCulture))
            : (TimeSpan?)null;
        MediaStream[] streams =
        [
            .. StreamLine().Matches(stderr).Select(m => new MediaStream(
                int.Parse(m.Groups[1].Value),
                m.Groups[2].Value switch { "Video" => StreamKind.Video, "Audio" => StreamKind.Audio, "Subtitle" => StreamKind.Subtitle, _ => StreamKind.Other },
                m.Groups[3].Value,
                m.Groups[4].Value.Contains("(attached pic)"))),
        ];
        return new(duration, streams);
    }

    public static List<string> Arguments(string input, string output, IReadOnlyList<PlannedStream> plan)
    {
        List<string> args = ["-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", input];
        for (var o = 0; o < plan.Count; o++)
        {
            var (stream, action) = plan[o];
            args.AddRange(["-map", $"0:{stream.Index}"]);
            args.AddRange(action switch
            {
                // hvc1 tag lets Apple players and Windows Photos open copied HEVC.
                StreamAction.Copy when stream.Codec == "hevc" => [$"-c:{o}", "copy", $"-tag:{o}", "hvc1"],
                StreamAction.Copy => [$"-c:{o}", "copy"],
                // CRF 18 is visually lossless; only used when MP4 can't hold the original codec.
                StreamAction.EncodeH264 => [$"-c:{o}", "libx264", $"-crf:{o}", "18", $"-preset:{o}", "medium", $"-pix_fmt:{o}", "yuv420p"],
                StreamAction.EncodeAac => [$"-c:{o}", "aac", $"-b:{o}", "256k"],
                _ => [$"-c:{o}", "mov_text"],
            });
        }
        args.AddRange(["-map_metadata", "0", "-max_muxing_queue_size", "1024", "-movflags", "+faststart",
            "-progress", "pipe:1", "-nostats", "-f", "mp4", output]);
        return args;
    }

    // Returns stderr. Cancelling kills the whole process tree.
    public static async Task<string> RunAsync(
        string exe, IEnumerable<string> args, Action<string>? onOutput, CancellationToken ct, bool allowFailure = false)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException("FFmpeg is missing. Reinstall the app.");
        var start = new ProcessStartInfo(exe)
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
            throw new InvalidOperationException($"FFmpeg failed: {reason ?? $"exit code {process.ExitCode}"}");
        }
        return stderr;
    }

    [GeneratedRegex(@"Duration: (\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationLine();

    [GeneratedRegex(@"^\s*Stream #0:(\d+)(?:\[0x[0-9a-f]+\])?(?:\([^)]*\))?: (Video|Audio|Subtitle|Data|Attachment): (\w+)(.*)$", RegexOptions.Multiline)]
    private static partial Regex StreamLine();
}
