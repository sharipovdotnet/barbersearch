namespace BarberSearch.Contracts.Common;

/// <summary>
/// Реестр публичных кодов ошибок HTTP-контракта.
/// </summary>
/// <remarks>
/// <para>
/// Код ошибки — часть контракта: клиент может опираться на него в ветвлениях и ретраях.
/// Формат — <c>{aggregate}.{method?}.{detail}</c>; имя метода репозитория (<c>getById</c>,
/// <c>getPaged</c>, <c>getByEmail</c> и т. п.) в публичный код не попадает, поэтому
/// <c>method?</c> здесь означает операцию предметной области, а не метод репозитория.
/// Значения стабильны: переименование — это breaking change, а не внутренняя правка.
/// </para>
/// <para>
/// Ошибки сохранения, которые <c>RepositoryBase</c> переводит из SQLSTATE, состоят из
/// префикса операции (<c>AddPrefix</c>, <c>UpdatePrefix</c>, <c>DeletePrefix</c> и т. п.)
/// и суффикса, который возвращают фабрики <see cref="UniqueViolation"/> и соседние.
/// Полный код строится только из этих констант: например,
/// <c>ApiErrorCodes.UniqueViolation(ApiErrorCodes.Booking.AddPrefix)</c> даёт
/// <c>booking.add.uniqueViolation</c>. Явно перечислены и полные коды, которые уже
/// обрабатываются или проверяются тестами: <c>booking.add.overlap</c>,
/// <c>barberProfile.add.uniqueViolation</c> и другие.
/// </para>
/// <para>
/// Внутренние диагностические коды начинаются с <c>internal.</c> и контрактом не являются.
/// Единственный такой код — <see cref="InternalDatabaseError"/> (500); клиент не должен
/// строить на нём логику, кроме общей обработки серверной ошибки.
/// </para>
/// </remarks>
public static class ApiErrorCodes
{
    /// <summary>
    /// Непредвиденная ошибка базы данных или сохранения; <c>500</c>.
    /// </summary>
    /// <remarks>
    /// Внутренний диагностический код: детали (SQLSTATE, имя ограничения, текст сервера)
    /// остаются в сообщении для логов, но код не выдаёт операцию репозитория и не является
    /// частью публичного контракта.
    /// </remarks>
    public const string InternalDatabaseError = "internal.database";

    /// <summary>
    /// Аутентификация и авторизация.
    /// </summary>
    public static class Auth
    {
        /// <summary>
        /// Запрос не аутентифицирован: нет пользователя, claim'а <c>sub</c> или токена; <c>401</c>.
        /// </summary>
        public const string Unauthorized = "auth.unauthorized";

        /// <summary>
        /// Пользователь аутентифицирован, но не вправе управлять этим ресурсом; <c>403</c>.
        /// </summary>
        public const string Forbidden = "auth.forbidden";

        /// <summary>
        /// Вход временно заблокирован после серии неудачных попыток; <c>401</c>.
        /// </summary>
        /// <remarks>
        /// Возвращается только при верном пароле: при неверном заблокированный аккаунт отвечает
        /// так же, как обычный (<c>auth.login.invalidCredentials</c>), поэтому по коду нельзя
        /// выяснить наличие адреса. Пока задержка растёт, повторные неудачи не продлевают
        /// блокировку — иначе атакующий держал бы владельца заблокированным бесконечно.
        /// </remarks>
        public const string LockedOut = "auth.login.lockedOut";
    }

    /// <summary>
    /// Брони.
    /// </summary>
    public static class Booking
    {
        /// <summary>
        /// Брони с указанным идентификатором нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "booking.notFound";

        /// <summary>
        /// Брони с указанным ключом идемпотентности у пользователя нет; <c>404</c>.
        /// </summary>
        public const string IdempotencyKeyNotFound = "booking.idempotencyKey.notFound";

        /// <summary>
        /// Ключ идемпотентности пуст или состоит из пробелов; <c>400</c>.
        /// </summary>
        public const string IdempotencyKeyInvalid = "booking.idempotencyKey.invalid";

        /// <summary>
        /// Номер страницы меньше единицы; <c>400</c>.
        /// </summary>
        public const string PagingPageInvalid = "booking.paging.page";

        /// <summary>
        /// Размер страницы вне диапазона <c>1..200</c>; <c>400</c>.
        /// </summary>
        public const string PagingPageSizeInvalid = "booking.paging.pageSize";

        /// <summary>
        /// Лимит keyset-страницы не положительный; <c>400</c>.
        /// </summary>
        public const string PageLimitInvalid = "booking.page.limit";

