using System.Text.RegularExpressions;

namespace HadalStream.Domain.Common;

public static partial class FileName
{
    public static string Sanitize(string name)
    {
        var cleaned = new string([.. name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c)]).Trim('.', ' ');
        if (cleaned.Length > 150) cleaned = cleaned[..150].Trim('.', ' ');
        if (cleaned.Length == 0) return "video";
        return Reserved().IsMatch(cleaned) ? "_" + cleaned : cleaned;
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();
}
