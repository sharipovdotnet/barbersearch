# Промпт для следующей сессии: подсказки города и адреса при вводе (BarberSearch)

## Роль и задача

Ты — senior .NET-разработчик в репозитории `D:\Projects\barbersearch\backend` (git root —
`D:\Projects\barbersearch`). Нужно сделать ввод адреса салона удобным и предсказуемым:

1. барбер или администратор начинает с **города** — поле «Город» подсказывает города;
2. выбранный город фильтрует подсказки **адреса**: улица ищется уже внутри города;
3. выбор улицы/дома заполняет адрес, индекс и координаты.

Работаем **только в бэкенде**. Веб- и мобильный клиенты — следующие срезы, но HTTP-контракт
этого среза менять уже нельзя.

## Стек и архитектура (кратко)

- .NET 10, ASP.NET Core, EF Core + Npgsql, PostgreSQL, xUnit, Serilog. 13 проектов в
  `BarberSearch/BarberSearch.sln` (девять `src` + четыре тестовых), после этой работы — 14.
- Слои: `Domain` (не знает EF/ASP.NET/JSON) → `Application` (команды/запросы + порты) →
  `Infrastructure.*` (адаптеры) → `Web` (композиционный корень, контроллеры, auth).
- `TreatWarningsAsErrors`, анализаторы `latest-recommended`; XML-доки и комментарии —
  по-русски; публичные коды ошибок — по-английски. Норма: **0 ошибок, 0 предупреждений**.
- Соглашения домена/слоёв — `AGENT.md` §10. Их не пересказываю, но нарушать нельзя.

## Текущее состояние (проверено)

- `Salon` (`Domain/Salons/Salon.cs`) хранит `Address`, `City`, `Country`, `PostalCode`,
  `Latitude`, `Longitude`, `TimeZoneId`. Домен валидирует координаты по диапазону
  (`salon.latitude` / `salon.longitude`) и пояс через `TimeZoneInfo`
  (`salon.timeZoneId.unknown`); `Address`, `City`, `Country`, `TimeZoneId` обязательны.
- Форма салона на фронте — шесть ручных полей, координаты вводятся «скопируйте из карт».
- `AGENT.md` §12.10: «координаты вводятся вручную (карта/геокодер — отдельное решение)».
  Это решение принимаем сейчас; пункт §12.10 нужно обновить.
- В коде **нет** ничего про подсказки/геокодирование: поиск по
  `suggest|geocod|dadata|street` пуст. Начинаем с нуля.
- Готовые образцы для копирования:
  - `Infrastructure.Telegram/TelegramSender.cs` — именованный `HttpClient`, таймаут,
    маппинг HTTP-ошибок в `Failure`, минимум логов (без PII и секретов);
  - `Infrastructure.Telegram/TelegramSenderExtensions.cs` — регистрация настроек,
    `AddHttpClient`, синглтон-отправитель;
  - `Infrastructure.Push/PushSenderExtensions.cs` — секция настроек и DI;
  - `Application/Push/PushOptions.cs` — тип секции в Application;
  - `Web/Deployment/RateLimitSettings.cs` — политики `auth`/`reads`, глобальный лимит записи;
  - `Web/Deployment/StartupSettings.cs` — проверка диапазонов и fail-closed секретов;
  - `Web/Common/ApiController.cs` — `ToActionResult(Result<T, Failure>)`;
  - `Contracts/Common/ApiErrorCodes.cs` — реестр публичных кодов (вложенные классы по
    агрегатам);
  - `tests/BarberSearch.Infrastructure.UnitTests/PushStartupSettingsTests.cs` — образец
    тестов конфигурации;
  - `tests/BarberSearch.Infrastructure.UnitTests/Fakes/FakeTimeProvider.cs` и
    `tests/BarberSearch.Application.UnitTests/Fakes/FakeTimeProvider.cs` — управляемые часы.

## Цель

Два новых read-эндпоинта, которые проксируют подсказки от внешнего провайдера (DaData)
через бэкенд: города и адрес внутри выбранного города. Ключ провайдера не покидает сервер,
квота защищена авторизацией, лимитом и кэшем, а недоступность провайдера **не ломает** ввод:
поля остаются обычными текстовыми, подсказки просто не показываются.

Подсказки — **прогрессивное улучшение**, а не новые обязательные поля: существующие
`POST api/salons`, `POST api/salons/own`, `PUT api/salons/{id}/location` и их DTO не меняются.

## Принятые решения (не пересматривать без владельца)

1. **Сначала город, потом адрес.** Город обязателен в домене, а `fias` выбранного города даёт
   точный фильтр для улиц: подсказки приходят из нужного города, а не из всей страны.
   Поэтому в этом срезе два эндпоинта, а не один.
