using System.Reflection;
using GraduateRecords.Application.Common.Messaging;

namespace GraduateRecords.Web.Infrastructure;

/// <summary>
/// AGENTS §4: handlers are plain classes registered by assembly scan and injected
/// into controllers by their interface. No mediator or pipeline framework exists.
/// </summary>
public static class QueryHandlerRegistration
{
    public static IServiceCollection AddQueryHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var implementation in assemblies
                     .SelectMany(static assembly => assembly.GetTypes())
                     .Where(static type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }))
        {
            var handlerServices = implementation.GetInterfaces()
                .Where(static iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
                .ToList();

            foreach (var handlerService in handlerServices)
            {
                services.AddScoped(handlerService, implementation);
            }
        }

        return services;
    }
}
