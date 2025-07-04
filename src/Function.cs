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

public class Function : YcFunction<ProxyRequest, ProxyResponse>
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ProxyService ProxyService = new(HttpClient);

    public ProxyResponse FunctionHandler(ProxyRequest request, Context context)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request?.Url) || string.IsNullOrWhiteSpace(request?.Method))
            {
                return ProxyResponse.Error("Invalid request parameters", 400);
            }
            if (string.IsNullOrWhiteSpace(context?.TokenJson))
            {
                return ProxyResponse.Error("Invalid IAM token", 500);
            }

            using var serviceProvider = CreateServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<Function>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();

            var token = ExtractToken(context.TokenJson);
            logger.LogInformation("Extracted token: {token}", token[..10]);
            return ProxyService.ForwardRequestAsync(request, token).Result;
        }
        catch (Exception ex)
        {
            return ProxyResponse.Error($"Request processing failed: {ex.Message}");
        }
    }

    private static string ExtractToken(string tokenJson)
    {
        var tokenData = JsonSerializer.Deserialize<TokenData>(tokenJson, JsonSerializerOptions.Default);

        if (tokenData?.AccessToken == null)
            throw new InvalidOperationException("Invalid IAM token received");

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
        catch (HttpRequestException ex)
        {
            return ProxyResponse.Error($"HTTP request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ProxyResponse.Error($"Request processing failed: {ex.Message}");
        }
    }

    private static HttpRequestMessage BuildHttpRequest(ProxyRequest request, string token)
    {
        var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);

        AddContent(httpRequest, request);
        AddHeaders(httpRequest, request.Headers);
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

    private static void AddHeaders(HttpRequestMessage httpRequest, Dictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var header in headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
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
    public string Url { get; init; } = string.Empty;

    public string Method { get; init; } = "GET";

    public string? Body { get; init; }

    public Dictionary<string, string>? Headers { get; init; }
}

public sealed record class ProxyResponse
{
    public int StatusCode { get; init; }

    public string Body { get; init; } = string.Empty;

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