2. **Города тоже приходят от провайдера, а не из нашей БД.** `SELECT DISTINCT city` по
   салонам не даёт `fias`, без которого точная фильтрация невозможна, и не знает городов,
   где салонов ещё нет. Список городов из БД — отдельная задача для публичного фильтра
   поиска (там он анонимный и бесплатный), в этот срез не входит.
3. **Прокси через бэкенд, не прямой вызов из клиента.** Ключ на сервере; один контракт для
   web и mobile; смена провайдера не требует релиза клиентов.
4. **Провайдер — DaData** (HTTP API «Подсказки», `POST .../rs/suggest/address` с
   `from_bound`/`to_bound`). Адаптер спрятан за портом, поэтому второго провайдера
   (например, self-hosted Photon) можно добавить позже, не меняя Application, контроллер
   и контракт. Приватность зафиксировать в `AGENT.md`: запросы уходят вендору, ключ — только
   сервер, в логах нет ни запроса, ни адреса.
5. **`[Authorize]` на обоих эндпоинтах.** Все сценарии заполнения адреса уже
   аутентифицированы (админ создаёт салон, барбер — свой). Это бесплатно защищает платную
   квоту. Анонимных потребителей в этом срезе нет.
6. **БД и домен не трогаем.** Ни миграции, ни новых колонок (`fias_id` и нормализация
   адреса — отдельный срез). `providerId` возвращается клиенту и передаётся обратно в
   запрос адреса, но никуда не сохраняется. Сервер по-прежнему **доверяет** координатам из
   `POST api/salons` / `PUT api/salons/{id}/location`: сверка «адрес ↔ координаты»
   (геокодирование по `providerId` при сохранении) — тоже отдельный срез. Пока её нет,
   домен молча примет адрес одного города с координатами другого; это осознанная граница
   среза, и её нужно зафиксировать в `AGENT.md` §12.10, а не оставить недосмотром.
7. **`timeZoneId` в ответе адреса всегда `null`.** Поле резервируется в контракте сразу,
   чтобы следующий срез (офлайн lat/lon → IANA) не менял форму ответа. Пока пояс выбирается
   в форме вручную, как сейчас.
8. **Недоступность — `409`, а не `503`.** В `Shared.ErrorType` нет варианта «недоступно»,
   и почта/push в этом репозитории отвечают `409 …unavailable`. Не расширять `ErrorType`.
9. **Города не пишутся в домен отдельным справочником.** `Salon.City` остаётся свободным
   текстом; подсказка просто заполняет поле.
10. **Имена разведены по уровням, не переименовывать.** Проект —
    `BarberSearch.Infrastructure.Geocoding`: семейство адаптеров к внешним гео-данным.
    Название выбрано по целевому результату: салону нужны не строки, а пара
    `Latitude`/`Longitude` — они обязательны в домене и от них зависят пояс салона,
    расстояние в поиске и склейка дублей по дому. Сегодня в проекте живёт первый адаптер
    (подсказки адреса, они же приносят координаты); позже сюда же лягут геокодирование
    адреса и обратное геокодирование, отдельными портами. Фича же называется
    «address suggestions»: порт `IAddressSuggestionsProvider`, его реализация
    `DadataAddressSuggestionsProvider`, DTO `AddressSuggestionResponse`/`CitySuggestionResponse`,
    секция `AddressSuggestions`, публичные коды `addressSuggestions.*`. DI-метод —
    `AddAddressSuggestions`, по образцу `AddTelegramSender`/`AddPushSender`: он регистрирует
    порт, а не проект. Не переименовывать проект в `DaData` (вендор сменится на Photon без
    правок Application) и не называть секцию `Geocoding` (секция описывает фичу, проект —
    семейство адаптеров).

## Контракт API

### 1. Города

```
GET /api/addresses/cities?query=Екат&limit=8
```

- `query` — обязательный, `trim`; короче `AddressSuggestions:MinCityQueryLength` (2) —
  `400 addressSuggestions.query.tooShort` (город может называться «Аша» или «Уфа», порог 3
  здесь не годится); длиннее 120 — `400 addressSuggestions.query.tooLong`.
- `limit` — по умолчанию и максимум — `AddressSuggestions:MaxSuggestions` (8), зажимается
  в `1..MaxSuggestions`; значения вне диапазона не дают 400.

Успех — `200`, голый JSON-массив:

```json
[
  {
    "providerId": "2763c110-cb8b-416a-9dac-ad28a55b4402",
    "display": "г. Екатеринбург",
    "city": "Екатеринбург",
    "region": "Свердловская обл.",
    "country": "Россия",
    "latitude": 56.8389,
    "longitude": 60.5975
  }
]
```

Пустой результат — `200 []`.

### 2. Адрес

```
GET /api/addresses/suggest?query=ул.%20Лен&cityProviderId=2763c110-…&city=Екатеринбург&limit=8
```

