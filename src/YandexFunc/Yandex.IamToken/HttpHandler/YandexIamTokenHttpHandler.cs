using System;
using System.Collections.Concurrent;
using System.Net.Http;

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

    private readonly YandexIamTokenOption option;

    internal YandexIamTokenHttpHandler(HttpMessageHandler innerHandler, YandexIamTokenOption option)
        : base(innerHandler)
        =>
        this.option = option;

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