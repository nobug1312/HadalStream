using System.Globalization;
using System.Text.RegularExpressions;

namespace HadalStream.Conversion;

public enum StreamKind { Video, Audio, Subtitle, Other }

public enum StreamAction { Copy, EncodeH264, EncodeAac, ToMovText }

public sealed record MediaStream(int Index, StreamKind Kind, string Codec, bool AttachedPicture = false);

public sealed record MediaInfo(TimeSpan? Duration, IReadOnlyList<MediaStream> Streams);

public sealed record PlannedStream(MediaStream Stream, StreamAction Action);

// Pure planning: which streams MP4 can carry untouched (lossless remux) and which must be re-encoded.
public static partial class Ffmpeg
{
    public static readonly string[] SupportedExtensions = [".flv", ".mov", ".mkv"];

    private static readonly HashSet<string> Mp4Video = ["h264", "hevc", "av1", "vp9", "mpeg4", "mpeg2video", "mpeg1video"];
    private static readonly HashSet<string> Mp4Audio = ["aac", "mp3", "ac3", "eac3", "opus", "flac", "alac"];
    private static readonly HashSet<string> TextSubtitles = ["subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text"];

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

    public static IReadOnlyList<PlannedStream> Plan(MediaInfo info)
    {
        List<PlannedStream> planned = [];
        foreach (var stream in info.Streams)
        {
            StreamAction? action = stream.Kind switch
            {
                StreamKind.Video when stream.AttachedPicture => null, // cover art
                StreamKind.Video => Mp4Video.Contains(stream.Codec) ? StreamAction.Copy : StreamAction.EncodeH264,
                StreamKind.Audio => Mp4Audio.Contains(stream.Codec) ? StreamAction.Copy : StreamAction.EncodeAac,
                StreamKind.Subtitle when TextSubtitles.Contains(stream.Codec) => StreamAction.ToMovText,
                _ => null, // image subtitles, fonts and data streams can't live in MP4
            };
            if (action is { } a) planned.Add(new(stream, a));
        }
        if (!planned.Any(p => p.Stream.Kind is StreamKind.Video or StreamKind.Audio))
            throw new InvalidOperationException("No video or audio stream found in this file.");
        // Video first: some players and thumbnailers only look at the first track (FLV often stores audio first).
        return [.. planned.OrderBy(p => p.Stream.Kind)];
    }

    public static bool IsLossless(IReadOnlyList<PlannedStream> plan) =>
        plan.All(p => p.Action is StreamAction.Copy or StreamAction.ToMovText);

    public static string Describe(IReadOnlyList<PlannedStream> plan) => string.Join(" · ", plan.Select(p => p.Action switch
    {
        StreamAction.Copy => $"{p.Stream.Codec} copied",
        StreamAction.EncodeH264 => $"{p.Stream.Codec} → H.264",
        StreamAction.EncodeAac => $"{p.Stream.Codec} → AAC",
        _ => $"{p.Stream.Codec} subtitles",
    }));

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

    [GeneratedRegex(@"Duration: (\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationLine();

    [GeneratedRegex(@"^\s*Stream #0:(\d+)(?:\[0x[0-9a-f]+\])?(?:\([^)]*\))?: (Video|Audio|Subtitle|Data|Attachment): (\w+)(.*)$", RegexOptions.Multiline)]
    private static partial Regex StreamLine();
}
