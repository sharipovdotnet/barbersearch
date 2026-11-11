using BarberSearch.Application.Abstractions;

namespace BarberSearch.Application.EmailChanges.ConfirmEmailChange;

/// <summary>
/// Подтверждение смены адреса по одноразовой ссылке из письма.
/// </summary>
/// <remarks>
/// Параметров, кроме токена, нет: адрес хранится в строке токена, а не приходит от клиента —
/// иначе подменить адрес можно было бы прямо в запросе.
/// </remarks>
/// <param name="Token">Открытый токен из ссылки письма.</param>
public sealed record ConfirmEmailChangeCommand(string Token) : ICommand;
