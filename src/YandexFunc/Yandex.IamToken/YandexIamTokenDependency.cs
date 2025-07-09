using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PrimeFuncPack;

namespace GGroupp.Yandex.Proxy;

public static class YandexIamTokenDependency
{
    private static readonly TimeSpan ExpirationDeltaDefault
        =
        TimeSpan.FromMinutes(3);

    public static Dependency<HttpMessageHandler> UseYandexIamToken(
        this Dependency<HttpMessageHandler> dependency, string passportOauthTokenConfigurationKey)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        return dependency.Map<HttpMessageHandler>(ResolveHandler);

        YandexIamTokenHttpHandler ResolveHandler(IServiceProvider serviceProvider, HttpMessageHandler innerHandler)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);
            ArgumentNullException.ThrowIfNull(innerHandler);

            var passportOauthToken = serviceProvider.GetRequiredService<IConfiguration>()[passportOauthTokenConfigurationKey.OrEmpty()];
            if (string.IsNullOrWhiteSpace(passportOauthToken))
            {
                throw new InvalidOperationException("YandexPassportOauthToken must be specified.");
            }

            return new(
                innerHandler: innerHandler,
                option: new()
                {
                    PassportOauthToken = passportOauthToken,
                    ExpirationDelta = ExpirationDeltaDefault
                });
        }
    }
}