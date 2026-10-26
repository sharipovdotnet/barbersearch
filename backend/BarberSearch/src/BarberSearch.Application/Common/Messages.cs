namespace BarberSearch.Application.Common;

/// <summary>
/// Тексты ошибок приложения, которые повторяются в нескольких обработчиках или репозиториях.
/// </summary>
/// <remarks>
/// Текст ошибки — часть наблюдаемого поведения (<c>Error.Message</c>), поэтому значения
/// остаются прежними: вынесены только повторяющиеся строки. Коды ошибок живут рядом со
/// своим агрегатом или в <c>ApiErrorCodes</c>, не здесь.
/// </remarks>
public static class Messages
{
    /// <summary>
    /// Курсор постраничной выдачи не разбирается.
    /// </summary>
    public const string CURSOR_INVALID = "The pagination cursor is invalid.";

    /// <summary>
    /// Указанной услуги нет в профиле барбера.
    /// </summary>
    public const string SERVICE_NOT_FOUND_IN_PROFILE =
        "The specified service was not found in this barber profile.";

    /// <summary>
    /// Указанного рабочего дня нет в салоне барбера.
    /// </summary>
    public const string WORKING_SCHEDULE_NOT_FOUND =
        "The specified working schedule was not found in this barber salon.";

    /// <summary>
    /// Размер страницы, запрошенный у репозитория, не положителен.
    /// </summary>
    public const string PAGE_LIMIT_NOT_POSITIVE = "Page limit must be positive.";

    /// <summary>
    /// Учётная запись деактивирована.
    /// </summary>
    public const string ACCOUNT_DEACTIVATED = "The account is deactivated.";

    /// <summary>
    /// Refresh-токен не передан.
    /// </summary>
    public const string REFRESH_TOKEN_REQUIRED = "A refresh token is required.";

    /// <summary>
    /// Сообщение о недопустимом размере страницы для конкретной верхней границы.
    /// </summary>
    /// <remarks>
    /// Граница параметром, а не константой: в Application (<see cref="Pagination.MAX_PAGE_SIZE"/>)
    /// и в инфраструктуре (<c>RepositoryBase.MAX_PAGE_SIZE</c>) она разная, а текст сообщения
    /// должен остаться прежним в обоих слоях.
    /// </remarks>
    /// <param name="maxPageSize">Верхняя граница размера страницы.</param>
    public static string PageSizeOutOfRange(int maxPageSize)
        => $"Page size must be between 1 and {maxPageSize}.";
}