        /// <summary>
        /// Конец запрошенного периода раньше его начала; <c>400</c>.
        /// </summary>
        public const string ActivePeriodInvalid = "booking.activePeriod.invalid";

        /// <summary>
        /// Конец проверяемого интервала раньше начала; <c>400</c>.
        /// </summary>
        public const string OverlapPeriodInvalid = "booking.overlap.period";

        /// <summary>
        /// Префикс кодов сохранения брони; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "booking.add";

        /// <summary>
        /// Префикс кодов обновления брони; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string UpdatePrefix = "booking.update";

        /// <summary>
        /// Повтор ключа идемпотентности при вставке; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";

        /// <summary>
        /// Пересечение активных броней барбера; <c>409</c>.
        /// </summary>
        public const string AddOverlap = AddPrefix + ".overlap";

        /// <summary>
        /// Взаимная блокировка при конкурентной вставке; <c>409</c>.
        /// </summary>
        public const string AddDeadlock = AddPrefix + ".deadlock";

        /// <summary>
        /// Истёк <c>lock_timeout</c> ожидания advisory-блокировки; <c>409</c>.
        /// </summary>
        public const string AddLockTimeout = AddPrefix + ".lockTimeout";

        /// <summary>
        /// Строку изменил или удалил параллельный запрос; <c>409</c>.
        /// </summary>
        public const string UpdateConcurrencyConflict = UpdatePrefix + ".concurrencyConflict";
    }

    /// <summary>
    /// Профили барберов.
    /// </summary>
    public static class BarberProfile
    {
        /// <summary>
        /// Профиля нет, он неактивен или не подтверждён; <c>404</c>.
        /// </summary>
        public const string NotFound = "barberProfile.notFound";

        /// <summary>
        /// Идентификатор профиля в запросе пуст; <c>400</c>.
        /// </summary>
        public const string InvalidId = "barberProfile.id.invalid";

        /// <summary>
        /// Указанный салон не привязан к профилю; <c>404</c>.
        /// </summary>
        public const string FindSalonNotFound = "barberProfile.findSalon.notFound";

        /// <summary>
        /// Профиль не предоставляет указанную услугу; <c>404</c>.
        /// </summary>
        public const string ServiceNotFound = "barberProfile.service.notFound";

        /// <summary>
        /// Профиль не виден для выдачи слотов; <c>404</c>.
        /// </summary>
        public const string SlotsNotFound = "barberProfile.slots.notFound";

        /// <summary>
        /// Номер страницы меньше единицы; <c>400</c>.
        /// </summary>
        public const string PagingPageInvalid = "barberProfile.paging.page";

        /// <summary>
        /// Размер страницы вне диапазона <c>1..200</c>; <c>400</c>.
        /// </summary>
        public const string PagingPageSizeInvalid = "barberProfile.paging.pageSize";

        /// <summary>
        /// Глубина offset-выдачи <c>page * pageSize</c> больше <c>10 000</c>; <c>400</c>.
        /// </summary>
        public const string PagingDepthInvalid = "barberProfile.paging.depth";

        /// <summary>
        /// У текущего пользователя уже есть профиль барбера; <c>409</c>.
        /// </summary>
        public const string CreateAlreadyExists = "barberProfile.create.alreadyExists";

        /// <summary>
        /// Префикс кодов сохранения профиля; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "barberProfile.add";

        /// <summary>
        /// Префикс кодов обновления профиля; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string UpdatePrefix = "barberProfile.update";

        /// <summary>
        /// Префикс кодов удаления профиля; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string DeletePrefix = "barberProfile.delete";

        /// <summary>
        /// Гонка двух созданий на уникальном индексе пользователя; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";

        /// <summary>
        /// Пересечение с отложенным exclusion-ограничением основного салона; <c>409</c>.
        /// </summary>
        public const string UpdateOverlap = UpdatePrefix + ".overlap";

        /// <summary>
        /// Строку изменил или удалил параллельный запрос; <c>409</c>.
        /// </summary>
        public const string UpdateConcurrencyConflict = UpdatePrefix + ".concurrencyConflict";
    }

    /// <summary>
    /// Пользователи.
    /// </summary>
    public static class User
    {
        /// <summary>
        /// Пользователя с указанным идентификатором или email нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "user.notFound";

        /// <summary>
        /// Пользователя нет в узкой проекции проверки security stamp; <c>404</c>.
        /// </summary>
        public const string SecurityStampNotFound = "user.securityStamp.notFound";

