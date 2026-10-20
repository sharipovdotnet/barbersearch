using Shared;

namespace BarberSearch.Application.Common;

/// <summary>
/// Общие правила постраничной выдачи для read-эндпоинтов.
/// </summary>
/// <remarks>
/// Размер страницы ограничен сверху, чтобы клиент не мог одним запросом выгрузить таблицу.
/// Курсорная пагинация не использует OFFSET, поэтому «глубокие» страницы не деградируют.
/// </remarks>
public static class Pagination
{
    /// <summary>
    /// Размер страницы по умолчанию, если клиент его не передал.
    /// </summary>
    public const int DEFAULT_PAGE_SIZE = 20;

    /// <summary>
    /// Максимальный размер страницы.
    /// </summary>
    public const int MAX_PAGE_SIZE = 100;

    /// <summary>
    /// Проверяет размер страницы. <c>null</c> — значение корректно.
    /// </summary>
    /// <param name="pageSize">Запрошенный размер страницы.</param>
    /// <param name="errorCode">Префикс кода ошибки конкретного эндпоинта.</param>
    public static Failure? ValidatePageSize(int pageSize, string errorCode)
        => pageSize is < 1 or > MAX_PAGE_SIZE
            ? new Failure(Error.Validation(
                $"{errorCode}.pageSize",
                $"Page size must be between 1 and {MAX_PAGE_SIZE}.",
                "pageSize"))
            : null;
}
