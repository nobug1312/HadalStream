using System.Diagnostics;
using HadalStream.Application.Abstractions;
using HadalStream.Domain.Conversions;
using HadalStream.Infrastructure;
using HadalStream.Infrastructure.Conversions;
using HadalStream.Infrastructure.Logging;
using HadalStream.Infrastructure.Persistence;

namespace HadalStream.Tests;

public class ConversionTests
{
    // Real `ffmpeg -i` output captured from FFmpeg 9.0.2.
    private const string MkvProbe = """
        Input #0, matroska,webm, from 'b.mkv':
          Duration: 00:01:04.50, start: 0.000000, bitrate: 410 kb/s
          Stream #0:0: Video: h264 (High), yuv420p(tv, progressive), 320x240 [SAR 1:1 DAR 4:3], 25 fps, 25 tbr, 1k tbn
              DURATION        : 00:00:04.000000000
          Stream #0:1(jpn): Audio: flac, 44100 Hz, mono, s16
          Stream #0:2(eng): Subtitle: subrip (srt)
          Stream #0:3: Subtitle: hdmv_pgs_subtitle, 1920x1080
          Stream #0:4: Attachment: ttf
          Stream #0:5: Video: mjpeg (Baseline), yuvj420p(pc), 600x800, 90k tbr, 90k tbn (attached pic)
        """;

    private const string MovProbe = """
          Duration: 00:00:04.00, start: 0.000000, bitrate: 8263 kb/s
          Stream #0:0[0x1]: Video: prores (HQ) (apch / 0x68637061), yuv422p10le(tv, progressive), 320x240, 25 fps (default)
          Stream #0:1[0x2](und): Audio: pcm_s16le (sowt / 0x74776F73), 44100 Hz, mono, s16, 705 kb/s (default)
        """;

    [Fact]
    public void ParseProbe_ReadsDurationAndStreams()
    {
        var info = Ffmpeg.ParseProbe(MkvProbe);

        Assert.Equal(TimeSpan.FromSeconds(64.5), info.Duration);
        Assert.Equal(
            new[] { "0:Video:h264", "1:Audio:flac", "2:Subtitle:subrip", "3:Subtitle:hdmv_pgs_subtitle", "4:Other:ttf", "5:Video:mjpeg" },
            info.Streams.Select(s => $"{s.Index}:{s.Kind}:{s.Codec}"));
        Assert.True(info.Streams[5].AttachedPicture);
    }

    [Fact]
    public void Plan_CopiesMp4CompatibleStreamsAndDropsTheRest()
    {
        var plan = Mp4Plan.For(Ffmpeg.ParseProbe(MkvProbe)).Value;

        Assert.Equal(
            new[] { (0, StreamAction.Copy), (1, StreamAction.Copy), (2, StreamAction.ToMovText) },
            plan.Select(p => (p.Stream.Index, p.Action)));
        Assert.True(Mp4Plan.IsLossless(plan));
    }

    [Fact]
    public void Plan_ReencodesOnlyWhatMp4CannotHold()
    {
        var plan = Mp4Plan.For(Ffmpeg.ParseProbe(MovProbe)).Value;

        Assert.Equal(new[] { StreamAction.EncodeH264, StreamAction.EncodeAac }, plan.Select(p => p.Action));
        Assert.False(Mp4Plan.IsLossless(plan));
        Assert.Equal("prores → H.264 · pcm_s16le → AAC", Mp4Plan.Describe(plan));
    }

    [Fact]
    public void Plan_RejectsFilesWithoutAudioOrVideo() =>
        Assert.Equal(ConversionErrors.NoMedia, Mp4Plan.For(Ffmpeg.ParseProbe("  Stream #0:0: Subtitle: subrip")).Error);

    [Fact]
    public void Arguments_PutVideoFirstMapByOutputIndexAndTagHevc()
    {
        var plan = Mp4Plan.For(new MediaInfo(null, [new(3, StreamKind.Audio, "opus"), new(7, StreamKind.Video, "hevc")])).Value;

        var args = string.Join(' ', Ffmpeg.Arguments("in.mkv", "out.part", plan));

        Assert.Contains("-map 0:7 -c:0 copy -tag:0 hvc1 -map 0:3 -c:1 copy", args);
        Assert.EndsWith("-f mp4 out.part", args);
    }

    // Drives the bundled ffmpeg end to end on generated FLV / MKV / MOV samples.
    [Theory]
    [InlineData("flv", new[] { "-c:v", "flv1", "-c:a", "libmp3lame" }, false, "h264,mp3")]
    [InlineData("mkv", new[] { "-c:v", "libx264", "-c:a", "flac" }, true, "h264,flac")]
    [InlineData("mov", new[] { "-c:v", "libx264", "-c:a", "pcm_s16le" }, false, "h264,aac")]
    public async Task Converter_ProducesPlayableMp4(string extension, string[] codecArgs, bool lossless, string expectedCodecs)
    {
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) return;
        var dir = Directory.CreateTempSubdirectory().FullName;
        var source = Path.Combine(dir, $"sample.{extension}");
        Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=2",
            "-f", "lavfi", "-i", "sine=duration=2", .. codecArgs, source]);

        var paths = new AppPaths(dir, ffmpeg);
        var queue = new ConversionQueue(paths, new FileErrorLog(new JsonSettingsStore(paths)));
        var done = new TaskCompletionSource<ConversionView>();
        queue.Changed += v =>
        {
            if (v.Status is ConversionStatus.Completed or ConversionStatus.Failed) done.TrySetResult(v);
        };
        queue.Add([source]);
        var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(ConversionStatus.Completed, result.Status);
        Assert.Equal(lossless, result.Lossless);
        Assert.Equal(Path.Combine(dir, "sample.mp4"), result.Output);
        var codecs = Ffmpeg.ParseProbe(Run(ffmpeg, ["-hide_banner", "-i", result.Output!])).Streams.Select(s => s.Codec);
        Assert.Equal(expectedCodecs, string.Join(',', codecs));
        Assert.Empty(Directory.GetFiles(dir, "*.part"));
    }

    private static string Run(string exe, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return stderr;
    }
}