- `query` — обязательный, `trim`; короче `MinQueryLength` (3) —
  `400 addressSuggestions.query.tooShort`; длиннее 120 — `400 addressSuggestions.query.tooLong`.
- `cityProviderId` — необязательный, `providerId` из выбора города; `trim`, 1..64 символа,
  без пробелов (opaque-токен, не `Guid`: у другого провайдера формат будет иной).
  Некорректный — `400 addressSuggestions.cityProviderId`.
- `city` — необязательный fallback: свободный текст, если человек ввёл город руками и не
  выбрал подсказку; `trim`, длиннее 100 — `400 addressSuggestions.city.tooLong`.
- Приоритет: `cityProviderId` → фильтр `locations: [{ "city_fias_id": … }]`; иначе непустой
  `city` → `locations: [{ "city": … }]`; иначе запрос без фильтра.
- `limit` — как у городов.

Успех — `200`, массив:

```json
[
  {
    "providerId": "f0a1b2c3-...-fias-id",
    "display": "г. Екатеринбург, ул. Ленина, д. 1",
    "address": "ул. Ленина, 1",
    "city": "Екатеринбург",
    "region": "Свердловская обл.",
    "country": "Россия",
    "postalCode": "620000",
    "latitude": 56.8389,
    "longitude": 60.5975,
    "timeZoneId": null
  }
]
```

Правила заполнения полей адреса:

- `display` — `value` из ответа DaData: единственная строка, которую видит пользователь.
- `address` — `street_with_type` + `house` (через запятую, без города). Если улицы нет —
  `value` целиком. В поле `Salon.Address` не должен попадать город: он идёт отдельно.
- `city` — `data.city`; **подсказка без города отбрасывается** (город обязателен в домене,
  а подсказка уровня «регион» форме ничего не даёт).
- `country` — `data.country`; если пусто — **дропнуть подсказку** (в домене обязателен).
- `postalCode`, `region` — как есть, `null` допустим.
- `latitude` / `longitude` — `geo_lat` / `geo_lon` как `double?` (DaData отдаёт строки,
  парсить `InvariantCulture`); `null`, если не распарсилось.
- `providerId` — `fias_id`, иначе `kladr_id`; `null` допустим.

Ошибки (оба эндпоинта): `400` — коды выше; `401` — аноним;
`409 addressSuggestions.unavailable` — фича выключена, пустой ключ, провайдер ответил
`4xx/5xx`, таймаут или сеть; `429` — лимитер.

## Обязательные требования

### 1. Application: порт, опции, обработчики

- `Application/Addresses/IAddressSuggestionsProvider.cs` — порт:
  `Task<Result<IReadOnlyList<AddressSuggestion>, Failure>> SuggestAsync(AddressSuggestionRequest request, CancellationToken cancellationToken)`.
- `Application/Addresses/AddressSuggestion.cs` — провайдер-нейтральная модель подсказки
  (`ProviderId`, `Display`, `Address`, `City`, `Region`, `Country`, `PostalCode`,
  `Latitude`, `Longitude`); `AddressSuggestionRequest.cs` — `Query`, `Bound`, `CityProviderId`,
  `City`, `Limit`; `AddressSuggestionBound.cs` — `City = 1`, `Address = 2` (enum, значения
  явные, как в §10.10).
- `Application/Addresses/AddressSuggestionsOptions.cs` — секция настроек, по образцу
  `Application/Push/PushOptions.cs`: `SECTION = "AddressSuggestions"`, `Enabled`, `BaseUrl`,
  `ApiKey`, `TimeoutSeconds`, `MinQueryLength` (3), `MinCityQueryLength` (2),
  `MaxSuggestions` (8), `CacheTtlSeconds` (900),
  `IsConfigured` (`Enabled && ApiKey не пуст && BaseUrl — абсолютный URI`; схему `https`
  проверяет только `StartupSettings` в `Production`, иначе локальный стаб по `http`
  было бы не проверить). Тип живёт в Application, а не в инфраструктуре, потому что границы
  запроса нужны обработчикам: Application не может ссылаться на Infrastructure.
- `Application/Addresses/AddressSuggestionLimits.cs` — пределы длин `query` (120) и `city`
  (100) и длины `cityProviderId` (64) константами: не магические числа в контроллере и не
  настройки.
- `Application/Addresses/AddressSuggestionQueryValidator.cs` — `internal static` помощник
  с общей частью обоих обработчиков: `trim`, схлопывание пробелов, проверки длин, зажим
  `limit`, проверка `cityProviderId`. Один источник правил, чтобы города и адрес не
  разъехались.
