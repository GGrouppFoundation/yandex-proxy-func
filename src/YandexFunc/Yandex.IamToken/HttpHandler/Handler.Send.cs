using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GGroupp.Yandex.Proxy;

partial class YandexIamTokenHttpHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var iamToken = await GetIamTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(iamToken) is false)
        {
            request.Headers.Authorization = new(BearerScheme, iamToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string?> GetIamTokenAsync(CancellationToken cancellationToken)
    {
        var oauthToken = option.PassportOauthToken;
        if (cachedTokens.TryGetValue(oauthToken, out var cached) && cached.ExpiresAt > (DateTimeOffset.Now + option.ExpirationDelta))
        {
            return cached.IamToken;
        }

        var content = new RequestJson
        {
            YandexPassportOauthToken = oauthToken
        };

        using var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = YandexIamTokenGetUri,
            Content = JsonContent.Create(content, default, JsonSerializerOptions.Web)
        };

        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode is false)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to get iamToken. Status: '{response.StatusCode}'. Body: '{body}'.");
        }

        var token = await response.Content.ReadFromJsonAsync<IamTokenJson>(JsonSerializerOptions.Web, cancellationToken);
        cachedTokens.AddOrUpdate(oauthToken, token, (_, _) => token);

        return token.IamToken;
    }
}