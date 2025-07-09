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
        request.Headers.Authorization = new(BearerScheme, iamToken);

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetIamTokenAsync(CancellationToken cancellationToken)
    {
        var cachedToken = GetCachedIamToken();
        if (string.IsNullOrEmpty(cachedToken) is false)
        {
            return cachedToken;
        }

        await Semaphore.WaitAsync(cancellationToken);

        try
        {
            cachedToken = GetCachedIamToken();
            if (string.IsNullOrEmpty(cachedToken) is false)
            {
                return cachedToken;
            }

            var content = new RequestJson
            {
                YandexPassportOauthToken = option.PassportOauthToken
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
            SaveTokenIntoCache(token);

            return token.IamToken.OrEmpty();
        }
        finally
        {
            Semaphore.Release();
        }
    }
}