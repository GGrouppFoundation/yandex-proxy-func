using System.Collections.Generic;
using System.Text.Json;

namespace GGroupp.Yandex.Proxy;

public sealed record class ProxySendIn
{
    public string? Url { get; init; }

    public string? Method { get; init; }

    public JsonElement? Body { get; init; }

    public Dictionary<string, string>? Headers { get; init; }
}