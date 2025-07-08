using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yandex.Cloud.Functions;

namespace GGroupp;

public sealed class Function : YcFunction<string, Task<ProxyResponse>>
{
    private static readonly HttpClient HttpClient = new();

    private static readonly ProxyService ProxyService = new(HttpClient);

    private static readonly TokenService TokenService = new(HttpClient);

    private static readonly ServiceProvider ServiceProvider = CreateServiceProvider();

    private static readonly ILogger Logger = ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<Function>();

    private static readonly IConfiguration Configuration = ServiceProvider.GetRequiredService<IConfiguration>();

    public async Task<ProxyResponse> FunctionHandler(string request, Context context)
    {
        try
        {
            var proxyRequest = ParseProxyRequest(request);

            var token = await TokenService.GetValidTokenAsync(Configuration, Logger);

            return await ProxyService.ForwardRequestAsync(proxyRequest, token);
        }
        catch (ArgumentException e)
        {
            return ProxyResponse.Error(e.Message, 400);
        }
        catch (Exception e)
        {
            return ProxyResponse.Error($"Request processing failed: {e.Message}");
        }
    }

    private static ProxyRequest ParseProxyRequest(string requestString)
    {
        if (string.IsNullOrWhiteSpace(requestString))
        {
            throw new ArgumentNullException(nameof(requestString), "Request payload cannot be empty.");
        }

        using var jsonDoc = JsonDocument.Parse(requestString);
        var root = jsonDoc.RootElement;

        ProxyRequest? proxyRequest;

        if (root.TryGetProperty("body", out var bodyElement))
        {
            var bodyString = bodyElement.GetString();
            if (string.IsNullOrWhiteSpace(bodyString))
            {
                throw new ArgumentException("Request body is empty.", nameof(requestString));
            }
            proxyRequest = JsonSerializer.Deserialize<ProxyRequest>(bodyString, JsonSerializerOptions.Web);
        }
        else
        {
            proxyRequest = JsonSerializer.Deserialize<ProxyRequest>(requestString, JsonSerializerOptions.Web);
        }

        if (proxyRequest is null || string.IsNullOrWhiteSpace(proxyRequest.Url) || string.IsNullOrWhiteSpace(proxyRequest.Method))
        {
            throw new ArgumentException("Request Method and Url are required.", nameof(requestString));
        }

        return proxyRequest;
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection()
            .AddLogging(InnerConfigureLogger)
            .AddSingleton(BuildConfiguration());

        return services.BuildServiceProvider();

        static void InnerConfigureLogger(ILoggingBuilder builder)
        {
            builder = builder.AddConsole();
        }
    }

    private static IConfiguration BuildConfiguration()
        =>
        new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build();
}

internal sealed class TokenService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    private static string? _cachedToken;

    private static DateTime _tokenExpiresAt;

    public async Task<string> GetValidTokenAsync(IConfiguration configuration, ILogger logger)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAt.AddMinutes(-5))
        {
            return _cachedToken;
        }

        await _semaphore.WaitAsync();
        try
        {
            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAt.AddMinutes(-5))
            {
                return _cachedToken;
            }


            var oauthToken = configuration["YANDEX_OAUTH_TOKEN"];
            if (string.IsNullOrWhiteSpace(oauthToken))
            {
                throw new ArgumentException("YANDEX_OAUTH_TOKEN environment variable is not set or empty.");
            }

            logger.LogInformation("Requesting new token");
            var iamTokenResponse = await RequestIamTokenAsync(oauthToken);


            _cachedToken = iamTokenResponse.IamToken;
            _tokenExpiresAt = iamTokenResponse.ExpiresAt;

            return iamTokenResponse.IamToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<IamTokenResponse> RequestIamTokenAsync(string oauthToken)
    {
        var request = new IamTokenRequest { YandexPassportOauthToken = oauthToken };
        var json = JsonSerializer.Serialize(request, JsonSerializerOptions.Web);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://iam.api.cloud.yandex.net/iam/v1/tokens")
        {
            Content = content
        };

        using var httpResponse = await _httpClient.SendAsync(httpRequest);

        var responseBody = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new ArgumentException("OAuth token used in configuration is invalid or expired.");
        }

        var iamTokenResponse = JsonSerializer.Deserialize<IamTokenResponse>(responseBody, JsonSerializerOptions.Web);

        if (iamTokenResponse?.IamToken == null)
        {
            throw new ArgumentException("Invalid IAM token response: IamToken is missing.");
        }

        return iamTokenResponse;
    }
}

internal sealed class ProxyService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<ProxyResponse> ForwardRequestAsync(ProxyRequest request, string token)
    {
        try
        {
            using var httpRequest = BuildHttpRequest(request, token);
            using var httpResponse = await _httpClient.SendAsync(httpRequest);

            var responseBody = await httpResponse.Content.ReadAsStringAsync();

            return new ProxyResponse
            {
                StatusCode = (int)httpResponse.StatusCode,
                Body = responseBody,
                IsSuccess = httpResponse.IsSuccessStatusCode
            };
        }
        catch (HttpRequestException e)
        {
            return ProxyResponse.Error($"HTTP request failed: {e.Message}");
        }
        catch (Exception e)
        {
            return ProxyResponse.Error($"Request processing failed: {e.Message}");
        }
    }

    private static HttpRequestMessage BuildHttpRequest(ProxyRequest request, string token)
    {
        if (string.IsNullOrWhiteSpace(request?.Method) || string.IsNullOrWhiteSpace(request?.Url))
        {
            throw new ArgumentException("Request Method and Url are required.");
        }
        var httpRequest = new HttpRequestMessage(new(request.Method), request.Url);

        AddContent(httpRequest, request);
        AddHeaders(httpRequest, request);
        AddAuthHeader(httpRequest, token);

        return httpRequest;
    }

    private static void AddContent(HttpRequestMessage httpRequest, ProxyRequest request)
    {
        if (string.IsNullOrEmpty(request.Body))
        {
            return;
        }

        var content = new StringContent(request.Body, Encoding.UTF8);
        if (request.Headers?.TryGetValue("Content-Type", out var contentType) == true)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        httpRequest.Content = content;
    }

    private static void AddHeaders(HttpRequestMessage httpRequest, ProxyRequest request)
    {
        if (request.Headers is null)
        {
            return;
        }

        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static void AddAuthHeader(HttpRequestMessage httpRequest, string token)
    {
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

public sealed record class ProxyRequest
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }
}

public sealed record class ProxyResponse
{
    public int StatusCode { get; init; }

    public string? Body { get; init; }

    public bool IsSuccess { get; init; }

    public static ProxyResponse Error(string message, int statusCode = 500)
        => new()
        {
            StatusCode = statusCode,
            Body = JsonSerializer.Serialize(new { error = message }, JsonSerializerOptions.Web),
            IsSuccess = false
        };
}

public sealed record class IamTokenRequest
{
    [JsonPropertyName("yandexPassportOauthToken")]
    public string YandexPassportOauthToken { get; init; } = string.Empty;
}

public sealed record class IamTokenResponse
{
    [JsonPropertyName("iamToken")]
    public string IamToken { get; init; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; init; }
}
