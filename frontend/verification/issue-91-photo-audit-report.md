# Issue 91 — аудит действий с фото: журнал решений и загрузок (BarberSearch)

Ветка `features/issue-91`, один PR в `main` (backend и frontend вместе). Срез закрывает
долги #31/#32: **каждое действие с фотографией оставляет строку в `audit_log`, а
администратор видит решение в `/admin/audit`.**

Дата прогонов: 2026-11-12. Миграций в срезе нет: `audit_log.action`/`entity_type` —
`varchar(64)`, `payload` — `jsonb`, новые строки помещаются без изменения схемы.

## Что сделано

### Backend

- **Восьмой тип аудита.** `AuditTrail.AUDITED_ENTITIES` получил `PortfolioPhoto` →
  `portfolio_photo` с allowlist `BarberProfileId`, `Url`, `Description`, `SortOrder`,
  `IsPublished`, `IsBlocked`, `ModerationStatus`; `AuditsCreation` остаётся `true` (кадров
  максимум десять на профиль). `PortfolioPhoto` — единственный ребёнок `BarberProfile` в
  allowlist: загрузка/удаление кадра — жизненный цикл контента, а `BarberService`,
  `BarberSalon`, `WorkingSchedule` и `TimeOff` остаются вне журнала.
- **Аватар.** В allowlist `User` к `IsAvatarBlocked` добавлены `AvatarUrl` и
  `AvatarModerationStatus`. Без `AvatarUrl` строка загрузки/удаления не называла бы файл
  (`PortfolioPhoto.Id` и `mediaId` в URL — разные GUID'ы), а `AvatarModerationStatus`
  меняется при загрузке (`Approved → Pending`). `AvatarRejectionReason`, `AvatarSubmittedAt`,
  `AvatarReviewedAt`, `AvatarReviewedByUserId`, `RejectionReason`, `ReviewedAt`,
  `ReviewedByUserId` и `ThumbnailUrl` в payload **не** входят.
- **Чтение.** `AdminAuditEntityType.PortfolioPhoto = 8` (в конец, числовые значения стабильны),
  `AdminAuditEntityTypes.PORTFOLIO_PHOTO = "portfolio_photo"` с ветками `TryParse`/`ToStorageName`,
  `portfolio_photo` в тексте ошибки `admin.audit.entityType` (код не изменился) и в XML-доках
  `AdminAuditController`, `AuditLogEntry`, `AdminAuditEntryResponse`. Репозиторий, курсор,
  порядок, `pageSize`, `actor`, `from`/`to` и разбор payload не тронуты.
- **Комментарии-обещания.** Переписаны места, которые стали ложью: «URL не входит»,
  «дети не входят», «решение в `audit_log` не пишется» в `AuditTrail`,
  `PortfolioPhotosController` и `AdminUsersController`.
- **Что попадает в журнал.** `portfolio_photo.created` (загрузка: профиль, `url`, подпись,
  порядок, флаги, `moderationStatus = Pending`), `portfolio_photo.updated` (только изменённые
  поля: подпись, `sortOrder`, `isPublished`, `isBlocked`, `moderationStatus`),
  `portfolio_photo.deleted` (`values` с `url`), `user.updated` для загрузки/замены/удаления
  аватара и решений по нему. No-op и отказы (`409`, `403`, `400`, падение `SaveChanges`)
  строк не пишут.

### Frontend

- **`src/lib/admin-audit.ts`**: девятое значение фильтра `portfolio_photo` (в конец
  `ADMIN_AUDIT_ENTITY_TYPES`, прежние восемь не сдвигаются), подпись «Фото портфолио», шесть
  подписей полей (`url` — «Файл», `avatarUrl` — «Фото профиля», `sortOrder` — «Порядок»,
  `isBlocked` — «Скрыто администратором», `moderationStatus` — «Статус проверки»,
  `avatarModerationStatus` — «Статус проверки фото»).
  `formatAuditValue(field, value, entityType?)` переводит `moderationStatus` по типу
  сущности (`salon` → `SALON_MODERATION_LABELS`, `portfolio_photo` →
  `PHOTO_MODERATION_LABELS`) и `avatarModerationStatus` через `photoModerationLabel`;
  неизвестное значение остаётся строкой сервера.
- **`audit-entry.tsx`**: оба вызова `formatAuditValue` получают `entry.entityType`; раскладка
  не менялась, длинный URL переносится (`break-words` + `min-w-0`) и на 320 px не даёт
  горизонтального скролла.
- **`audit-screen.tsx`**: изменений не потребовалось — select строится из
  `ADMIN_AUDIT_ENTITY_TYPES`/`_LABELS`, запрос уже отправляет `entityType`.
- **i18n**: семь новых строк в `src/i18n/messages/en/admin-libs.json`; `check:i18n` — 0
  непереведённых, 0 конфликтов.
- **`response-guards.ts`**: изменений нет; guard пропускает `entityType: 'portfolio_photo'`
  (закреплено тестом).

## Проверка

### Backend

```
> dotnet build BarberSearch/BarberSearch.sln
Сборка успешно завершена.  Предупреждений: 0  Ошибок: 0

> dotnet test tests/BarberSearch.Domain.UnitTests         → пройдено 588
> dotnet test tests/BarberSearch.Application.UnitTests    → пройдено 878
> dotnet test tests/BarberSearch.Infrastructure.UnitTests → пройдено 317
> dotnet test tests/BarberSearch.IntegrationTests         → пройдено 292
итого 2075, падений и пропусков нет

> dotnet ef migrations has-pending-model-changes --project …Infrastructure.Postgresql \
      --startup-project …Web --no-build
No changes have been made to the model since the last migration.
```

Новые backend-тесты: 24 факта в `AuditTrailTests` (создание фото с `url`/`moderationStatus`,
подпись, `ReorderPhoto`/`ReorderPhotos` и no-op, публикация, блокировка/разблокировка и
повторная блокировка, `Approve`/`Reject` без причины и `reviewedByUserId`, удаление с `url`,
обрезка подписи до 500 символов с `...`, загрузка/замена/удаление аватара и решения по нему);
`AdminAuditReadHandlerTests` (`TryParse` `portfolio_photo`/` PORTFOLIO_PHOTO `/отказы
`photo`/`photos`/`8`, `ToStorageName`); `PhotoAuditCoverageTests` (13 интеграционных фактов
на реальном API) и переписанные `AdminAuditCoverageTests`/`PhotoModerationTests`.

Лог сборки — `verification/issue-91-backend-build.txt`, миграции —
`verification/issue-91-migrations.txt`.

### Аудит: payload'ы из `audit_log`

```sql
SELECT action, entity_type, entity_id, payload
FROM audit_log
WHERE entity_type = 'portfolio_photo'
ORDER BY occurred_at DESC
LIMIT 10;
```

Реальные строки (E2E-сценарии портфолио, `verification/issue-91-audit-sql.txt`):

```
portfolio_photo.created | 01a1cdf7… | {"values": {"url": "/api/media/portfolio/9e100000-…/01a1cdf7….jpg", "isBlocked": false, "sortOrder": 0, "description": "E2E Работа", "isPublished": true, "barberProfileId": "9e100000-…", "moderationStatus": "Pending"}}
portfolio_photo.updated | 01a1cdf7… | {"changes": {"moderationStatus": {"to": "Approved", "from": "Pending"}}}
portfolio_photo.updated | 01a1cdf7… | {"changes": {"isBlocked": {"to": true, "from": false}}}
portfolio_photo.updated | 01a1cdf7… | {"changes": {"isBlocked": {"to": false, "from": true}}}
portfolio_photo.updated | 01a1cdf7… | {"changes": {"moderationStatus": {"to": "Rejected", "from": "Pending"}}}
portfolio_photo.deleted | 01a1cdf7… | {"values": {"url": "/api/media/portfolio/9e100000-…/01a1cdf7….jpg", "isBlocked": false, "sortOrder": 0, "description": "E2E Работа", "isPublished": true, "barberProfileId": "9e100000-…", "moderationStatus": "Approved"}}
```

Причины отказа (`rejectionReason`/`avatarRejectionReason`), `reviewedAt`, `reviewedByUserId` и
`thumbnailUrl` в payload отсутствуют; `updated` содержит только реально изменённые поля.

### API

`GET api/admin/audit` под администратором (`verification/issue-91-api-audit.txt`):

| Запрос | Статус | Результат |
|---|---|---|
| `?entityType=portfolio_photo&from=2026-11-12&to=2026-11-12` | `200` | только фото-строки (`created`/`updated`/`deleted`), включая строки удалённых кадров |
| `?entityType=%20PORTFOLIO_PHOTO%20` | `200` | регистр и пробелы принимаются |
| `?entityType=photo` | `400` | `admin.audit.entityType` |
| `?entityType=8` | `400` | `admin.audit.entityType` |
| `?entityType=all&from=…&to=…` | `200` | `entityTypes=["user","portfolio_photo"]`, фото-строки входят |

### Frontend

```
> npm run lint              → 0 warnings, 0 errors (510 files)
> npm test                  → 131 files, 1394 tests passed (было 1382, +12)
> npm run build             → успешно, 2589 модулей
> npm run check:i18n        → 1721 t()-строк, 2035 переводов, 0 missing, 0 conflicts
> npx tsc --noEmit -p tsconfig.e2e.json → чисто
```

Бандл: `index-*.js` 353.11 kB (gzip 114.50), `admin-page-*.js` 300.73 / 61.76 kB (+0.49 raw /
+0.12 gzip к #90 — подписи и `formatAuditValue`), `index-*.css` 90.01 / 15.39 kB без изменений.

Новые frontend-тесты: `admin-audit.test.ts` (девятое значение, подписи, `moderationStatus`
для салона и портфолио, `avatarModerationStatus`, неизвестные значения, `url` строкой),
`audit-entry.test.tsx` (строка `portfolio_photo.created`, раскрытый diff «Файл / Порядок /
Статус проверки / На проверке → Одобрено», строка аватара), `audit-screen.test.tsx`
(фильтр отправляет `entityType=portfolio_photo` и начинает первую страницу),
`response-guards.test.ts` (`portfolio_photo` проходит guard).

### E2E

```
> npx playwright test --workers=2 --reporter=list
309 passed, 6 skipped, 0 failed (315 tests, 6.3 min)
```

Сценарии среза: фильтр «Фото портфолио» в `admin-audit.spec.ts` (запрос
`entityType=portfolio_photo`, «Ничего не найдено» под датой фикстур) и API-ассерты журнала в
`barber-profile-photos-mutations.spec.ts` (upload/approve/hide/unhide/delete — точный
`photoId`, сегодняшние UTC-сутки, `url` у `created`/`deleted`, нет `rejectionReason`) и
`account-avatar-mutations.spec.ts` (`avatarUrl`/`avatarModerationStatus`, нет
`avatarRejectionReason`). Журнал читается из админского контекста: `page.request` владельца
получает `403 AdminOnly`. Seed и дата фикстур 2026-09-10 не менялись, счётчики страниц не
затронуты.

Точечные прогоны: `verification/issue-91-e2e-audit.txt` (18 passed) и
`verification/issue-91-e2e-media-mutations.txt` (15 passed); полный — 
`verification/issue-91-e2e-full.txt`. API для прогона поднят с `EmailOutbox__IntervalSeconds=1`,
`Email__*` на Mailpit и `Storage__RootPath` вне рабочей копии (README, «E2E»); без этого письма
оседают в outbox дольше 10 секунд, которые ждёт `mail.ts`.

### Скриншоты

`verification/issue-91-audit-1280.png` и `verification/issue-91-audit-320.png`: фильтр «Фото
портфолио», сегодняшние даты, раскрытый diff. Горизонтального скролла нет
(`scrollWidth == clientWidth`: 1280 и 320), длинный URL переносится.

## Что осталось

- S3/MinIO и общий volume для нескольких инстансов, серверный и CDN-кэш, политика `srcset`
  для CDN, AVIF — прежние долги #31/#32.
- `pendingAvatarUrl` (старый кадр публичен до одобрения нового), фильтр `avatar=blocked`,
  ретенция журнала — отдельные решения.
- Ретенции, партиционирования, экспорта и полнотекстового поиска по `payload` нет.
