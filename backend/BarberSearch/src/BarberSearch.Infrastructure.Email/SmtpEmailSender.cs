using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using BarberSearch.Application.Abstractions;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared;

namespace BarberSearch.Infrastructure.Email;

/// <summary>
/// Отправка писем через <see cref="SmtpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Пакет для почты не нужен: транспорт — встроенный <see cref="SmtpClient"/>. Это осознанное
/// решение среза: MailKit понадобится только для OAuth2 или HTTP-API провайдера.
/// </para>
/// <para>
/// <see cref="SmtpClient"/> поддерживает только STARTTLS (<see cref="SmtpClient.EnableSsl"/>);
/// implicit TLS на порту 465 им не поддерживается. Локально письма принимает Mailpit
/// на порту 1025 без TLS.
/// </para>
/// <para>
/// Отправитель без состояния: <see cref="SmtpClient"/> создаётся на вызов и диспозится,
/// поэтому singleton безопасен. В лог попадают только тип исключения и SMTP-статус: адрес
/// получателя и текст письма — персональные данные, а сообщение исключения может содержать
/// адрес.
/// </para>
/// </remarks>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => _options.IsConfigured;

    /// <inheritdoc />
    public async Task<UnitResult<Failure>> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.IsConfigured)
        {
            _logger.LogWarning("SMTP is not configured; the message was not sent.");

            return SendFailed("SMTP is not configured on the server.");
        }

        var timeoutSeconds = _options.TimeoutSeconds > 0
            ? _options.TimeoutSeconds
            : EmailOptions.DEFAULT_TIMEOUT_SECONDS;

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            // Порядок важен: EnableSsl — это STARTTLS, а не implicit TLS.
            EnableSsl = _options.UseTls,
            UseDefaultCredentials = false,
            Timeout = (int)Math.Min((long)timeoutSeconds * 1000, int.MaxValue)
        };

        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            // Анонимный SMTP (Mailpit) не должен получить пустые учётные данные.
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress!, _options.FromName),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            // Основное тело — текст, HTML — AlternateView ниже. Обратный порядок
            // (Body = html, IsBodyHtml = true, текстовый AlternateView) даёт
            // multipart/alternative, где обе части помечены text/plain: клиент
            // показывает разметку как обычный текст.
            Body = message.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        mail.To.Add(message.To);

        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            message.HtmlBody,
            Encoding.UTF8,
            MediaTypeNames.Text.Html));

        try
        {
            await client.SendMailAsync(mail, cancellationToken);

            return UnitResult.Success<Failure>();
        }
        catch (SmtpException exception)
        {
            // Сообщение SmtpException может содержать адрес получателя, поэтому в лог
            // попадают только тип и SMTP-статус.
            _logger.LogWarning(
                "SMTP send failed: {ExceptionType}, status {StatusCode}.",
                exception.GetType().Name,
                exception.StatusCode);

            return SendFailed($"The SMTP server rejected the message ({exception.StatusCode}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут SmtpClient: вызывающий запрос не отменял отправку.
            _logger.LogWarning("SMTP send timed out.");

            return SendFailed("The SMTP request timed out.");
        }
    }

    private static UnitResult<Failure> SendFailed(string message)
        => UnitResult.Failure(new Failure(Error.Failure(
            ApiErrorCodes.Auth.EmailVerificationSendFailed,
            message)));
}
