# Промпт для следующей сессии: outbox для email (BarberSearch)

## Роль и задача

Ты — senior .NET-разработчик в репозитории `D:\Projects\barbersearch\backend`. Нужно сделать
доставку писем надёжной: перейти от синхронной отправки после коммита к **transactional outbox**
с фоновым диспетчером, ретраями и наблюдаемостью. Публичный HTTP-контракт не меняется.

## Стек и архитектура (кратко)

- .NET 10, ASP.NET Core, EF Core + Npgsql, PostgreSQL, xUnit, Serilog.
- Проекты: `BarberSearch.Domain`, `BarberSearch.Application`, `BarberSearch.Contracts`,
  `BarberSearch.Infrastructure.Postgresql`, `BarberSearch.Infrastructure.Email`,
  `BarberSearch.Web` + четыре тестовых проекта.
- Слои: Domain не знает EF/ASP.NET; Application — команды/запросы и порты; инфраструктура —
  адаптеры; Web — композиционный корень, контроллеры и фоновые воркеры.
- Соглашения: `TreatWarningsAsErrors`, анализаторы включены, XML-доки в коде, комментарии
  по-русски, стиль — как в соседних файлах. Миграции — `dotnet ef` из `BarberSearch.Web`.

## Текущее состояние email-слоя (после среза, который уже влит)

- `BarberSearch.Infrastructure.Email`: `SmtpEmailSender` (реализация `IEmailSender` на
  `System.Net.Mail.SmtpClient`), `EmailOptions` (секция `Email`), `EmailSenderExtensions.AddEmailSender`.
- `BarberSearch.Application.Abstractions`: порты `IEmailSender` (`IsAvailable`, `SendAsync`) и
  `EmailMessage` (To, Subject, HtmlBody, TextBody).
- Письма собираются в Application: `EmailVerificationMessage`, `PasswordResetMessage`.
- Два сценария отправки, оба **после коммита**:
  - `RequestEmailVerificationHandler` — `POST api/auth/email-verification`, кулдаун 60 с,
    TTL 24 ч, при недоступной почте `409 auth.emailVerification.unavailable`;
  - `RequestPasswordResetHandler` — `POST api/auth/password-reset`, кулдаун 60 с, TTL 30 мин,
    при недоступной почте `409 auth.passwordReset.unavailable`, остальные исходы тихие (`204`).
- Токены хранятся только хешем; открытый токен живёт в письме.
- Локально письма принимает Mailpit (`docker compose up -d`, SMTP 1025, UI 8025);
  интеграционные тесты подменяют `IEmailSender` через `TestApp` (`CapturingEmailSender`).
- Готовые паттерны для копирования: `Application/Notifications/DeliverPendingNotifications/*`
  (батч-обработчик) и `Web/Notifications/ReminderDispatchService` (воркер с `RunTickAsync`,
  `InitialTickCompleted`, интервал из настроек) + `PasswordResetTokenCleanupService`.

## Цель

Письмо не должно теряться, если SMTP недоступен в момент запроса: строка исходящего письма
пишется в БД **в той же транзакции**, что и токен, а отправляет её фоновый диспетчер с
ретраями. При этом:

- регистрация/запрос сброса продолжают работать при недоступной почте (текущие `409` — до
  записи в outbox);
- кулдаун, TTL и анти-энумерация не меняются;
- доставка — at-least-once; дубликат письма допустим, потеря — нет;
- в логах нет адреса получателя, открытого токена, тела письма.

## Обязательные требования

1. **Схема.** Таблица `email_outbox` (миграция `AddEmailOutbox`): `id` UUIDv7 PK,
   `deduplication_key` (unique, например id токена/письма), `to_email`, `subject`,
   `html_body`, `text_body`, `created_at`, `next_attempt_at`, `attempts`,
   `sent_at`, `failed_at`, `last_error` (код/тип, без PII). Индексы под выборку:
   `(sent_at, failed_at, next_attempt_at)`.
2. **Запись в транзакции.** Оба handler'а вместо `IEmailSender.SendAsync` кладут сообщение в
   outbox внутри `IUnitOfWork.ExecuteAsync`, рядом с отзывом старого и вставкой нового токена.
   Отправка из handler'а удаляется полностью.
3. **Диспетчер.** `IEmailSender` остаётся транспортом; новый application-обработчик
   `DispatchPendingEmails` выбирает пачку готовых строк (`FOR UPDATE SKIP LOCKED`), отправляет,
   помечает `sent_at` или увеличивает `attempts` и переносит `next_attempt_at`
   (экспоненциальный backoff: 1 мин, 5 мин, 15 мин, 1 ч, 6 ч; максимум 5 попыток → `failed_at`).
   Воркер в Web по образцу `ReminderDispatchService`: singleton, `RunTickAsync`,
   `InitialTickCompleted`, интервал из конфигурации, `AddHostedService(sp => sp.GetRequiredService<...>())`.
4. **Конфигурация.** Секция `EmailOutbox` (`Enabled`, `BatchSize`, `IntervalSeconds`,
   `MaxAttempts`) с валидацией в `StartupSettings`, по умолчанию выключена в `appsettings.json`
   и включена в `appsettings.Development.json`. `Enabled` управляет только воркером: handler'ы
   пишут строку всегда, когда почта настроена. Пустая/выключенная секция не роняет старт;
   при выключенном воркере письма копятся в outbox и не теряются.
5. **Идемпотентность.** `deduplication_key` защищает от двух строк на одно письмо.
   Повторная отправка после «SMTP принял, но отметка не сохранилась» допустима — описать это
   в XML-доке и AGENT.md.
