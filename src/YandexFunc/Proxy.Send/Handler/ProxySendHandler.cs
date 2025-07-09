using System;
using System.Text.Json;
using GarageGroup.Infra;

namespace GGroupp.Yandex.Proxy;

internal sealed partial class ProxySendHandler(IHttpApi httpApi) : IProxySendHandler
{
    private const int BaseSuccessStatusCode = 200;

    private static Result<ProxySendIn, Failure<HandlerFailureCode>> Validate(ProxySendIn? input)
    {
        if (string.IsNullOrWhiteSpace(input?.Url))
        {
            return Failure.Create(HandlerFailureCode.Persistent, "Input Url must be specified.");
        }

        if (string.IsNullOrWhiteSpace(input.Method))
        {
            return Failure.Create(HandlerFailureCode.Persistent, "Input Method must be specified.");
        }

        return Result.Success(input);
    }

    private static object? DeserializeBody(HttpBody body)
    {
        if (body.Content is null)
        {
            return null;
        }

        if (body.Type.IsJsonMediaType(false))
        {
            return body.DeserializeFromJson<JsonElement?>();
        }

        return body.Content.ToString();
    }
}