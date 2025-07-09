using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;

namespace GGroupp.Yandex.Proxy;

internal sealed partial class YandexIamTokenHttpHandler : DelegatingHandler
{
    private const string BearerScheme = "Bearer";

    private static readonly Uri YandexIamTokenGetUri
        =
        new("https://iam.api.cloud.yandex.net/iam/v1/tokens");

    private static readonly ConcurrentDictionary<string, IamTokenJson> cachedTokens
        =
        new();

    private static readonly SemaphoreSlim Semaphore
        =
        new(1, 1); 

    private readonly YandexIamTokenOption option;

    internal YandexIamTokenHttpHandler(HttpMessageHandler innerHandler, YandexIamTokenOption option)
        : base(innerHandler)
        =>
        this.option = option;

    private string? GetCachedIamToken()
    {
        var oauthToken = option.PassportOauthToken;
        if (cachedTokens.TryGetValue(oauthToken, out var cached) && cached.ExpiresAt > (DateTimeOffset.Now + option.ExpirationDelta))
        {
            return cached.IamToken;
        }

        return null;
    }

    private void SaveTokenIntoCache(IamTokenJson iamToken)
        =>
        _ = cachedTokens.AddOrUpdate(option.PassportOauthToken, iamToken, (_, _) => iamToken);

    private sealed record class RequestJson
    {
        public required string YandexPassportOauthToken { get; init; }
    }

    private readonly record struct IamTokenJson
    {
        public string? IamToken { get; init; }

        public DateTimeOffset ExpiresAt { get; init; }
    }
}