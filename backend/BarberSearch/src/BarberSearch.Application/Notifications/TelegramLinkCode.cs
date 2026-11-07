using System.Security.Cryptography;
using System.Text;
using BarberSearch.Domain;

namespace BarberSearch.Application.Notifications;

/// <summary>
/// Одноразовый код привязки Telegram и его хеш.
/// </summary>
/// <remarks>
/// <para>
/// Код — 32 символа base32 без цифр <c>0/1</c> и букв <c>O/I/L</c>: его диктуют голосом и
/// копируют руками, поэтому похожие символы исключены. Случайность — криптографическая
/// (<see cref="RandomNumberGenerator"/>), а не <c>Random</c>: код даёт право привязать чат.
/// </para>
/// <para>
/// В базе хранится только <see cref="Hash"/> — SHA-256 в hex, нижний регистр. Ссылка уходит
/// клиенту, и по ней нельзя восстановить код; поиск на вебхуке идёт по хешу. Нормализация
/// (срез пробелов и верхний регистр) применяется и при генерации, и при хешировании:
/// регистр не должен решать, сработает код или нет.
/// </para>
/// </remarks>
public static class TelegramLinkCode
{
    /// <summary>
    /// Длина кода в символах — 32 символа base32.
    /// </summary>
    public const int LENGTH = Constants.TELEGRAM_LINK_CODE_LENGTH;

    /// <summary>
    /// Алфавит base32 (RFC 4648) без символов, которые путают на слух и на глаз.
    /// </summary>
    private const string ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Генерирует новый код привязки.
    /// </summary>
    /// <remarks>
    /// На каждый символ берётся отдельный случайный байт и маскируется пятью битами
    /// (<c>b &amp; 31</c>): 256 делится на 32 нацело, поэтому все символы равновероятны.
    /// </remarks>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(LENGTH);
        var builder = new StringBuilder(LENGTH);

        foreach (var value in bytes)
        {
            builder.Append(ALPHABET[value & 31]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Считает SHA-256 кода в hex (нижний регистр) — то, что хранится в настройках.
    /// </summary>
    /// <param name="code">Код из ссылки или из сообщения боту; регистр и пробелы не важны.</param>
    public static string Hash(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var normalized = Normalize(code);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Приводит код к каноническому виду: срезает пробелы по краям и переводит в верхний регистр.
    /// </summary>
    /// <param name="code">Код в любом регистре.</param>
    public static string Normalize(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return code.Trim().ToUpperInvariant();
    }
}
