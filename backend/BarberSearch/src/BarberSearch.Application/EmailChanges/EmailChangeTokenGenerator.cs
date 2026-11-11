using BarberSearch.Application.EmailVerifications;

namespace BarberSearch.Application.EmailChanges;

/// <summary>
/// Генерация открытого токена смены адреса и его SHA-256.
/// </summary>
/// <remarks>
/// Формат совпадает с токеном подтверждения email: 32 случайных байта в base64url,
/// в базу попадает только SHA-256 в нижнем регистре. Криптография не дублируется —
/// делегирование в <see cref="EmailVerificationTokenGenerator"/> держит один источник
/// правил формата и проверки, а имя типа остаётся в терминах сценария.
/// </remarks>
public static class EmailChangeTokenGenerator
{
    /// <summary>
    /// Максимальная длина входной строки до хеширования.
    /// </summary>
    public const int MAX_TOKEN_LENGTH = EmailVerificationTokenGenerator.MAX_TOKEN_LENGTH;

    /// <summary>
    /// Создаёт новый открытый токен.
    /// </summary>
    public static string Generate() => EmailVerificationTokenGenerator.Generate();

    /// <summary>
    /// Проверяет формат открытого токена до хеширования и похода в базу.
    /// </summary>
    /// <param name="token">Проверяемое значение; <c>null</c> недопустим.</param>
    public static bool IsValidToken(string? token)
        => EmailVerificationTokenGenerator.IsValidToken(token);

    /// <summary>
    /// Считает SHA-256 открытого токена в нижнем регистре.
    /// </summary>
    /// <exception cref="ArgumentException">Токен не проходит <see cref="IsValidToken"/>.</exception>
    public static string ComputeHash(string token)
        => EmailVerificationTokenGenerator.ComputeHash(token);
}