- `Application/Addresses/SuggestCities/SuggestCitiesQuery.cs` + `SuggestCitiesHandler.cs`
  (`IQueryHandler<IReadOnlyList<CitySuggestionResponse>, SuggestCitiesQuery>`) и
  `Application/Addresses/SuggestAddresses/SuggestAddressesQuery.cs` + `SuggestAddressesHandler.cs`
  (`IQueryHandler<IReadOnlyList<AddressSuggestionResponse>, SuggestAddressesQuery>`):
  опции приходят в конструктор (как `PushOptions` у push-обработчиков), валидация — через
  помощник, вызов порта с нужным `Bound`, маппинг в контракт, проброс `CancellationToken`.
  Если `!Options.IsConfigured` — сразу ошибка `addressSuggestions.unavailable`, порт
  **не вызывается**: выключенная фича не должна зависеть от того, какой адаптер
  зарегистрирован (в тестах и в проде он разный).
  Ошибку порта превращать в `Error.Conflict(ApiErrorCodes.AddressSuggestions.Unavailable, …)`
  — текст английский, без деталей провайдера и без адреса.
- Никакого `IUnitOfWork`, репозиториев и домена: это чистое чтение.

### 2. Contracts: DTO и коды ошибок

- `Contracts/Addresses/SuggestCities/CitySuggestionResponse.cs` — `record` с `<param>`-доками
  на русском; `timeZoneId` здесь **нет** (городу он не нужен, а обещать его в контракте
  рано).
- `Contracts/Addresses/SuggestAddresses/AddressSuggestionResponse.cs` — `record` с полями
  в порядке из примера; `timeZoneId` — `string?` и `null` в этом срезе (причина — в
  `<remarks>`).
- `ApiErrorCodes.AddressSuggestions`: `QueryTooShort = "addressSuggestions.query.tooShort"`,
  `QueryTooLong = "addressSuggestions.query.tooLong"`,
  `CityTooLong = "addressSuggestions.city.tooLong"`,
  `CityProviderIdInvalid = "addressSuggestions.cityProviderId"`,
  `Unavailable = "addressSuggestions.unavailable"`. Валидационные — `Error.Validation`
  с `invalidField`, недоступность — `Error.Conflict`, как в §10.7.

### 3. Новый проект `BarberSearch.Infrastructure.Geocoding`

`src/BarberSearch.Infrastructure.Geocoding/` — проект назван по семейству адаптеров (см.
решение 10), а классы внутри — по фиче, которую реализуют: `DadataAddressSuggestionsProvider`,
`CachedAddressSuggestionsProvider`. Никакого `DadataGeocodingProvider` быть не должно: сейчас
геокодирования (адрес → координаты) в коде нет, и имя класса не должно обещать больше, чем
он делает.

- `.csproj` — копия `BarberSearch.Infrastructure.Telegram.csproj` (те же три
  `PackageReference` 10.0.10: `DependencyInjection.Abstractions`, `Http`, `Logging.Abstractions`;
  ссылки на `Application`, `Contracts`, `Shared`). **Новых NuGet-пакетов не добавлять.**
  `AddressSuggestionsOptions` в этом проекте **нет** — он в Application (см. п. 1).
- `DadataAddressSuggestionsProvider.cs` — именованный `HttpClient` `"geocoding"`:
  - `POST {BaseUrl}` с `Authorization: Token {ApiKey}`, `Accept: application/json`;
  - тело: `{"query":"…","count":N}` плюс
    - `"from_bound":{"value":"city"},"to_bound":{"value":"city"}` для `Bound == City`;
    - `"locations":[{"city_fias_id":"…"}]` при `CityProviderId`, иначе
      `"locations":[{"city":"…"}]` при `City`, иначе без `locations` — для `Bound == Address`.
      Для городов `locations` не передаётся вообще;
  - разбор через `System.Text.Json` с явными `[JsonPropertyName]`; DTO ответа — приватные
    вложенные типы, наружу не выходят;
  - **никаких повторов**: пустой ответ — валидный `200 []`. Повтор без фильтра вернул бы
    улицы чужих городов, а с выбранным городом пустой ответ означает «в этом городе такой
    улицы нет»;
  - ошибки: не-2xx → `Error.Failure(ApiErrorCodes.AddressSuggestions.Unavailable, …)`;
    таймаут (`OperationCanceledException` без отмены вызывающим), `HttpRequestException`,
    битый JSON — то же; в лог только статус/тип, **никогда** запрос, адрес или ключ;
  - `IsConfigured == false` — HTTP-вызов не выполняется вообще, сразу `Failure`.
- `CachedAddressSuggestionsProvider.cs` — декоратор над портом: `ConcurrentDictionary`,
  ключ = нормализованные `bound|query|cityProviderId|city|limit` (trim, схлопнутые пробелы,
  `ToLowerInvariant`), TTL из `CacheTtlSeconds`, жёсткий предел 2048 записей (при
  переполнении сначала выкинуть истёкшие, затем самые старые). Время — только из
  `TimeProvider`. Кэшируются успешные ответы, включая пустой список; `Failure` не кэшируется.
