using System;
using GarageGroup.Infra;
using PrimeFuncPack;

namespace GGroupp.Yandex.Proxy;

public static class ProxySendDependency
{
    public static Dependency<IProxySendHandler> UseProxySendHandler(this Dependency<IHttpApi> dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        return dependency.Map<IProxySendHandler>(CreateHandler);

        static ProxySendHandler CreateHandler(IHttpApi httpApi)
        {
            ArgumentNullException.ThrowIfNull(httpApi);
            return new(httpApi);
        }
    }
}