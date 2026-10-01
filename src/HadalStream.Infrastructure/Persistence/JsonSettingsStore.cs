using HadalStream.Application.Abstractions;
using HadalStream.Domain.Common;
using HadalStream.Domain.Settings;

namespace HadalStream.Infrastructure.Persistence;

internal sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string file;

    public JsonSettingsStore(AppPaths paths)
    {
        file = Path.Combine(paths.DataDir, "settings.json");
        Current = JsonFile.Load<AppSettings>(file) is { } loaded && loaded.Validate().IsSuccess ? loaded : new();
    }

    public AppSettings Current { get; private set; }

    public Result Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(settings.DownloadDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Error("Can't create that folder.");
        }
        JsonFile.Save(file, settings);
        Current = settings;
        return Result.Success();
    }
}
