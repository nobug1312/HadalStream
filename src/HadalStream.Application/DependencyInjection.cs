using HadalStream.Application.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace HadalStream.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) => services.AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
        config.AddOpenBehavior(typeof(ErrorLoggingBehavior<,>));
    });
}
