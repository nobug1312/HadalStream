using HadalStream.Domain.Common;

namespace HadalStream.Domain.Conversions;

public enum StreamKind { Video, Audio, Subtitle, Other }

public enum StreamAction { Copy, EncodeH264, EncodeAac, ToMovText }

public sealed record MediaStream(int Index, StreamKind Kind, string Codec, bool AttachedPicture = false);

public sealed record MediaInfo(TimeSpan? Duration, IReadOnlyList<MediaStream> Streams);

public sealed record PlannedStream(MediaStream Stream, StreamAction Action);

// Which streams MP4 can carry untouched (lossless remux) and which must be re-encoded.
public static class Mp4Plan
{
    private static readonly HashSet<string> Mp4Video = ["h264", "hevc", "av1", "vp9", "mpeg4", "mpeg2video", "mpeg1video"];
    private static readonly HashSet<string> Mp4Audio = ["aac", "mp3", "ac3", "eac3", "opus", "flac", "alac"];
    private static readonly HashSet<string> TextSubtitles = ["subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text"];

    public static bool Accepts(string path) => Path.GetExtension(path).ToLowerInvariant() is ".flv" or ".mov" or ".mkv";

    public static Result<PlannedStream[]> For(MediaInfo info)
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
        if (!planned.Any(p => p.Stream.Kind is StreamKind.Video or StreamKind.Audio)) return ConversionErrors.NoMedia;
        // Video first: some players and thumbnailers only look at the first track (FLV often stores audio first).
        return planned.OrderBy(p => p.Stream.Kind).ToArray();
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
}
