using HadalStream.Application.Abstractions;
using MediatR;

namespace HadalStream.Application.Behaviors;

// Unexpected exceptions from any handler are logged once here, then bubble up to the caller.
internal sealed class ErrorLoggingBehavior<TRequest, TResponse>(IErrorLog log) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (Exception e)
        {
            log.Write($"{typeof(TRequest).Name} failed", e.ToString());
            throw;
        }
    }
}
