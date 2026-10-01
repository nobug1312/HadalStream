using HadalStream.Application.Abstractions;

namespace HadalStream.Infrastructure.Logging;

internal sealed class FileErrorLog(ISettingsStore settings) : IErrorLog
{
    private readonly Lock gate = new();

    public string FileIn(string folder) => Path.Combine(folder, "HadalStream.log");

    // ponytail: append-only, no rotation; add size-based rotation if logs ever grow large.
    public void Write(string context, string details, string? folder = null)
    {
        try
        {
            folder ??= settings.Current.DownloadDir;
            lock (gate)
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(FileIn(folder),
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Logging must never take the app down.
        }
    }
}