- `AddressSuggestionsExtensions.cs` — `AddAddressSuggestions(this IServiceCollection, AddressSuggestionsOptions)`:
  регистрирует опции синглтоном, `AddHttpClient` с таймаутом, `DadataAddressSuggestionsProvider`
  и оборачивает его кэшем (`IAddressSuggestionsProvider` → `CachedAddressSuggestionsProvider`
  → `DadataAddressSuggestionsProvider`). Стиль — как `TelegramSenderExtensions`.

### 4. Проект решения и Web

- Добавить проект в `.sln` **вручную** (`dotnet sln add` запрещён — §5.1): две строки
  `Project(…)`/`EndProject` и четыре строки `ProjectConfigurationPlatforms` для
  `Debug|Any CPU` и `Release|Any CPU`, отступы — табы, как в соседних записях.
  GUID: `{6E1F3A57-2C4B-49D8-8F0A-7B3D5C9E1A24}`, тип проекта —
  `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}`, путь —
  `src\BarberSearch.Infrastructure.Geocoding\BarberSearch.Infrastructure.Geocoding.csproj`.
  Не вкладывать проект в несуществующую папку решения.
- `BarberSearch.Web.csproj` — `ProjectReference` на новый проект.
- `Web/Controllers/AddressesController.cs` — `[ApiController]`, `[Route("api/addresses")]`,
  наследник `ApiController`, `[Authorize]`,
  `[EnableRateLimiting(RateLimitSettings.AddressesPolicyName)]` на классе, два действия:
  - `GET api/addresses/cities` — `Cities([FromQuery] string query, [FromQuery] int? limit, …)`;
  - `GET api/addresses/suggest` — `Suggest([FromQuery] string query, [FromQuery] string? cityProviderId, [FromQuery] string? city, [FromQuery] int? limit, …)`.

  XML-доки на русском: что делает, кто может, какой лимит, какие коды, и что
  `cityProviderId` — `providerId` из ответа городов.
- `Web/Deployment/RateLimitSettings.cs` — политика `addresses`:
  `AddressesPolicyName = "addresses"`, `AddressesPermitLimit = 60` в минуту; партиция —
  тот же ключ, что у лимита записи (сначала claim `sub`, иначе `RemoteIpAddress`, иначе
  `unknown`), чтобы лимит был на пользователя. Зарегистрировать в `Configure`.
  **GET не проходит через глобальный лимит записи** — это проверяет существующий порядок
  middleware, менять его не нужно.
- `Web/Deployment/StartupSettings.cs` — новое свойство `AddressSuggestions` типа
  `AddressSuggestionsOptions` (из `BarberSearch.Application.Addresses`, как `Push` из
  `Application.Push`), заполняется приватным `ReadAddressSuggestions(configuration)`:
  `Enabled` — мягкий флаг, `BaseUrl` по умолчанию
  `https://suggestions.dadata.ru/suggestions/api/4_1/rs/suggest/address`,
  `ApiKey` — без проверки значения, диапазоны `TimeoutSeconds 1..15`,
  `MinQueryLength 2..10`, `MinCityQueryLength 1..10`, `MaxSuggestions 1..10`,
  `CacheTtlSeconds 0..86400` (0 = кэш выключен).
  `Enabled=true` при пустом `ApiKey` — **падение старта** с сообщением, называющим переменную
  `AddressSuggestions__ApiKey`, но не значение. `Enabled=false` и пустой ключ — штатное
  состояние, старт не падает. `BaseUrl` при `Enabled=true` — абсолютный URI; в `Production`
  только `https`, в `Development`/`Testing` `http` разрешён (локальный стаб).
- `Program.cs` — `builder.Services.AddAddressSuggestions(startup.AddressSuggestions);`
  рядом с `AddTelegramSender`/`AddPushSender`, с коротким комментарием «почему выключенный
  провайдер не роняет старт».
- `appsettings.json` — секция `AddressSuggestions` с `Enabled: false`, пустым `ApiKey` и
  дефолтным `BaseUrl`. `appsettings.Development.json` — тот же `Enabled: false`
  (ключа в репозитории нет; разработчик включает локально user-secrets + env).

### 5. Безопасность и наблюдаемость

- Ключ живёт только в `AddressSuggestions:ApiKey` (env `AddressSuggestions__ApiKey`,
  user-secrets в Development). В коде, appsettings, логах и ответах его нет.
- В логах — провайдер, bound, статус, длительность, длина запроса. **Не** логировать
  `query`, адрес, координаты, ключ, тело ответа.
- Таймаут одного вызова — из опций, без ретраев на уровне `HttpClient`
  (подсказка, пришедшая через 5 секунд, хуже её отсутствия).
- Эндпоинты ничего не пишут в БД, в аудит и не трогают домен.

