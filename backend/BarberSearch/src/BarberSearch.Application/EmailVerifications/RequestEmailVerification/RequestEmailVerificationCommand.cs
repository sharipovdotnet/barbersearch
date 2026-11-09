using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailVerifications.RequestEmailVerification;

/// <summary>
/// Запрос письма со ссылкой подтверждения email.
/// </summary>
/// <remarks>
/// Пользователь берётся из claims контроллера, а не из тела: иначе любой мог бы отправить
/// письмо на чужой адрес. Повторная отправка отзывает предыдущий активный токен.
/// </remarks>
/// <param name="UserId">Пользователь, которому отправляется письмо.</param>
public sealed record RequestEmailVerificationCommand(Guid UserId) : ICommand;
