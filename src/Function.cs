using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
            using var serviceProvider = CreateServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<Function>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();

            var token = ExtractToken(context.TokenJson);
            logger.LogInformation("Extracted token: {token}", token[..10]);
            return ProxyService.ForwardRequestAsync(request, token).Result;
        }
        catch (Exception ex)
        {
            return ProxyResponse.Error(ex.Message);
        }
    }

    private static string ExtractToken(string tokenJson)
    {
        var tokenData = JsonConvert.DeserializeObject<TokenData>(tokenJson);

        if (tokenData?.access_token == null)
            throw new InvalidOperationException("Invalid IAM token received");

        return tokenData.access_token;
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

public class ProxyService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<ProxyResponse> ForwardRequestAsync(ProxyRequest request, string token)
    {
        if (!IsValidRequest(request))
            return ProxyResponse.Error("Invalid request parameters", 400);

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

    private static bool IsValidRequest(ProxyRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Url) &&
               !string.IsNullOrWhiteSpace(request.Method);
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
            return;

        var content = new StringContent(request.Body, Encoding.UTF8);

        if (request.Headers?.TryGetValue("Content-Type", out var contentType) == true)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        httpRequest.Content = content;
    }

    private static void AddHeaders(HttpRequestMessage httpRequest, Dictionary<string, string>? headers)
    {
        if (headers == null)
            return;

        foreach (var header in headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;

            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static void AddAuthHeader(HttpRequestMessage httpRequest, string token)
    {
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

public class ProxyRequest
{
    public string Url { get; set; } = string.Empty;

    public string Method { get; set; } = "GET";

    public string? Body { get; set; }

    public Dictionary<string, string>? Headers { get; set; }
}

public class ProxyResponse
{
    public int StatusCode { get; set; }

    public string Body { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public static ProxyResponse Error(string message, int statusCode = 500)
    {
        return new ProxyResponse
        {
            StatusCode = statusCode,
            Body = message,
            IsSuccess = false
        };
    }
}

public class TokenData
{
    public string? access_token { get; set; }

    public int expires_in { get; set; }

    public string? token_type { get; set; }
}
