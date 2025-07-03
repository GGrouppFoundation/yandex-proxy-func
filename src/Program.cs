using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Yandex.Cloud.Functions;

namespace GGroupp;

public class Handler : YcFunction<ProxyRequest, ProxyResponse>
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private static readonly ProxyService ProxyService = new ProxyService(HttpClient);

    public ProxyResponse FunctionHandler(ProxyRequest request, Context context)
    {
        try
        {
            var token = ExtractToken(context.TokenJson);
            return ProxyService.ForwardRequestAsync(request, token).Result;
        }
        catch (Exception ex)
        {
            return ProxyResponse.Error(ex.Message);
        }
    }

    private static string ExtractToken(string tokenJson)
    {
        var tokenData = JsonSerializer.Deserialize<TokenData>(tokenJson);

        if (tokenData?.access_token == null)
            throw new InvalidOperationException("Invalid IAM token received");

        return tokenData.access_token;
    }
}

public class ProxyService
{
    private readonly HttpClient _httpClient;

    public ProxyService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

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
                Body = ParseResponseBody(responseBody),
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

    private static object ParseResponseBody(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return new { };

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return JsonSerializer.Deserialize<object>(document.RootElement.GetRawText()) ?? new { };
        }
        catch (JsonException)
        {
            return responseBody;
        }
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
    public object Body { get; set; } = new { };
    public bool IsSuccess { get; set; }

    public static ProxyResponse Error(string message, int statusCode = 500)
    {
        return new ProxyResponse
        {
            StatusCode = statusCode,
            Body = new { error = message },
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
