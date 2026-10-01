using HadalStream.Application.Abstractions;
using HadalStream.Infrastructure.Conversions;
using HadalStream.Infrastructure.Downloads;
using HadalStream.Infrastructure.Http;
using HadalStream.Infrastructure.Logging;
using HadalStream.Infrastructure.Persistence;
using HadalStream.Infrastructure.Shell;
using HadalStream.Infrastructure.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace HadalStream.Infrastructure;

public sealed record AppPaths(string DataDir, string Ffmpeg);

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, AppPaths paths) => services
        .AddSingleton(paths)
        .AddSingleton<ISettingsStore, JsonSettingsStore>()
        .AddSingleton<IErrorLog, FileErrorLog>()
        .AddSingleton<IShell, WindowsShell>()
        .AddSingleton<HttpClientProvider>()
        .AddSingleton<IWebPageReader>(provider => provider.GetRequiredService<HttpClientProvider>())
        .AddSingleton<IVideoSource, AbyssSource>()
        .AddSingleton<IVideoSource, DoodSource>()
        .AddSingleton<IDownloadQueue, DownloadQueue>()
        .AddSingleton<IConversionQueue, ConversionQueue>();
}
