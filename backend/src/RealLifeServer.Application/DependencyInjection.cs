using Microsoft.Extensions.DependencyInjection;
using RealLifeServer.Application.Streams.Services;
using RealLifeServer.Application.Streams.StateMachine;

namespace RealLifeServer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddSingleton<SceneStateMachine>();
        services.AddScoped<SceneTransitionService>();

        return services;
    }
}
