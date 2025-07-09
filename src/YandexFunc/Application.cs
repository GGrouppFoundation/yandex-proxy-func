using GarageGroup.Infra;
using GGroupp.Infra;
using PrimeFuncPack;

namespace GGroupp.Yandex.Proxy;

public static class Application
{
    [YandexHttpFuncton("ProxyFunction")]
    public static Dependency<IProxySendHandler> UseProxySendHandler()
        =>
        PrimaryHandler.UseStandardSocketsHttpHandler()
        .UseLogging("ProxyApi")
        .UseYandexIamToken("YANDEX_OAUTH_TOKEN")
        .UsePollyStandard()
        .UseHttpApi()
        .UseProxySendHandler();
}