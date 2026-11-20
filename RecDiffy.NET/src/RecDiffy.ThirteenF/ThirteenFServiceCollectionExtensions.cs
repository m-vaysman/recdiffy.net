using Microsoft.Extensions.DependencyInjection;

namespace RecDiffy.ThirteenF;

public static class ThirteenFServiceCollectionExtensions
{
    public static IServiceCollection AddThirteenF(this IServiceCollection services, string userAgentContact)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgentContact);

        var service = new SecThirteenFService(SecSharedHttp.Client, userAgentContact);
        services.AddSingleton(service);
        services.AddSingleton<IThirteenFService>(service);
        return services;
    }
}
