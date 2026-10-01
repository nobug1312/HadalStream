using HadalStream.Application.Abstractions;
using HadalStream.Domain.Common;
using HadalStream.Domain.Conversions;
using MediatR;

namespace HadalStream.Application.Conversions;

public sealed record ListConversions : IRequest<Result<ConversionView[]>>;

// Paths come from the native file dialog or WebView2 drop objects, never from page script.
public sealed record AddConversions(string[] Paths) : IRequest<Result<ConversionView[]>>;

public sealed record CancelConversion(string Id) : IRequest<Result>;

public sealed record RetryConversion(string Id) : IRequest<Result>;

public sealed record RemoveConversion(string Id) : IRequest<Result>;

public sealed record OpenConversion(string Id) : IRequest<Result>;

public sealed record RevealConversion(string Id) : IRequest<Result>;

internal sealed class ConversionHandlers(IConversionQueue queue, IShell shell) :
    IRequestHandler<ListConversions, Result<ConversionView[]>>,
    IRequestHandler<AddConversions, Result<ConversionView[]>>,
    IRequestHandler<CancelConversion, Result>,
    IRequestHandler<RetryConversion, Result>,
    IRequestHandler<RemoveConversion, Result>,
    IRequestHandler<OpenConversion, Result>,
    IRequestHandler<RevealConversion, Result>
{
    public Task<Result<ConversionView[]>> Handle(ListConversions request, CancellationToken ct) => Views(queue.List());

    public Task<Result<ConversionView[]>> Handle(AddConversions request, CancellationToken ct) =>
        Views(request.Paths.Length > 0 && !request.Paths.Any(Mp4Plan.Accepts)
            ? ConversionErrors.Unsupported
            : queue.Add(request.Paths));

    public Task<Result> Handle(CancelConversion request, CancellationToken ct) => Done(queue.Cancel(request.Id));

    public Task<Result> Handle(RetryConversion request, CancellationToken ct) => Done(queue.Retry(request.Id));

    public Task<Result> Handle(RemoveConversion request, CancellationToken ct) => Done(queue.Remove(request.Id));

    public Task<Result> Handle(OpenConversion request, CancellationToken ct) => Done(queue.Output(request.Id).Bind(shell.Open));

    public Task<Result> Handle(RevealConversion request, CancellationToken ct) => Done(queue.Output(request.Id).Bind(shell.Reveal));

    private static Task<Result<ConversionView[]>> Views(Result<ConversionView[]> result) => Task.FromResult(result);

    private static Task<Result> Done(Result result) => Task.FromResult(result);
}
