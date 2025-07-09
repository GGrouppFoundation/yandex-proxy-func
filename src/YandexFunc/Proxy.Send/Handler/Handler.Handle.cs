using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GarageGroup.Infra;

namespace GGroupp.Yandex.Proxy;

partial class ProxySendHandler
{
    public ValueTask<Result<ProxySendOut, Failure<HandlerFailureCode>>> HandleAsync(
        ProxySendIn? input, CancellationToken cancellationToken)
        =>
        AsyncPipeline.Pipe(
            input, cancellationToken)
        .Pipe(
            Validate)
        .MapSuccess(
            SendProxyAsync);

    private Task<ProxySendOut> SendProxyAsync(
        ProxySendIn input, CancellationToken cancellationToken)
        =>
        AsyncPipeline.Pipe(
            input, cancellationToken)
        .Pipe(
            static @in => new HttpSendIn(
                method: HttpVerb.From(@in.Method.OrEmpty()),
                requestUri: @in.Url.OrEmpty())
            {
                Headers = @in.Headers.ToFlatArray(),
                Body = @in.Body is null ? default : HttpBody.SerializeAsJson(@in.Body.Value)
            })
        .PipeValue(
            httpApi.SendAsync)
        .Fold(
            static success => new ProxySendOut
            {
                IsSuccess = true,
                StatusCode = BaseSuccessStatusCode + (int)success.StatusCode,
                Body = success.Body.DeserializeFromJson<JsonElement?>()
            },
            static failure => new ProxySendOut
            {
                IsSuccess = false,
                StatusCode = (int)failure.StatusCode,
                Body = DeserializeBody(failure.Body)
            });
}