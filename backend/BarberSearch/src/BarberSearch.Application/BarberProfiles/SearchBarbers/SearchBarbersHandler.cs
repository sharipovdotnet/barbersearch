using BarberSearch.Application.Abstractions;
using BarberSearch.Application.Common;
using BarberSearch.Contracts.BarberProfiles.SearchBarbers;
using BarberSearch.Contracts.Common;
using BarberSearch.Domain.BarberProfiles.Enums;
using CSharpFunctionalExtensions;
using Shared;

namespace BarberSearch.Application.BarberProfiles.SearchBarbers;

/// <summary>
/// Обработчик поиска барберов.
/// </summary>
/// <remarks>
/// Обработчик отвечает за контракт пагинации: проверяет размер страницы, декодирует курсор,
/// читает на одну строку больше запрошенного и кодирует курсор следующей страницы.
/// Сами фильтры и порядок <c>rating DESC NULLS LAST, id</c> строит репозиторий — это один
/// SQL-запрос с проекцией в <see cref="BarberSearchItemResponse"/>, без Include коллекций.
/// </remarks>
public sealed class SearchBarbersHandler
    : IQueryHandler<PagedResponse<BarberSearchItemResponse>, SearchBarbersQuery>
{
    private readonly IBarberProfileRepository _repository;

    public SearchBarbersHandler(IBarberProfileRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    /// <summary>
    /// Ищет барберов и возвращает страницу с курсором продолжения.
    /// </summary>
    public async Task<Result<PagedResponse<BarberSearchItemResponse>, Failure>> Handle(
        SearchBarbersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = query.PageSize ?? Pagination.DEFAULT_PAGE_SIZE;

        if (Pagination.ValidatePageSize(pageSize, "barberSearch.search") is { } pageSizeError)
        {
            return pageSizeError;
        }

        BarberSearchCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = BarberSearchCursor.Decode(query.Cursor);
            if (decoded.IsFailure)
            {
                return decoded.Error;
            }

            cursor = decoded.Value;
        }

        Language? language = null;

        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            if (!TryParseLanguage(query.Language, out var parsedLanguage))
            {
                return new Failure(Error.Validation(
                    ApiErrorCodes.Common.BarberSearchLanguageInvalid,
                    $"""
                     Unknown search language '{query.Language.Trim()}'.
                     Allowed values: Ru, En, De, Kg, Tj, Uz.
                     """,
                    nameof(query.Language)));
            }

            language = parsedLanguage;
        }

        var filter = new BarberSearchFilter(
            City: string.IsNullOrWhiteSpace(query.City) ? null : query.City.Trim(),
            SpecializationId: query.SpecializationId == Guid.Empty ? null : query.SpecializationId,
            HairServiceId: query.HairServiceId == Guid.Empty ? null : query.HairServiceId,
            Date: query.Date,
            Language: language,
            Cursor: cursor,
            Limit: pageSize + 1);

        var rowsResult = await _repository.SearchAsync(filter, cancellationToken);
        if (rowsResult.IsFailure)
        {
            return rowsResult.Error;
        }

        var rows = rowsResult.Value;
        var hasNextPage = rows.Count > pageSize;
        var items = hasNextPage
            ? rows.Take(pageSize).ToList()
            : rows.ToList();

        var nextCursor = hasNextPage && items.Count > 0
            ? new BarberSearchCursor(items[^1].Rating, items[^1].Id).Encode()
            : null;

        return new PagedResponse<BarberSearchItemResponse>(items, nextCursor);
    }

    /// <summary>
    /// Разбирает язык фильтра по имени enum без учёта регистра.
    /// </summary>
    /// <remarks>
    /// Числовые формы отклоняются: <c>language=1</c> не должен молча означать <c>Ru</c>,
    /// иначе опечатка клиента вернула бы чужие результаты вместо <c>400</c>.
    /// </remarks>
    /// <param name="value">Строка из query string.</param>
    /// <param name="language">Разобранный язык при успехе.</param>
    /// <returns><c>true</c>, если строка — известное имя enum.</returns>
    private static bool TryParseLanguage(string value, out Language language)
    {
        var text = value.Trim();

        if (text.Length == 0 || text.Any(char.IsDigit))
        {
            language = default;
            return false;
        }

        return Enum.TryParse(text, ignoreCase: true, out language)
            && Enum.IsDefined(language)
            // Список через запятую (`Ru,En`) даёт числовое сочетание, которое может совпасть
            // с другим членом enum (Ru|En = De). Принимаем только точное имя одного языка.
            && string.Equals(Enum.GetName(language), text, StringComparison.OrdinalIgnoreCase);
    }
}
