namespace HadalStream;

// Errors go next to the user's videos so they are easy to find and share.
internal static class ErrorLog
{
    public const string FileName = "HadalStream.log";
    private static readonly Lock Gate = new();

    public static string PathIn(string dir) => Path.Combine(dir, FileName);

    // ponytail: append-only, no rotation; add size-based rotation if logs ever grow large.
    public static void Write(string dir, string context, string details)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(PathIn(dir),
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Logging must never take the app down.
        }
    }
}
