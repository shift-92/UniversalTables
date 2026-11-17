using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UniversalTables.Configuration;

namespace UniversalTables.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalTables(this IServiceCollection services, Action<ModelRegistry> register, Action<TableOptions>? configure = null)
    {
        var registry = new ModelRegistry(); register(registry);
        services.AddSingleton(registry);
        var options = new TableOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped<CommandDispatcher>();
        services.TryAddScoped<ITableDefaultsProvider, EmptyTableDefaultsProvider>();
        return services;
    }
}