### 6. Документация

- `AGENT.md`: §3/§4 — 14 проектов, десять `src`, дерево с новым проектом;
  §5.1 — обновить ожидаемый вывод `dotnet sln list`; §6 — граф зависимостей;
  §12.10 — решение по подсказкам адреса (прокси, DaData, города → адрес по `fias`,
  авторизованный эндпоинт, приватность, что осталось за срезом: `fias_id` в БД,
  нормализация, пояс из координат, города из БД для публичного поиска);
  §16 — две строки в таблице эндпоинтов (метод, путь, доступ, лимит, коды, форма ответа).
  Числа в шапке файла («13 projects», «nine src», счётчики тестов) поправить по всему
  тексту — поиск по строке `13 projects`, `nine src`, `thirteen`.
- `BarberSearch/src/BarberSearch.Web/BarberSearch.Web.http` — два примера запросов
  (города и адрес) с комментарием про cookie-сессию.

## Затрагиваемые места

| Что | Файл/папка |
|---|---|
| Новый проект | `BarberSearch/src/BarberSearch.Infrastructure.Geocoding/` (4 файла) |
| Порт, опции, обработчики | `BarberSearch.Application/Addresses/` (`IAddressSuggestionsProvider`, `AddressSuggestion`, `AddressSuggestionRequest`, `AddressSuggestionBound`, `AddressSuggestionsOptions`, `AddressSuggestionLimits`, `AddressSuggestionQueryValidator`, `SuggestCities/*`, `SuggestAddresses/*`) |
| Контракты и коды | `BarberSearch.Contracts/Addresses/SuggestCities/CitySuggestionResponse.cs`, `Addresses/SuggestAddresses/AddressSuggestionResponse.cs`, `Common/ApiErrorCodes.cs` |
| Контроллер | `BarberSearch.Web/Controllers/AddressesController.cs` |
| Лимитер | `BarberSearch.Web/Deployment/RateLimitSettings.cs` |
| Конфигурация | `BarberSearch.Web/Deployment/StartupSettings.cs`, `Program.cs`, `BarberSearch.Web.csproj`, `appsettings.json`, `appsettings.Development.json` |
| Решение | `BarberSearch/BarberSearch.sln` (вручную) |
| Документация | `AGENT.md`, `BarberSearch.Web.http` |
| Тесты | `tests/BarberSearch.Application.UnitTests/`, `tests/BarberSearch.Infrastructure.UnitTests/` (+ `ProjectReference` на новый проект в его `.csproj`), `tests/BarberSearch.IntegrationTests/` |

## Тесты (минимум)

**Application.UnitTests** (`AddressSuggestionTests.cs`, фейковый порт):

- trim и схлопывание пробелов в `query`/`city`; пустой `city` → `null`;
- города: `query` из 1 символа → `400 addressSuggestions.query.tooShort`, порт не вызывается;
  из 2 — вызывается;
- адрес: `query` из 2 символов → `400 addressSuggestions.query.tooShort` (порог другой!);
- `query` длиннее 120 → `addressSuggestions.query.tooLong`;
- `city` длиннее 100 → `addressSuggestions.city.tooLong`;
- `cityProviderId` из 65 символов или с пробелом → `400 addressSuggestions.cityProviderId`;
- `limit` `null` → 8, `0` → 1, `999` → `MaxSuggestions` (8), `5` → 5;
- города → `Bound == City` и `CityProviderId` не пробрасывается; адрес → `Bound == Address`;
- `cityProviderId` важнее свободного `city` (в порт уходит `cityProviderId`, а `city`
  остаётся для фолбэка);
- маппинг адреса сохраняет порядок и значения полей; `timeZoneId` всегда `null`;
- города маппятся в `CitySuggestionResponse` без `postalCode`/`address`;
- `Failure` порта → `Error.Conflict` с кодом `addressSuggestions.unavailable`;
- пустой список порта → пустой список, не ошибка;
- выключенные опции (`IsConfigured == false`) → `409`-ошибка, порт не вызывается.

**Infrastructure.UnitTests** (стаб `HttpMessageHandler` + `FakeTimeProvider`):

- `DadataAddressSuggestionsProvider`: метод `POST`, URL из `BaseUrl`, заголовок
  `Authorization: Token …`;
- bound `City` → `from_bound`/`to_bound` = `city`, `locations` отсутствует;
- bound `Address` + `cityProviderId` → `locations:[{"city_fias_id": …}]`;
  + только `city` → `locations:[{"city": …}]`; без обоих → `locations` нет;
- разбор `value`, `street_with_type` + `house` → `address`, `geo_lat`/`geo_lon` → `double`,
  отброс подсказок без `city`/`country`;
- пустой ответ → `200 []`-эквивалент (`Success` + пустой список), **без повторного запроса**
  (проверить число вызовов стаба);
