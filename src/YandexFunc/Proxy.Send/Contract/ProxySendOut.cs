namespace GGroupp.Yandex.Proxy;

public sealed record class ProxySendOut
{
    public required bool IsSuccess { get; init; }

    public required int StatusCode { get; init; }

    public object? Body { get; init; }
}