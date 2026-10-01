using HadalStream.Domain.Common;
using HadalStream.Domain.Settings;

namespace HadalStream.Application.Abstractions;

public interface ISettingsStore
{
    AppSettings Current { get; }

    Result Save(AppSettings settings);
}

// Errors go next to the user's videos so they are easy to find and share.
public interface IErrorLog
{
    string FileIn(string folder);

    // Writes into the download folder unless another folder is given. Never throws.
    void Write(string context, string details, string? folder = null);
}

public interface IShell
{
    Result Open(string path);

    Result Reveal(string path);
}