- `500`, `400`, битый JSON, таймаут → `Failure` с кодом `addressSuggestions.unavailable`,
  в тексте ошибки нет адреса и ключа;
- `IsConfigured == false` → `Failure`, HTTP-обработчик не вызван ни разу;
- `CachedAddressSuggestionsProvider`: второй одинаковый запрос не доходит до внутреннего
  провайдера; другой `bound`/`cityProviderId`/`limit` — другой ключ; после `TTL` внутренний
  провайдер вызван снова; `Failure` не кэшируется; при `CacheTtlSeconds = 0` каждый запрос
  идёт дальше.

**Infrastructure.UnitTests** (`AddressSuggestionsStartupSettingsTests.cs`, по образцу
`PushStartupSettingsTests`):

- `Enabled=false` + пустой ключ → старт проходит, дефолты на месте;
- `Enabled=true` + пустой ключ → `InvalidOperationException`, в сообщении есть имя ключа
  и нет значения;
- `TimeoutSeconds`/`MinQueryLength`/`MinCityQueryLength`/`MaxSuggestions`/`CacheTtlSeconds`
  вне диапазона → исключение; нецелое значение → исключение;
- `Enabled=true` + `http://` в `Production` → исключение; то же в `Development` → проходит.

**IntegrationTests** (`AddressSuggestionsTests.cs`, `TestApp` с фейковым портом):

- аноним → `401` на обоих эндпоинтах;
- авторизованный (барбер или админ) → `200`, имена полей JSON ровно как в контрактах;
- города: 2 символа → `200`; адрес: 2 символа → `400` и `invalidField = "query"`;
- `cityProviderId` доходит до порта (фейк фиксирует запрос);
- порт вернул `Failure` → `409` и код `addressSuggestions.unavailable`;
- `Enabled=false` (пустые опции) → `409`, фейковый провайдер не вызывается ни разу
  (проверка сидит в обработчике, а не только в адаптере).

`TestApp` расширить параметром-фейком провайдера (как уже сделано для `TimeProvider`),
чтобы тесты не ходили в сеть. Если у `RateLimitOrderTests`/`StartupSettingsTests` есть
проверки числа политик — обновить.

## Критерии готовности

- `dotnet build BarberSearch/BarberSearch.sln --nologo` — 0 ошибок, 0 предупреждений
  (Debug и Release).
- `dotnet sln list` показывает 14 проектов, включая новый; вложенность решения не сломана.
- Все новые и существующие тесты зелёные: `Domain`, `Application`, `Infrastructure`
  (без БД) и `Integration` (локальный PostgreSQL / Testcontainers).
- При `Enabled=false` старт проходит без ключа, оба эндпоинта отвечают
  `409 addressSuggestions.unavailable`, провайдер не вызывается ни разу.
- При `Enabled=true` и живом ключе: «Екат» → город, выбор города, затем «ул. Лен» → улицы
  **этого** города; выбор улицы заполняет адрес/город/страну/индекс/координаты.
- В логах за весь прогон нет ни одного адреса, ключа и тела ответа провайдера.
- `AGENT.md` обновлён (§3, §4, §5.1, §6, §12.10, §16, шапка) и не противоречит коду.
- Ни одной миграции, ни одного изменения домена и существующих DTO.

## Проверка

```powershell
# сборка (в confined-сессии harness может потребовать danger-full-access)
dotnet build BarberSearch/BarberSearch.sln --nologo
dotnet build BarberSearch/BarberSearch.sln --nologo -c Release

# unit-тесты: вывод вне workspace, иначе testhost падает с Win32Exception (5) — §5.2
dotnet test tests/BarberSearch.Application.UnitTests --nologo --verbosity quiet `
  "-p:BaseOutputPath=$env:TEMP\bs-tests\Application.UnitTests\"
dotnet test tests/BarberSearch.Infrastructure.UnitTests --nologo --verbosity quiet `
  "-p:BaseOutputPath=$env:TEMP\bs-tests\Infrastructure.UnitTests\"
dotnet test tests/BarberSearch.IntegrationTests --nologo --verbosity quiet `
  "-p:BaseOutputPath=$env:TEMP\bs-tests\IntegrationTests\"

# запуск API
dotnet run --project BarberSearch/src/BarberSearch.Web

# ручная проверка (в другом окне): ключ — только в env, не в appsettings
$env:AddressSuggestions__Enabled = 'true'
$env:AddressSuggestions__ApiKey  = '<ключ DaData>'

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod -Method Post -Uri 'http://localhost:5243/api/auth/login' `
  -WebSession $session -ContentType 'application/json' `
  -Body '{"email":"<админ>","password":"<пароль>"}'

