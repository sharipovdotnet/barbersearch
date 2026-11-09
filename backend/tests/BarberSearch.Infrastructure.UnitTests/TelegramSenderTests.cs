using System.Net;
using BarberSearch.Application.Notifications;
using BarberSearch.Contracts.Common;
using BarberSearch.Infrastructure.Telegram;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Проверяет контракт <see cref="ITelegramSender"/> у реализации Infrastructure.Telegram:
/// без токена HTTP-запрос не выполняется, а <c>400</c>/<c>403</c> отделяются от временных
/// ошибок.
/// </summary>
/// <remarks>
/// Настоящий Bot API не вызывается: <see cref="IHttpClientFactory"/> подменён, поэтому
/// тест не зависит от сети и не может отправить сообщение.
/// </remarks>
public class TelegramSenderTests
{
    [Fact]
    public async Task Unconfigured_token_never_calls_http_client()
    {
        var factory = new StubHttpClientFactory(() => throw new InvalidOperationException("HTTP must not be called."));
        var sender = CreateSender(new TelegramOptions(), factory);

        var result = await sender.SendMessageAsync(42, "text");

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Notification.TelegramSendFailed,
            result.Error.Errors[0].Code);
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public async Task Forbidden_chat_maps_to_chat_unavailable()
    {
        var factory = new StubHttpClientFactory(() => new HttpClient(new StubHandler(HttpStatusCode.Forbidden)));
        var sender = CreateSender(ConfiguredOptions(), factory);

        var result = await sender.SendMessageAsync(42, "text");

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Notification.TelegramChatUnavailable,
            result.Error.Errors[0].Code);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task Too_many_requests_maps_to_temporary_send_failure()
    {
        var factory = new StubHttpClientFactory(() => new HttpClient(new StubHandler(HttpStatusCode.TooManyRequests)));
        var sender = CreateSender(ConfiguredOptions(), factory);

        var result = await sender.SendMessageAsync(42, "text");

        Assert.True(result.IsFailure);
        Assert.Equal(
            ApiErrorCodes.Notification.TelegramSendFailed,
            result.Error.Errors[0].Code);
    }

    private static TelegramOptions ConfiguredOptions()
        => new()
        {
            BotToken = "123456:test",
            BotUsername = "test_bot"
        };

    private static TelegramSender CreateSender(
        TelegramOptions options,
        IHttpClientFactory factory)
        => new(factory, options, NullLogger<TelegramSender>.Instance);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpClient> _create;

        public StubHttpClientFactory(Func<HttpClient> create) => _create = create;

        public int Calls { get; private set; }

        public HttpClient CreateClient(string name)
        {
            Calls++;

            return _create();
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public StubHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status));
    }
}