        /// <summary>
        /// Email пуст или не является корректным адресом; <c>400</c>.
        /// </summary>
        public const string EmailInvalid = "user.email.invalid";

        /// <summary>
        /// Номер страницы меньше единицы; <c>400</c>.
        /// </summary>
        public const string PagingPageInvalid = "user.paging.page";

        /// <summary>
        /// Размер страницы вне диапазона <c>1..200</c>; <c>400</c>.
        /// </summary>
        public const string PagingPageSizeInvalid = "user.paging.pageSize";

        /// <summary>
        /// Глубина offset-выдачи <c>page * pageSize</c> больше <c>10 000</c>; <c>400</c>.
        /// </summary>
        public const string PagingDepthInvalid = "user.paging.depth";

        /// <summary>
        /// Префикс кодов сохранения пользователя; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "user.add";

        /// <summary>
        /// Префикс кодов обновления пользователя; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string UpdatePrefix = "user.update";

        /// <summary>
        /// Префикс кодов удаления пользователя; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string DeletePrefix = "user.delete";

        /// <summary>
        /// Пользователь с таким email уже существует; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";

        /// <summary>
        /// Строку изменил или удалил параллельный запрос; <c>409</c>.
        /// </summary>
        public const string UpdateConcurrencyConflict = UpdatePrefix + ".concurrencyConflict";
    }

    /// <summary>
    /// Админский список и карточка барберов.
    /// </summary>
    /// <remarks>
    /// Отдельные коды, потому что это отдельный эндпоинт: клиент различает ошибку фильтра
    /// админского списка и ошибку публичного поиска, хотя параметры похожи.
    /// </remarks>
    public static class AdminBarber
    {
        /// <summary>
        /// Фильтр статуса не <c>all</c>, <c>pending</c> или <c>hidden</c>; <c>400</c>.
        /// </summary>
        public const string StatusInvalid = "admin.barbers.status";

        /// <summary>
        /// Поисковая строка длиннее допустимой; <c>400</c>.
        /// </summary>
        public const string SearchTooLong = "admin.barbers.search";

        /// <summary>
        /// Идентификатор профиля в запросе пуст; <c>400</c>.
        /// </summary>
        public const string InvalidId = "admin.barbers.id";

        /// <summary>
        /// Лимит keyset-страницы не положительный; <c>400</c>.
        /// </summary>
        public const string PageLimitInvalid = "admin.barbers.page.limit";

        /// <summary>
        /// Префикс кодов проверки размера страницы; полный код — <c>admin.barbers.pageSize</c>.
        /// </summary>
        public const string PageSizePrefix = "admin.barbers";
    }

    /// <summary>
    /// Салоны.
    /// </summary>
    public static class Salon
    {
        /// <summary>
        /// Салона с указанным идентификатором нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "salon.notFound";

        /// <summary>
        /// Город пуст или состоит из пробелов; <c>400</c>.
        /// </summary>
        public const string CityInvalid = "salon.city.invalid";

        /// <summary>
        /// Номер страницы меньше единицы; <c>400</c>.
        /// </summary>
        public const string PagingPageInvalid = "salon.paging.page";

        /// <summary>
        /// Размер страницы вне диапазона <c>1..200</c>; <c>400</c>.
        /// </summary>
        public const string PagingPageSizeInvalid = "salon.paging.pageSize";

        /// <summary>
        /// Префикс кодов сохранения салона; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "salon.add";

        /// <summary>
        /// Префикс кодов обновления салона; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string UpdatePrefix = "salon.update";

        /// <summary>
        /// Префикс кодов удаления салона; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string DeletePrefix = "salon.delete";

        /// <summary>
        /// Салон с такими уникальными значениями уже существует; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";

        /// <summary>
        /// Строку изменил или удалил параллельный запрос; <c>409</c>.
        /// </summary>
        public const string UpdateConcurrencyConflict = UpdatePrefix + ".concurrencyConflict";
    }

    /// <summary>
    /// Справочник услуг.
    /// </summary>
    public static class HairService
    {
        /// <summary>
        /// Услуги с указанным идентификатором нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "hairService.notFound";

        /// <summary>
        /// Категория услуги не определена; <c>400</c>.
        /// </summary>
        public const string CategoryInvalid = "hairService.category.invalid";

        /// <summary>
        /// Номер страницы меньше единицы; <c>400</c>.
        /// </summary>
        public const string PagingPageInvalid = "hairService.paging.page";

        /// <summary>
        /// Размер страницы вне диапазона <c>1..200</c>; <c>400</c>.
        /// </summary>
        public const string PagingPageSizeInvalid = "hairService.paging.pageSize";

