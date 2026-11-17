using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace UniversalTables.Services;
public static class Part4Registration
{
    public static IServiceCollection AddUniversalTableForms(this IServiceCollection services)
    {
        services.TryAddScoped<DraftFactory>();
        return services;
    }
}