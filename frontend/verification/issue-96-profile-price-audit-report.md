# Срез #96 — «Журнал без слепых зон»: карточка барбера и прайс в `audit_log`

Дата: 2026-11-13. Ветка: `features/issue-96` (от свежего `origin/main` после #95). PR — следующий
свободный. Этап backend + frontend в одном PR, миграций нет.

## Что сделано

**Backend.**

- `AuditTrail` — allowlist `BarberProfile` дополнен `Bio` и `LatePolicy` (текст карточки),
  добавлен девятый тип `barber_service` с полями `BarberProfileId`, `HairServiceId`, `Price`,
  `DurationMinutes`, `IsActive` и `AuditsCreation: true` (добавление услуги пишет `created`).
- Реестр фильтра: `AdminAuditEntityType.BarberService = 9`, `AdminAuditEntityTypes.BARBER_SERVICE
  = "barber_service"`, ветки `TryParse`/`ToStorageName`, doc-комментарии в `AdminAuditController`.
- Домен не менялся: `UpdateProfile`, `AddService`, `UpdateService`, `Activate`/`Deactivate` уже
  зовут `MarkUpdated`, аудит смотрит на change tracker.
- Новых ручек и миграций нет: `audit_log.entity_type` — `varchar(64)`, `payload` — `jsonb`.

**Frontend.**

- `src/lib/admin-audit.ts`: десятый тип в union и в массиве опций **в конец** (порядок прежних не
  сдвигается), подпись «Услуга барбера», подписи полей `bio`, `latePolicy`, `price`,
  `durationMinutes`, `hairServiceId`; `formatAuditValue` рендерит `price` в рублях только для
  `entityType === 'barber_service'` (`formatPrice`), нечисловое значение остаётся строкой сервера.
- `src/i18n/messages/en/admin-libs.json`: «Услуга барбера» → Barber service, «Услуга каталога» →
  Catalogue service, «Правила опоздания» → Late policy. «Цена», «О себе», «Длительность, мин» уже
  переведены — дублей нет.
- Экран и раскладка `audit-screen.tsx`/`audit-entry.tsx` не менялись, `response-guards.ts` тоже:
  `entityType` — строка, контракт чтения прежний.
- Админского редактирования карточки/цены не появилось — срез только пишет и показывает строки.

## Решение D4: `displayName` в журнал **не вошёл** (осознанно)

Черновик `next-4-…` предполагал обычное свойство `DisplayName`, и первый RED-тест
(`Capture_WhenBarberProfileDisplayNameChanged_WritesTextFromAndTo`, путь allowlist
`DisplayName.Value`, резолв через `entry.Reference("DisplayName").TargetEntry`) показал ровно ту
ловушку, ради которой он и писался: коллекция строк пуста.

Диагностика change tracker после `UpdateProfile` (замена инстанса `DisplayName`):

```
ownerState=Modified | ownerNavIsModified=True | targetNull=False
ownedState=Added | ownedIsModified=False | from=Petr | to=Petr
```

То есть EF удаляет старую owned-запись и создаёт новую в состоянии `Added`; `OriginalValue` новой
записи уже равен новому значению. Снимка старого имени нет ни в записи владельца, ни где-либо ещё
в трекере (старая запись не остаётся `Deleted`). Построить честный diff `from/to` нечем, поэтому
по правилу D4 поле в allowlist **не добавлено**. Чтобы это не было «зелёной тихой дырой»:

- `AuditTrail` (allowlist `BarberProfile`) содержит комментарий с причиной и ссылкой на тест;
- `Capture_WhenBarberProfileDisplayNameChanged_WritesNoRowBecauseOwnedSnapshotIsLost` фиксирует
  отсутствие строки как известное ограничение (упадёт, если EF когда-нибудь сохранит снимок, — это
  приглашение добавить поле, а не регресс);
- `Capture_WhenBarberProfileDisplayNameAndBioChanged_WritesOnlyBioChange` доказывает, что даже при
  одновременной правке имени и текста в diff попадает только `bio`, без `displayName` и без имени
  CLR-типа;
- факт записан в `frontend/AGENT.md` §14 #30, `backend/AGENT.md` §15/§16, README и в этом отчёте.

Доменный дизайн ради аудита не ломался: `DisplayName` остаётся неизменяемым value object, событие
изменения цены/имени в домен не добавлялось.

## Примеры payload (реальные строки из `barbersearch_e2e`)

Создание услуги (E2E-сцена «услуга появляется в прайсе»):

```json
{
  "action": "barber_service.created",
  "payload": {
    "values": {
      "price": 1500,
      "isActive": true,
      "hairServiceId": "9e500000-0000-4000-8000-000000000001",
      "barberProfileId": "9e100000-0000-4000-8000-000000000015",
      "durationMinutes": 45
    }
  }
}
```

Правка цены и длительности (одна строка, два change'а; `decimal` уходит числом):

```json
{
  "action": "barber_service.updated",
  "payload": {
    "changes": {
      "price": { "from": 1500.00, "to": 2000 },
      "durationMinutes": { "from": 45, "to": 60 }
    }
  }
}
```

Масштаб `decimal` в JSON сохраняется (`1500.00` рядом с `2000`) — это не строка, и клиент
читает оба значения как числа (`Number(value)`, `formatPrice`).

Скрытие и возврат услуги — `barber_service.updated` с `isActive`, без `deleted`:

```json
{ "changes": { "isActive": { "from": true, "to": false } } }
```

Текст карточки (имя в diff не попадает — D4):

```json
{
  "action": "barber_profile.updated",
  "payload": {
    "changes": {
      "bio": {
        "from": "E2E-профиль: карточка, услуги и специализации.",
        "to": "E2E-био 1794567264158"
      }
    }
  }
}
```

Длинный текст обрезается существующим правилом (500 символов с маркером `...`), полный текст
остаётся в `barber_profiles.bio` — это проверяют unit-тесты на 600 и 2000 символов.

## Проверка

Backend (из `D:\Projects\barbersearch\backend`):

- `dotnet build BarberSearch/BarberSearch.sln --nologo` — 0 warnings / 0 errors;
- `dotnet test tests/BarberSearch.Domain.UnitTests` — **595** зелёных;
- `dotnet test tests/BarberSearch.Application.UnitTests` — **958** зелёных;
- `dotnet test tests/BarberSearch.Infrastructure.UnitTests` — **363** зелёных;
- `dotnet test tests/BarberSearch.IntegrationTests` — **320** зелёных (включая новый
  `BarberProfileAuditCoverageTests`, 7 `[Fact]`);
- `dotnet ef migrations has-pending-model-changes` — «No changes have been made to the model since
  the last migration» (миграций не появилось).

Frontend (из `D:\Projects\barbersearch\frontend`):

- `npm run lint` — 0 warnings / 0 errors;
- `npm test` — **1529** тестов в 144 файлах, все зелёные;
- `npm run build` — сборка проходит;
- `npm run check:i18n -- --strict` — exit 0, без предупреждений;
- `npx tsc -p tsconfig.e2e.json --noEmit` — без ошибок;
- `npm run test:e2e` — полный прогон **317 passed, 6 skipped, 0 failed** (323 теста, 6.8 мин,
  `--workers=2`); мутационная сцена #96 идёт последней и зелёная. Прогон с дефолтными 12
  воркерами на этой машине дал таймауты в посторонних read-only спеках a11y/пагинации
  (Vite + API + 12 браузеров упираются в CPU) — это окружение, а не регресс; локальная
  конвенция `--workers=2` записана в README/AGENT.

> Testhost в этой оболочке требует вывода вне workspace (`-p:BaseOutputPath=%TEMP%\bs-tests\…`),
> иначе VSTest падает с `Win32Exception (5)` — см. `backend/AGENT.md` §5.2.

## E2E

Мутационная сцена в `e2e/specs/barber-profile-mutations.spec.ts` (проект `mutations-chromium`,
спека `serial`) — после сцены «новая цена видна»:

1. барбер в `/barber/profile` меняет цену «E2E Стрижка» на 2 100 ₽ через диалог («Цена обновлена.»);
2. администратор в отдельном контексте открывает `/admin/audit`, выбирает «Услуга барбера» и
   видит строку с diff «Цена: 2 000 ₽ → 2 100 ₽» (матчинг по цифрам, не по NBSP), axe — без
   `critical`/`serious` на 1280;
3. через `adminContext.request` проверяется `GET /api/admin/audit?entityType=barber_service`:
   `action = barber_service.updated`, `entityType = barber_service`, `changes.price.to = 2100`,
   автор — `profileBarber`;
4. `finally` возвращает цену 2 000 ₽ — спека `serial`, следующие сцены скрывают и возвращают ту же
   услугу.

Скриншоты diff'а: `verification/issue-96-audit-diff-1280.png`,
`verification/issue-96-audit-diff-320.png`; горизонтальной прокрутки на 320 нет
(`scrollWidth === clientWidth`). На снимке — первая сверху строка с ценой (`finally` вернул
2 000 ₽): «Цена: 2 100 ₽ → 2 000 ₽»; восходящая строка «2 000 ₽ → 2 100 ₽» лежит следующей
ниже. Оба направления — деньги в рублях, а не строки «5000».

Артефакты прогона (закоммичены через `git add -f`, как в прошлых срезах):

| Файл | Что внутри |
|---|---|
| `issue-96-backend-tests.txt` | build, четыре тестовых проекта, `has-pending-model-changes` |
| `issue-96-frontend-tests.txt` | lint, unit, build, i18n, `tsc -p tsconfig.e2e.json` |
| `issue-96-e2e-full-2.txt` | полный зелёный прогон (317 passed / 6 skipped, `--workers=2`) |
| `issue-96-e2e-full.txt` | тот же прогон с 12 воркерами: таймауты посторонних read-only спек (окружение) |
| `issue-96-e2e-mutations.txt` | `barber-profile-mutations.spec.ts` отдельно (6 passed) |
| `issue-96-e2e-audit.txt` | `admin-audit.spec.ts` отдельно (11 passed) |
| `issue-96-audit-sql.txt` | `psql`-выборка строк `barber_service` и `barber_profile` |
| `issue-96-shots.mjs`, `issue-96-shots.txt` | скрипт снимков и его лог (какая строка раскрыта) |
| `issue-96-audit-diff-1280.png`, `issue-96-audit-diff-320.png` | diff в рублях на двух ширинах |

## Что осталось вне журнала

- цена **визита** (`Booking.Price`), `SalonId` и `BarberServiceId` брони — отдельный срез правки
  цены;
- списки карточки (языки, достижения, способы оплаты, формат общения, доп. услуги),
  специализации и стаж;
- расписание, исключения, отпуска, салоны и `BarberSalon`;
- ретенция, экспорт, партиционирование и восстановление значений из журнала;
- `displayName` — по причине D4 (см. выше);
- имя услуги каталога по `hairServiceId`: журнал принципиально не резолвит имена (join ради
  подписи — отдельное решение).

## Приватность

`/privacy` не менялся: новых категорий данных в журнале нет. `bio`/`latePolicy` — публичный текст
карточки, `price`/`durationMinutes` — публичный прайс, `hairServiceId` — не PII. Ретенция та же,
что у остальных строк.
