using System.Diagnostics;
using HadalStream.Application.Abstractions;
using HadalStream.Domain.Common;

namespace HadalStream.Infrastructure.Shell;

internal sealed class WindowsShell : IShell
{
    private static readonly Error Missing = new("File not found.");

    public Result Open(string path) => Start(path, new ProcessStartInfo(path) { UseShellExecute = true });

    public Result Reveal(string path) => Start(path, new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""));

    private static Result Start(string path, ProcessStartInfo start)
    {
        if (!File.Exists(path)) return Missing;
        Process.Start(start)?.Dispose();
        return Result.Success();
    }
}