6. **Наблюдаемость.** Логи: `outboxId`, `attempts`, код ошибки; метрика/лог при переходе в
   `failed`. Health-check не должен падать из-за выключенного воркера.
7. **Ограничения.** Новых NuGet-пакетов не добавлять (кроме уже используемых), MailKit не
   вводить, `System.Net.Mail.SmtpClient` остаётся транспортом. Публичные DTO/эндпоинты не менять.

## Критичное проектное решение: открытый токен в outbox

Сейчас в БД лежит только SHA-256 токена, а письмо содержит открытый токен. Outbox вынужден
хранить либо готовое тело письма, либо ссылку/открытый токен — иначе письмо не восстановить.
Это ослабляет текущую модель: строка outbox становится носителем рабочего секрета.

Выбери и задокументируй один вариант:

- **A (проще):** хранить готовые `html_body`/`text_body`; удалять строку/тело после успешной
  отправки, ретенция «отправленных» — короткая (например, 7 дней). Риск ограничен TTL токена
  (24 ч / 30 мин) и бэкапами.
- **B (строже):** хранить `template`, `user_id`, `token_id` и шифротекст открытого токена,
  зашифрованный через Data Protection (ключи уже персистентны), собирать письмо в диспетчере.

Для среза рекомендуется A с явным разделом в AGENT.md «почему это приемлемо и чем ограничено».
Не хранить открытый токен дольше, чем нужно для доставки.

## Затрагиваемые места

- `Domain/EmailOutbox/EmailOutboxMessage.cs` (или Application-модель, если решите не плодить
  агрегат) + `Infrastructure.Postgresql/Configurations`, `Repositories`, миграция.
- `Application/Abstractions/IEmailOutboxRepository.cs`, `Application/Emails/DispatchPendingEmails/*`.
- Оба `Request*Handler`: убрать `IEmailSender`, добавить outbox-репозиторий.
- `Web/Emails/EmailDispatchService.cs` (имя — по аналогии с `ReminderDispatchService`).
- `Program.cs`: регистрация воркера и опций; `StartupSettings` — валидация.
- `TestApp`: метод `RunEmailDispatchAsync()` (или автотик в Testing), чтобы тесты получали
  письма; `CapturingEmailSender` остаётся.
- Тесты, которые сейчас ждут письмо сразу после запроса, перевести на «запрос → тик → письмо».

## Тесты (минимум)

- Unit: handler пишет outbox, не вызывает sender; откат транзакции не оставляет строку;
  кулдаун/TTL/анти-энумерация не изменились; успешный тик ставит `sent_at`; ошибка SMTP
  увеличивает `attempts` и сдвигает `next_attempt_at`; после `MaxAttempts` — `failed_at`;
  `deduplication_key` не даёт дубль.
- Integration (PostgreSQL): регистрация создаёт строку outbox; тик отправляет её через
  подменный sender; два параллельных тика не отправляют одно письмо дважды; недоступный SMTP
  не теряет строку; повторный запрос после кулдауна создаёт новое письмо.
- Обновить `EmailVerificationTests`/`PasswordResetTests` (unit и integration), `TestApp`.

## Критерии готовности

- `dotnet build BarberSearch.sln` — 0 ошибок, 0 предупреждений.
- Все существующие и новые тесты зелёные (unit + integration на локальном PostgreSQL).
- При `Email:Enabled=false` поведение прежнее: `409` и ни одной строки outbox; при выключенном
  воркере строки остаются в БД и уходят после его включения.
- При недоступном SMTP запрос отвечает как раньше, а письмо остаётся в outbox и уходит после
  восстановления SMTP (проверить вручную через Mailpit: остановить/запустить контейнер).
- В логах нет email, токена и тела письма; в `last_error` — только код/тип.
- AGENT.md дополнен разделом про outbox, схему, ретраи и решение по открытому токену.

## Проверка

```powershell
dotnet build D:\Projects\barbersearch\backend\BarberSearch\BarberSearch.sln
dotnet ef migrations add AddEmailOutbox `
  --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql `
  --startup-project BarberSearch/src/BarberSearch.Web --output-dir Migrations
docker compose up -d   # Mailpit + PostgreSQL
```

Если `dotnet test` падает с `Win32Exception (5)` в testhost на `OpenProcess` — это ограничение
среды, а не тестов: запускай из IDE или используй прямой раннер по образцу уже применённого
в этой сессии рефлексионного прогона Fact/Theory.

## Чего не делать

- Не отправлять письмо внутри транзакции и не откатывать токен при сбое SMTP.
- Не менять коды ошибок и статусы `409/204`, не добавлять `Retry-After` для кулдауна.
- Не логировать PII и не хранить открытый токен дольше необходимого.
- Не делать по воркеру на сообщение и не ретраить бесконечно.
- Не переносить тексты писем из Application в инфраструктуру и не менять их смысл.

## Контекст текущего среза (что уже сделано)

`BarberSearch.Infrastructure.Email` выделен; `EmailOptions` и `SmtpEmailSender` перенесены,
добавлен `AddEmailSender`, у `IEmailSender` появился `IsAvailable` (handler'ы больше не знают
`EmailOptions`). Попутно исправлен MIME: текстовое тело — основное, HTML — `AlternateView`,
иначе обе части уходили как `text/plain`. Добавлены unit-тесты `SmtpEmailSender`
(включая локальный fake SMTP-сервер) и перенесены `EmailOptionsTests`. AGENT.md в этом срезе
намеренно не обновлялся — сделать это в outbox-срезе.
