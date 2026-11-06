using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Users;
using BarberSearch.Contracts.BarberClients;
using BarberSearch.Contracts.Common;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberClients;

/// <summary>
/// Обработчик поиска пользователей для формы «Записать клиента».
/// </summary>
/// <remarks>
/// <para>
/// Минимум три символа защищает от выдачи всего списка аккаунтов с контактами: пустой
/// запрос — это <c>400 barberClient.search.tooShort</c>, а не «первые десять». Верхняя
/// граница <c>100</c> совпадает с остальными поисками проекта и не даёт раздуть шаблон.
/// </para>
/// <para>
/// Результат ограничен десятью строками: это подсказка в форме, а не справочник.
/// Пагинации нет сознательно — барбер либо узнаёт человека, либо уточняет запрос.
/// Роль не фильтруется: записать можно клиента, администратора, барбера и любого
/// другого активного пользователя.
/// </para>
/// </remarks>
public sealed class SearchBarberClientsHandler
    : IQueryHandler<IReadOnlyList<BarberClientResponse>, SearchBarberClientsQuery>
{
    /// <summary>Минимальная длина поисковой строки.</summary>
    public const int MIN_SEARCH_LENGTH = 3;

    /// <summary>Максимальная длина поисковой строки.</summary>
    public const int MAX_SEARCH_LENGTH = 100;

    /// <summary>Сколько строк возвращает подсказка.</summary>
    public const int RESULT_LIMIT = 10;

    private readonly IUserRepository _users;

    public SearchBarberClientsHandler(IUserRepository users)
    {
        ArgumentNullException.ThrowIfNull(users);

        _users = users;
    }

    /// <summary>
    /// Ищет активных пользователей по email или телефону; короткая и слишком длинная
    /// строка — <c>400</c>.
    /// </summary>
    public async Task<Result<IReadOnlyList<BarberClientResponse>, Failure>> Handle(
        SearchBarberClientsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var search = query.Search?.Trim() ?? string.Empty;

        if (search.Length < MIN_SEARCH_LENGTH)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.BarberClient.SearchTooShort,
                $"A client search must contain at least {MIN_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        if (search.Length > MAX_SEARCH_LENGTH)
        {
            return new Failure(Error.Validation(
                ApiErrorCodes.BarberClient.SearchTooLong,
                $"A client search cannot be longer than {MAX_SEARCH_LENGTH} characters.",
                nameof(query.Search)));
        }

        return await _users.SearchClientsAsync(
            search,
            RESULT_LIMIT,
            cancellationToken);
    }
}
