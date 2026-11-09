using System.Net;
using System.Net.Sockets;
using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using BarberSearch.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// SMTP-транспорт: готовность, отказ без настроек, сетевая ошибка и успешная отправка.
/// </summary>
/// <remarks>
/// Успешный путь проверяется на локальном <see cref="FakeSmtpServer"/>; настоящая почта в
/// unit-тестах не участвует. Ветка таймаута детерминированно не воспроизводится:
/// <c>SmtpClient</c> не применяет <c>Timeout</c> к асинхронной отправке.
/// </remarks>
public class SmtpEmailSenderTests
{
    private const string FromAddress = "no-reply@barbersearch.test";

    [Fact]
    public void IsAvailable_MirrorsOptions()
    {
        Assert.True(CreateSender(ConfiguredOptions(25)).IsAvailable);
        Assert.False(CreateSender(new EmailOptions()).IsAvailable);
    }

    [Fact]
    public async Task SendAsync_WhenNotConfigured_ReturnsFailure()
    {
        var sender = CreateSender(new EmailOptions());

        var result = await sender.SendAsync(CreateMessage(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == ApiErrorCodes.Auth.EmailVerificationSendFailed);
    }

    [Fact]
    public async Task SendAsync_WhenServerRefusesConnection_ReturnsFailure()
    {
        var sender = CreateSender(ConfiguredOptions(ReserveFreePort()));

        var result = await sender.SendAsync(CreateMessage(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(
            result.Error.Errors,
            error => error.Code == ApiErrorCodes.Auth.EmailVerificationSendFailed);
    }

    [Fact]
    public async Task SendAsync_WhenServerAccepts_SendsHtmlAndTextParts()
    {
        await using var server = FakeSmtpServer.Start();
        var sender = CreateSender(ConfiguredOptions(server.Port));

        var result = await sender.SendAsync(CreateMessage(), CancellationToken.None);
        var data = await server.DataAsync.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.IsSuccess);
        Assert.Contains("Subject:", data, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/plain", data, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text/html", data, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client@example.com", data, StringComparison.OrdinalIgnoreCase);
    }

    private static SmtpEmailSender CreateSender(EmailOptions options)
        => new(options, NullLogger<SmtpEmailSender>.Instance);

    private static EmailMessage CreateMessage()
        => new(
            "client@example.com",
            "Подтвердите адрес электронной почты",
            "<p>Здравствуйте!</p>",
            "Здравствуйте!");

    private static EmailOptions ConfiguredOptions(int port)
        => new()
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            UseTls = false,
            FromAddress = FromAddress,
            TimeoutSeconds = 2
        };

    /// <summary>
    /// Свободный порт, на котором никто не слушает: соединение получает отказ.
    /// </summary>
    private static int ReserveFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