        /// <summary>
        /// Префикс кодов сохранения услуги; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "hairService.add";

        /// <summary>
        /// Префикс кодов обновления услуги; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string UpdatePrefix = "hairService.update";

        /// <summary>
        /// Префикс кодов удаления услуги; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string DeletePrefix = "hairService.delete";

        /// <summary>
        /// Услуга с такими уникальными значениями уже существует; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";

        /// <summary>
        /// Строку изменил или удалил параллельный запрос; <c>409</c>.
        /// </summary>
        public const string UpdateConcurrencyConflict = UpdatePrefix + ".concurrencyConflict";
    }

    /// <summary>
    /// Справочник специализаций.
    /// </summary>
    public static class Specialization
    {
        /// <summary>
        /// Специализации с указанным идентификатором или именем нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "specialization.notFound";

        /// <summary>
        /// Имя специализации пусто; <c>400</c>.
        /// </summary>
        public const string NameInvalid = "specialization.name.invalid";

        /// <summary>
        /// Номер страницы меньше единицы; <c>400</c>.
        /// </summary>
        public const string PagingPageInvalid = "specialization.paging.page";

        /// <summary>
        /// Размер страницы вне диапазона <c>1..200</c>; <c>400</c>.
        /// </summary>
        public const string PagingPageSizeInvalid = "specialization.paging.pageSize";

        /// <summary>
        /// Префикс кодов сохранения специализации; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "specialization.add";

        /// <summary>
        /// Префикс кодов обновления специализации; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string UpdatePrefix = "specialization.update";

        /// <summary>
        /// Префикс кодов удаления специализации; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string DeletePrefix = "specialization.delete";

        /// <summary>
        /// Специализация с таким именем уже существует; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";

        /// <summary>
        /// Строку изменил или удалил параллельный запрос; <c>409</c>.
        /// </summary>
        public const string UpdateConcurrencyConflict = UpdatePrefix + ".concurrencyConflict";
    }

    /// <summary>
    /// Отзывы.
    /// </summary>
    public static class Review
    {
        /// <summary>
        /// Отзыва с указанным идентификатором или бронью нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "review.notFound";

        /// <summary>
        /// Номер страницы меньше единицы; <c>400</c>.
        /// </summary>
        public const string PagingPageInvalid = "review.paging.page";

        /// <summary>
        /// Размер страницы вне диапазона <c>1..200</c>; <c>400</c>.
        /// </summary>
        public const string PagingPageSizeInvalid = "review.paging.pageSize";

        /// <summary>
        /// Глубина offset-выдачи <c>page * pageSize</c> больше <c>10 000</c>; <c>400</c>.
        /// </summary>
        public const string PagingDepthInvalid = "review.paging.depth";

        /// <summary>
        /// Лимит keyset-страницы не положительный; <c>400</c>.
        /// </summary>
        public const string PageLimitInvalid = "review.page.limit";

        /// <summary>
        /// Префикс кодов сохранения отзыва; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "review.add";

        /// <summary>
        /// Префикс кодов обновления отзыва; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string UpdatePrefix = "review.update";

        /// <summary>
        /// Префикс кодов удаления отзыва; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string DeletePrefix = "review.delete";

        /// <summary>
        /// Второй отзыв на ту же бронь; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";

        /// <summary>
        /// Строку изменил или удалил параллельный запрос; <c>409</c>.
        /// </summary>
        public const string UpdateConcurrencyConflict = UpdatePrefix + ".concurrencyConflict";
    }

    /// <summary>
    /// Refresh-токены.
    /// </summary>
    public static class RefreshToken
    {
        /// <summary>
        /// Refresh-токена с указанным хешем нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "refreshToken.notFound";

        /// <summary>
        /// Хеш refresh-токена пуст; <c>400</c>.
        /// </summary>
        public const string TokenHashInvalid = "refreshToken.tokenHash.invalid";

        /// <summary>
        /// Токен уже отозван параллельной ротацией; <c>409</c>.
        /// </summary>
        public const string AlreadyRotated = "refreshToken.rotate.alreadyRotated";

        /// <summary>
        /// Текущий токен не помечен отозванным перед ротацией; <c>400</c>.
        /// </summary>
        public const string RotateNotRevoked = "refreshToken.rotate.notRevoked";

        /// <summary>
        /// Префикс кодов сохранения токена; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "refreshToken.add";

        /// <summary>
        /// Префикс кодов ротации токена; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string RotatePrefix = "refreshToken.rotate";