Invoke-RestMethod -Uri 'http://localhost:5243/api/addresses/cities?query=Екат' `
  -WebSession $session

Invoke-RestMethod -Uri 'http://localhost:5243/api/addresses/suggest?query=ул.%20Лен&cityProviderId=<fias из городов>' `
  -WebSession $session
```

В confined-сессии harness сети нет: живой DaData недоступен. Для проверки без интернета
поднять локальный стаб (например, `python -m http.server` с заранее сохранёнными JSON
ответами DaData — отдельными файлами для городов и для улиц, различаемыми по телу запроса)
и выставить `AddressSuggestions__BaseUrl` на него — контракт адаптера это позволяет.

## Чего не делать

- Не менять `CreateSalonRequest`, `UpdateSalonLocationRequest`, `Salon`, его конфигурацию
  EF и миграции. Никаких `fias_id`, `normalized_address` и `PostGIS` в этом срезе.
- Не добавлять третье поле в форму и не делать `cityProviderId` обязательным: город,
  введённый руками, должен работать как раньше (фолбэк по строке, затем без фильтра).
- Не вызывать провайдера из контроллера напрямую и не тащить `HttpClient` в Application.
- Не добавлять новые NuGet-пакеты, `Polly`, `Refit`, `GeoTimeZone`,
  `Microsoft.Extensions.Caching.Memory`. Кэш — свой, на `ConcurrentDictionary` + `TimeProvider`.
- Не повторять запрос без `locations` при пустом ответе: с выбранным городом пустой ответ
  осмыслен, а повтор вернёт улицы чужих городов.
- Не возвращать `200 []` при выключенной фиче или упавшем провайдере: клиент покажет
  «ничего не найдено» и соврёт. Только `409 addressSuggestions.unavailable`.
- Не логировать `query`, адрес, координаты, ключ и тело ответа; не писать ключ в
  `appsettings*.json`.
- Не добавлять проект через `dotnet sln add` и не расширять `Shared.ErrorType` ради `503`.
- Не переименовывать проект в `BarberSearch.Infrastructure.DaData` и не переносить адаптер
  в `Web/Addresses`: проект — семейство гео-адаптеров, вендор меняется конфигурацией
  и вторым классом в том же проекте (см. решение 10).
- Не менять коды/статусы существующих эндпоинтов и не делать адрес обязательным полем формы.
- Не обновлять `AGENT.md` «по факту» — обновить его и проверить, что §5.1 и §4 не врут.

## Следующий срез (фронт, не делать здесь)

`frontend/src/components/salon/salon-form.tsx` получает два ARIA-combobox:

1. **«Город»** — подсказки с 2 символов, дебаунс 250 мс, `AbortController`;
   выбор заполняет `city`, `country` (и `region` только для показа, в API не уходит) и
   сохраняет `providerId` в состоянии компонента — **не** в значениях формы, с которыми
   потом идёт `POST/PUT`.
2. **«Адрес»** — запросы только когда город выбран (ручной ввод города без выбора тоже
   годится: тогда уходит `city`); выбор заполняет `address`, `postalCode`, `latitude`,
   `longitude`.

Правила, без которых данные разъедутся:

- хранить признак происхождения полей (`fromSuggestion`): правка строки адреса сбрасывает
  только то, что пришло из подсказки — координаты и индекс. Ручной ввод координат и индекса
  не трогать, иначе форма «выбросит» уже введённые человеком значения;
- смена города после выбора адреса сбрасывает адресную подсказку и всё, что из неё пришло
  (`address`, `postalCode`, `latitude`, `longitude`); `city`/`country` — нет, их только что
  выбрали;
- `timeZoneId` остаётся ручным;
- `409 addressSuggestions.unavailable` — молча спрятать подсказки и оставить обычный ввод;
  `429` — не долбить повторами.

## Следующий срез (бэкенд): координаты от провайдера, а не из формы

Проект назван `Geocoding` именно ради этого: сейчас координаты просто долетают из подсказки
в тело `POST/PUT`, и домен проверяет только диапазон. Отдельным срезом:

- `POST api/addresses/resolve` (или `GET .../{providerId}`) — канонический адрес и
  координаты по `providerId`; тот же проект и тот же именованный `HttpClient`, отдельный
  порт (условно `IAddressGeocoder`) и тот же лимитер;
- `CreateSalonRequest`/`UpdateSalonLocationRequest` получают необязательный
  `addressProviderId`: если он есть и провайдер настроен, **сервер** берёт `latitude`/
  `longitude` (и, возможно, `postalCode`/`city`) из провайдера, а присланные клиентом
  координаты игнорирует; если нет — поведение прежнее;
- в `salons` появляются `address_provider_id` и признак точности (`qc_geo` у DaData),
  чтобы различать «дом» и «город» и не склеивать дубли по городу;
- `AGENT.md` §12.10 фиксирует, что ручной ввод координат остаётся только как fallback
  при выключенном провайдере.
