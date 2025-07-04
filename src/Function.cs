using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yandex.Cloud.Functions;

namespace GGroupp;

public sealed class Function : YcFunction<string, ProxyResponse>
{
    private static readonly HttpClient HttpClient = new();

    private static readonly ProxyService ProxyService = new(HttpClient);

    private static readonly ServiceProvider ServiceProvider = CreateServiceProvider();

    private static readonly ILogger Logger = ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<Function>();

    private static readonly IConfiguration Configuration = ServiceProvider.GetRequiredService<IConfiguration>();

    public ProxyResponse FunctionHandler(string request, Context context)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(context?.TokenJson))
            {
                return ProxyResponse.Error("Invalid IAM token", 500);
            }

            var proxyRequest = ParseProxyRequest(request);
            Logger.LogInformation("Received request: {proxyRequest}", proxyRequest);

            var token = ExtractToken(context.TokenJson);
            Logger.LogInformation("Extracted token: {token}", token[..10]);

            return ProxyService.ForwardRequestAsync(proxyRequest, token).GetAwaiter().GetResult();
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

    private static string ExtractToken(string tokenJson)
    {
        var tokenData = JsonSerializer.Deserialize<TokenData>(tokenJson, JsonSerializerOptions.Web);

        if (tokenData?.AccessToken == null)
            throw new ArgumentException("Invalid IAM token: AccessToken is missing.", nameof(tokenJson));

        return tokenData.AccessToken;
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
            Body = message,
            IsSuccess = false
        };
}

public sealed record class TokenData
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }
}
