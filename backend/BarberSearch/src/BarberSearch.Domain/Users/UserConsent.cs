using System.Net;
using BarberSearch.Domain.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Domain.Users;

/// <summary>
/// Запись подтверждаемого согласия на обработку персональных данных.
/// </summary>
/// <remarks>
/// Значение неизменяемо: согласие — это факт «когда и на какую редакцию», а не поле профиля,
/// которое правят. Отзыв и повторное согласие — отдельный срез с собственной историей.
/// <para>
/// Валидация не бросает исключений на пользовательский ввод: <see cref="Create"/> возвращает
/// ошибки в том же формате, что собирает <see cref="User.Create"/>, и вызывающий превращает
/// их в обычный <c>Validation</c>-результат.
/// </para>
/// </remarks>
public sealed class UserConsent
{
    /// <summary>
    /// Предел длины редакции: дата редакции занимает 10 символов, запас — на будущий формат.
    /// </summary>
    public const int MAX_POLICY_VERSION_LENGTH = 32;

    /// <summary>
    /// Текст ошибки «редакция Политики пуста».
    /// </summary>
    private const string POLICY_VERSION_REQUIRED = "Privacy policy version cannot be empty.";

    private UserConsent(string policyVersion, DateTimeOffset consentedAt, IPAddress? ipAddress)
    {
        PolicyVersion = policyVersion;
        ConsentedAt = consentedAt;
        IpAddress = ipAddress;
    }

    /// <summary>
    /// Редакция Политики, на которую дано согласие.
    /// </summary>
    public string PolicyVersion { get; }

    /// <summary>
    /// Момент согласия в UTC. Время ставит сервер, клиентское не принимается.
    /// </summary>
    public DateTimeOffset ConsentedAt { get; }

    /// <summary>
    /// IP-адрес, с которого дано согласие; <c>null</c>, если сервер его не знает.
    /// </summary>
    public IPAddress? IpAddress { get; }

    /// <summary>
    /// Создаёт запись согласия: срезает пробелы у редакции и приводит время к UTC.
    /// </summary>
    public static Result<UserConsent, Failure> Create(
        string? policyVersion,
        DateTimeOffset consentedAt,
        IPAddress? ipAddress)
    {
        var errors = DomainErrors.Collect();
        var normalizedVersion = policyVersion?.Trim() ?? string.Empty;

        if (normalizedVersion.Length == 0)
        {
            errors.Add(Error.Validation(
                "user.consent.policyVersion",
                POLICY_VERSION_REQUIRED,
                nameof(policyVersion)));
        }
        else if (normalizedVersion.Length > MAX_POLICY_VERSION_LENGTH)
        {
            errors.Add(Error.Validation(
                "user.consent.policyVersion",
                $"Privacy policy version must be at most {MAX_POLICY_VERSION_LENGTH} characters long.",
                nameof(policyVersion)));
        }

        if (errors.Count != 0)
        {
            return new Failure(errors);
        }

        return new UserConsent(normalizedVersion, consentedAt.ToUniversalTime(), ipAddress);
    }
}