        /// <summary>
        /// Префикс кодов отзыва токена; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string RevokePrefix = "refreshToken.revoke";

        /// <summary>
        /// Повтор хеша refresh-токена при вставке; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";
    }

    /// <summary>
    /// Общие коды, не принадлежащие одному агрегату.
    /// </summary>
    public static class Common
    {
        /// <summary>
        /// Лимит поиска барберов не положительный; <c>400</c>.
        /// </summary>
        public const string BarberSearchLimitInvalid = "barberSearch.search.limit";

        /// <summary>
        /// У профиля барбера нет активного салона или указанный салон не активен; <c>404</c>.
        /// </summary>
        public const string BarberSlotsSalonNotFound = "barberSlots.salon.notFound";

        /// <summary>
        /// Профиль барбера не предоставляет указанную услугу для слотов; <c>404</c>.
        /// </summary>
        public const string BarberSlotsServiceNotFound = "barberSlots.service.notFound";

        /// <summary>
        /// Превышен лимит запросов; <c>429</c>.
        /// </summary>
        /// <remarks>
        /// Ответ приходит с заголовком <c>Retry-After</c> — секундами до сброса окна лимита.
        /// Клиент должен подождать и только потом повторять запрос, а не ретраить сразу:
        /// лимит записи защищает слоты и отзывы от мусорных операций.
        /// </remarks>
        public const string RateLimitExceeded = "rateLimit.exceeded";
    }

    /// <summary>
    /// Суффикс нарушения уникальности; <c>409</c>.
    /// </summary>
    /// <param name="codePrefix">Префикс операции, например <c>booking.add</c>.</param>
    /// <returns>Полный публичный код, например <c>booking.add.uniqueViolation</c>.</returns>
    public static string UniqueViolation(string codePrefix) => codePrefix + ".uniqueViolation";

    /// <summary>
    /// Суффикс пересечения периодов (exclusion-ограничение); <c>409</c>.
    /// </summary>
    /// <param name="codePrefix">Префикс операции, например <c>booking.add</c>.</param>
    /// <returns>Полный публичный код, например <c>booking.add.overlap</c>.</returns>
    public static string Overlap(string codePrefix) => codePrefix + ".overlap";

    /// <summary>
    /// Суффикс взаимной блокировки транзакций; <c>409</c>.
    /// </summary>
    /// <param name="codePrefix">Префикс операции, например <c>booking.add</c>.</param>
    /// <returns>Полный публичный код, например <c>booking.add.deadlock</c>.</returns>
    public static string Deadlock(string codePrefix) => codePrefix + ".deadlock";

    /// <summary>
    /// Суффикс истёкшего <c>lock_timeout</c>; <c>409</c>.
    /// </summary>
    /// <param name="codePrefix">Префикс операции, например <c>booking.add</c>.</param>
    /// <returns>Полный публичный код, например <c>booking.add.lockTimeout</c>.</returns>
    public static string LockTimeout(string codePrefix) => codePrefix + ".lockTimeout";

    /// <summary>
    /// Суффикс нарушения внешнего ключа; <c>409</c>.
    /// </summary>
    /// <param name="codePrefix">Префикс операции, например <c>booking.add</c>.</param>
    /// <returns>Полный публичный код, например <c>booking.add.foreignKeyViolation</c>.</returns>
    public static string ForeignKeyViolation(string codePrefix)
        => codePrefix + ".foreignKeyViolation";

    /// <summary>
    /// Суффикс нарушения check-ограничения; <c>400</c>.
    /// </summary>
    /// <param name="codePrefix">Префикс операции, например <c>booking.add</c>.</param>
    /// <returns>Полный публичный код, например <c>booking.add.checkViolation</c>.</returns>
    public static string CheckViolation(string codePrefix) => codePrefix + ".checkViolation";

    /// <summary>
    /// Суффикс нарушения NOT NULL; <c>400</c>.
    /// </summary>
    /// <param name="codePrefix">Префикс операции, например <c>booking.add</c>.</param>
    /// <returns>Полный публичный код, например <c>booking.add.notNullViolation</c>.</returns>
    public static string NotNullViolation(string codePrefix) => codePrefix + ".notNullViolation";

    /// <summary>
    /// Суффикс проигранной оптимистической блокировки; <c>409</c>.
    /// </summary>
    /// <param name="codePrefix">Префикс операции, например <c>booking.update</c>.</param>
    /// <returns>Полный публичный код, например <c>booking.update.concurrencyConflict</c>.</returns>
    public static string ConcurrencyConflict(string codePrefix)
        => codePrefix + ".concurrencyConflict";
}
