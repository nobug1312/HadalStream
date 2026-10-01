namespace HadalStream.Infrastructure;

internal static class FilePaths
{
    public static string Unique(string dir, string baseName, string extension)
    {
        var path = Path.Combine(dir, baseName + extension);
        for (var n = 1; File.Exists(path); n++) path = Path.Combine(dir, $"{baseName} ({n}){extension}");
        return path;
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
