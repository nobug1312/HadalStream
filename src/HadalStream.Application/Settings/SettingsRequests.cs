using HadalStream.Application.Abstractions;
using HadalStream.Domain.Common;
using HadalStream.Domain.Settings;
using MediatR;

namespace HadalStream.Application.Settings;

public sealed record GetSettings : IRequest<Result<AppSettings>>;

public sealed record SaveSettings(AppSettings Settings) : IRequest<Result<AppSettings>>;

internal sealed class SettingsHandlers(ISettingsStore store) :
    IRequestHandler<GetSettings, Result<AppSettings>>,
    IRequestHandler<SaveSettings, Result<AppSettings>>
{
    public Task<Result<AppSettings>> Handle(GetSettings request, CancellationToken ct) => Task.FromResult<Result<AppSettings>>(store.Current);

    public Task<Result<AppSettings>> Handle(SaveSettings request, CancellationToken ct)
    {
        var saved = request.Settings.Validate();
        if (saved.IsSuccess) saved = store.Save(request.Settings);
        return Task.FromResult<Result<AppSettings>>(saved.Error is { } error ? error : store.Current);
    }
}
