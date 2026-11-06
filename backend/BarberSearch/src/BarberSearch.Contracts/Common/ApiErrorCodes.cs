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

        /// <summary>
        /// Регистрация без согласия на обработку персональных данных; <c>400</c>.
        /// </summary>
        /// <remarks>
        /// Часть 1 статьи 9 152-ФЗ требует доказуемости согласия: без редакции Политики
        /// сервер не создаёт аккаунт, даже если клиентская проверка галочки обойдена.
        /// </remarks>
        public const string RegisterConsentRequired = "auth.register.consentRequired";

        /// <summary>
        /// Регистрация с устаревшей или чужой редакцией Политики; <c>400</c>.
        /// </summary>
        /// <remarks>
        /// Клиент отправляет дату редакции. Если текст Политики правили и подняли версию,
        /// старая открытая форма получает отказ: согласие на прежнюю редакцию не доказательство
        /// согласия с новой. Пользователь обновляет страницу и подтверждает согласие снова.
        /// </remarks>
        public const string RegisterConsentVersion = "auth.register.consentVersion";
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

        /// <summary>
        /// Скоуп списка записей барбера не <c>upcoming</c> и не <c>past</c>; <c>400</c>.
        /// </summary>
        /// <remarks>
        /// Свой код, а не общий код биндера: клиент различает ошибку параметра и не показывает
        /// технический текст ASP.NET Core пользователю.
        /// </remarks>
        public const string GetBarberBookingsScope = "booking.getBarberBookings.scope";

        /// <summary>
        /// Скоуп списка броней клиента не <c>all</c>, не <c>upcoming</c> и не <c>past</c>; <c>400</c>.
        /// </summary>
        /// <remarks>
        /// Отдельный код от <see cref="GetBarberBookingsScope"/>: у клиентского списка
        /// значение по умолчанию <c>all</c> и другой набор допустимых значений.
        /// </remarks>
        public const string GetBookingsScope = "booking.getBookings.scope";

        /// <summary>
        /// Заметка барбера длиннее <c>1000</c> символов; <c>400</c>.
        /// </summary>
        public const string BarberNoteTooLong = "booking.barberNote.tooLong";

        /// <summary>
        /// Барбер отменяет запись без причины; <c>400</c>.
        /// </summary>
        public const string CancelReasonRequired = "booking.cancel.reasonRequired";

        /// <summary>
        /// Причина отмены длиннее <c>500</c> символов; <c>400</c>.
        /// </summary>
        public const string CancelReasonTooLong = "booking.cancel.reasonTooLong";

        /// <summary>
        /// Клиент для ручной записи не найден, неактивен или не является клиентом; <c>404</c>.
        /// </summary>
        /// <remarks>
        /// Ответ намеренно не различает «нет такого пользователя» и «пользователь не
        /// подходит по роли/активности»: барберу не нужно знать причину, а лишнее
        /// различие превратило бы форму в проверку существования аккаунтов.
        /// </remarks>
        public const string CreateCustomerNotFound = "booking.create.customerNotFound";
    }

    /// <summary>
    /// Поиск клиентов барбером для ручной записи.
    /// </summary>
    /// <remarks>
    /// Отдельный префикс от <see cref="Booking"/>: это чтение подсказки в форме,
    /// а не операция над бронью, и клиент различает ошибку поиска и ошибку создания.
    /// </remarks>
    public static class BarberClient
    {
        /// <summary>
        /// Поисковая строка короче трёх символов; <c>400</c>.
        /// </summary>
        /// <remarks>
        /// Минимум защищает от запроса «все клиенты»: без него пустая строка вернула бы
        /// первые десять аккаунтов с контактами.
        /// </remarks>
        public const string SearchTooShort = "barberClient.search.tooShort";

        /// <summary>
        /// Поисковая строка длиннее <c>100</c> символов; <c>400</c>.
        /// </summary>
        public const string SearchTooLong = "barberClient.search.tooLong";
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
    /// Заявки на роль барбера: подача, своя заявка, административное решение.
    /// </summary>
    /// <remarks>
    /// Заявка не выдаёт прав: <c>Barber</c> появляется только после одобрения. Коды подачи
    /// отделены от кодов решения, потому что клиент различает «я не могу подать» и
    /// «администратор уже решил». Ошибки валидации полей живут в домене
    /// (<c>barberRequest.city</c> и т. п.) и в реестр не дублируются.
    /// </remarks>
    public static class BarberRequest
    {
        /// <summary>
        /// Заявки с указанным идентификатором нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "barberRequest.notFound";

        /// <summary>
        /// Заявку подаёт не клиент (барбер или администратор); <c>403</c>.
        /// </summary>
        /// <remarks>
        /// Роль в теле запроса не принимается: право подать заявку определяется по аккаунту,
        /// иначе клиент выдал бы себе роль <c>Barber</c> в обход администратора.
        /// </remarks>
        public const string CreateForbidden = "barberRequest.create.forbidden";

        /// <summary>
        /// У пользователя уже есть заявка в статусе <c>Pending</c>; <c>409</c>.
        /// </summary>
        /// <remarks>
        /// Возвращается и при гонке двух одновременных подач: предпроверка и уникальный
        /// частичный индекс дают один и тот же публичный код, а не <c>500</c>.
        /// </remarks>
        public const string CreateAlreadyPending = "barberRequest.create.alreadyPending";

        /// <summary>
        /// Заявка уже не в статусе <c>Pending</c>, поэтому решение невозможно; <c>409</c>.
        /// </summary>
        public const string ReviewInvalidStatus = "barberRequest.review.invalidStatus";

        /// <summary>
        /// Заявитель уже не клиент, поэтому одобрение не должно менять его роль; <c>409</c>.
        /// </summary>
        /// <remarks>
        /// Защитный случай: заявку подаёт <c>Customer</c>, но пока она ждала решения, роль
        /// могли сменить административным действием. Повторно выдавать <c>Barber</c> (или
        /// понижать администратора) нельзя — заявка остаётся открытой, а решение принимает
        /// человек через административный интерфейс.
        /// </remarks>
        public const string ReviewInvalidApplicant = "barberRequest.review.invalidApplicant";

        /// <summary>
        /// Отказ без причины или с причиной из одних пробелов; <c>400</c>.
        /// </summary>
        public const string RejectionReasonRequired = "barberRequest.rejection.reasonRequired";

        /// <summary>
        /// Причина отказа длиннее <c>500</c> символов; <c>400</c>.
        /// </summary>
        public const string RejectionReasonTooLong = "barberRequest.rejection.reasonTooLong";

        /// <summary>
        /// Фильтр статуса не <c>pending</c> и не <c>all</c>; <c>400</c>.
        /// </summary>
        public const string StatusInvalid = "barberRequest.list.status";

        /// <summary>
        /// Префикс кодов проверки размера страницы; полный код — <c>barberRequest.list.pageSize</c>.
        /// </summary>
        public const string PageSizePrefix = "barberRequest.list";

        /// <summary>
        /// Лимит keyset-страницы не положительный; <c>400</c>.
        /// </summary>
        public const string PageLimitInvalid = "barberRequest.page.limit";

        /// <summary>
        /// Префикс кодов сохранения заявки; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string AddPrefix = "barberRequest.add";

        /// <summary>
        /// Префикс кодов обновления заявки; к нему добавляется суффикс из фабрик.
        /// </summary>
        public const string UpdatePrefix = "barberRequest.update";

        /// <summary>
        /// Гонка двух подач на частичном уникальном индексе; <c>409</c>.
        /// </summary>
        public const string AddUniqueViolation = AddPrefix + ".uniqueViolation";

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
        /// Администратор пытается сменить роль себе; <c>409</c>.
        /// </summary>
        /// <remarks>
        /// Роль себе меняет другой администратор: иначе один промах в интерфейсе
        /// закрывает владельцу доступ к разделу, из которого он эту роль менял.
        /// </remarks>
        public const string RoleSelfChange = "user.role.self";

        /// <summary>
        /// Администратор пытается заблокировать собственный доступ; <c>409</c>.
        /// </summary>
        public const string DeactivationSelf = "user.deactivation.self";

        /// <summary>
        /// Операция оставила бы сервис без активного администратора; <c>409</c>.
        /// </summary>
        /// <remarks>
        /// Срабатывает и на снятие роли <c>Admin</c>, и на блокировку последнего активного
        /// администратора. Проверка выполняется под транзакционной блокировкой, поэтому два
        /// одновременных понижения не могут обнулить список администраторов.
        /// </remarks>
        public const string LastAdmin = "user.admin.last";

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
    /// Админский список и карточка пользователей.
    /// </summary>
    /// <remarks>
    /// Отдельные коды от <see cref="User"/>: это отдельный эндпоинт, и клиент различает
    /// ошибку фильтра админского списка и ошибку операции над пользователем.
    /// </remarks>
    public static class AdminUser
    {
        /// <summary>
        /// Пользователя с указанным идентификатором нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "admin.users.notFound";

        /// <summary>
        /// Фильтр статуса не <c>all</c>, <c>active</c>, <c>blocked</c> или <c>unverified</c>; <c>400</c>.
        /// </summary>
        public const string StatusInvalid = "admin.users.status";

        /// <summary>
        /// Фильтр роли не <c>all</c>, <c>Customer</c>, <c>Barber</c> или <c>Admin</c>; <c>400</c>.
        /// </summary>
        public const string RoleInvalid = "admin.users.role";

        /// <summary>
        /// Поисковая строка длиннее допустимой; <c>400</c>.
        /// </summary>
        public const string SearchTooLong = "admin.users.search";

        /// <summary>
        /// Идентификатор пользователя в запросе пуст; <c>400</c>.
        /// </summary>
        public const string InvalidId = "admin.users.id";

        /// <summary>
        /// Лимит keyset-страницы не положительный; <c>400</c>.
        /// </summary>
        public const string PageLimitInvalid = "admin.users.page.limit";

        /// <summary>
        /// Префикс кодов проверки размера страницы; полный код — <c>admin.users.pageSize</c>.
        /// </summary>
        public const string PageSizePrefix = "admin.users";
    }

    /// <summary>
    /// Админское чтение каталога: услуги, специализации и салоны.
    /// </summary>
    /// <remarks>
    /// Отдельные коды от публичных чтений справочников: это отдельный эндпоинт с фильтром
    /// статуса и поиском. Ошибки операций над строкой приходят с кодами агрегатов
    /// (<c>salon.timeZoneId.unknown</c>, <c>specialization.create.nameTaken</c> и так далее):
    /// мутации остаются на существующих контроллерах.
    /// </remarks>
    public static class AdminCatalogue
    {
        /// <summary>
        /// Фильтр статуса не <c>all</c>, не <c>active</c> и не <c>hidden</c>; <c>400</c>.
        /// </summary>
        public const string StatusInvalid = "admin.catalogue.status";

        /// <summary>
        /// Поисковая строка длиннее <c>100</c> символов; <c>400</c>.
        /// </summary>
        public const string SearchTooLong = "admin.catalogue.search";
    }

    /// <summary>
    /// Админское чтение журнала изменений.
    /// </summary>
    /// <remarks>
    /// Журнал только читается, поэтому кодов мутаций здесь нет. Ошибки фильтров отделены
    /// от <see cref="AdminUser"/> и <see cref="AdminCatalogue"/>: клиент различает ошибку
    /// фильтра журнала и ошибку другого административного списка.
    /// Курсор журнала держит собственный код <c>admin.audit.cursor.invalid</c>
    /// в <c>AdminAuditCursor</c>, как и остальные курсоры.
    /// </remarks>
    public static class AdminAudit
    {
        /// <summary>
        /// Фильтр типа не <c>all</c>, <c>user</c>, <c>barber_profile</c>, <c>review</c>,
        /// <c>salon</c>, <c>hair_service</c>, <c>specialization</c> или <c>booking</c>; <c>400</c>.
        /// </summary>
        public const string EntityTypeInvalid = "admin.audit.entityType";

        /// <summary>
        /// Подстрока email автора длиннее <c>100</c> символов; <c>400</c>.
        /// </summary>
        public const string ActorTooLong = "admin.audit.actor";

        /// <summary>
        /// Дата периода не в формате <c>yyyy-MM-dd</c> или начало позже конца; <c>400</c>.
        /// </summary>
        public const string PeriodInvalid = "admin.audit.period";

        /// <summary>
        /// Лимит keyset-страницы не положительный; <c>400</c>.
        /// </summary>
        public const string PageLimitInvalid = "admin.audit.page.limit";

        /// <summary>
        /// Префикс кодов проверки размера страницы; полный код — <c>admin.audit.pageSize</c>.
        /// </summary>
        public const string PageSizePrefix = "admin.audit";
    }

    /// <summary>
    /// Админское чтение броней: список поддержки и карточка визита.
    /// </summary>
    /// <remarks>
    /// Отдельные коды от <see cref="Booking"/>: это отдельный эндпоинт, и клиент различает
    /// ошибку фильтра админского списка и ошибку операции над бронью. Мутаций здесь нет —
    /// отмена и неявка остаются на существующих действиях <c>BookingsController</c>
    /// и отвечают кодами агрегата (<c>booking.cancel.invalidStatus</c> и другими).
    /// Курсор броней держит собственный код <c>admin.bookings.cursor.invalid</c>
    /// в <c>AdminBookingCursor</c>, как и остальные курсоры.
    /// </remarks>
    public static class AdminBooking
    {
        /// <summary>
        /// Брони с указанным идентификатором нет; <c>404</c>.
        /// </summary>
        public const string NotFound = "admin.bookings.notFound";

        /// <summary>
        /// Фильтр статуса не <c>all</c> и не имя <c>BookingStatus</c>; <c>400</c>.
        /// </summary>
        /// <remarks>
        /// Числа вроде <c>0</c> отвергаются: контракт принимает только имена статусов,
        /// хотя <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> принял бы и число.
        /// </remarks>
        public const string StatusInvalid = "admin.bookings.status";

        /// <summary>
        /// Поисковая строка длиннее <c>100</c> символов; <c>400</c>.
        /// </summary>
        public const string SearchTooLong = "admin.bookings.search";

        /// <summary>
        /// Лимит keyset-страницы не положительный; <c>400</c>.
        /// </summary>
        public const string PageLimitInvalid = "admin.bookings.page.limit";

        /// <summary>
        /// Префикс кодов проверки размера страницы; полный код — <c>admin.bookings.pageSize</c>.
        /// </summary>
        public const string PageSizePrefix = "admin.bookings";
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
        /// Размер страницы «мои отзывы» вне диапазона <c>1..100</c>; <c>400</c>.
        /// </summary>
        /// <remarks>
        /// Свой код, потому что это свой эндпоинт: клиент различает ошибку размера страницы
        /// в кабинете и в публичном списке, хотя параметр называется одинаково.
        /// </remarks>
        public const string GetMyReviewsPageSizeInvalid = "review.getMyReviews.pageSize";

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
        /// Язык в фильтре поиска барберов не является известным именем enum; <c>400</c>.
        /// </summary>
        /// <remarks>
        /// Отдельный код, а не framework-ошибка биндера: клиент различает кривой фильтр
        /// и серверный сбой и не показывает технический текст пользователю.
        /// </remarks>
        public const string BarberSearchLanguageInvalid = "barberSearch.language.invalid";

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
