# Issue 87 — модерация и аудит аватара (BarberSearch)

Ветка `features/issue-87`, один PR в `main`. Срез продолжает #85/#86: администратор может
**скрыть и вернуть** аватар любого пользователя, не блокируя аккаунт, увидеть состояние в
админских строках и карточках, а каждое решение оставляет строку в `audit_log`. Пользователь
видит объяснение и может загрузить другое фото. Срез сознательно трогает оба каталога
(`frontend/` и `backend/`), как #83/#84/#85/#86.

Дата прогонов: 2026-11-11.

## Что сделано

### Backend

- **Домен.** `User.IsAvatarBlocked` (`bool`, по умолчанию `false`) — состояние аккаунта, а не
  отдельной строки файла. `BlockAvatar()` без `AvatarUrl` — `409 user.blockAvatar.noAvatar`;
  повторная блокировка идемпотентна и не меняет `UpdatedAt`. `UnblockAvatar()` идемпотентен.
  `SetAvatar` и `UpdateProfile` снимают флаг **только при реальной смене URL** (сравнение
  `Ordinal` до присваивания; переход в `null` — тоже смена): повторная запись того же URL,
  включая сохранение имени из кабинета, флаг сохраняет. `IsActive`, `Role` и `SecurityStamp` не
  меняются. XML-доки объясняют, почему флаг живёт на пользователе, почему смена URL его снимает
  и почему повторная запись того же URL — нет.
- **Схема и миграция.** `UserConfiguration`: `users.is_avatar_blocked`, `IsRequired()`,
  `HasDefaultValue(false)`. Миграция `20261111140731_AddUserAvatarBlocked` (34-я): одна колонка
  `boolean NOT NULL DEFAULT false`, без backfill и без индексов; `Down` удаляет колонку.
  `dotnet ef migrations list` — 34, `has-pending-model-changes` — «No changes…».
- **Контракты чтений.** `MyProfileResponse` — последним параметром `bool IsAvatarBlocked`, URL
  остаётся сырым. `AdminUserResponse` и `AdminBarberResponse` — последними `string? AvatarUrl` и
  `bool IsAvatarBlocked`; `AdminUserDetailResponse` и `AdminBarberDetailResponse` — последним
  `bool IsAvatarBlocked`. Публичные `BarberSearchItemResponse`, `BarberProfileResponse` и
  `BarberBookingCustomerResponse` форму не меняли, только XML-доки: `null`, если фото не загружено
  **или скрыто администратором**.
- **Чтения и маска.** `BarberProfileRepository.SearchAsync` проецирует
  `IsAvatarBlocked ? null : AvatarUrl` в SQL (`BuildSearchAvatarsQuery`); заблокированный барбер
  остаётся в выдаче, число запросов и план keyset-запроса не меняются, пустая страница
  по-прежнему не делает дополнительный запрос. Публичная и собственная карточки используют один
  `BuildCardAsync`/`BuildCardAvatarQuery` с `CASE` по PK. `BookingRepository.BuildBarberBookingsQuery`
  маскирует аватар клиента; админские брони (`BuildAdminBookingsQuery`) остаются сырыми.
  `BuildAdminUsersQuery`, `BuildAdminBarbersQuery` (из уже существующего `join users`) и
  `BuildAdminBarberDetailQuery` читают URL и флаг; второго join'а нет.
- **Раздача.** Новый публичный `Application/Users/UserAvatarAccess.cs` (`AvatarUrl`,
  `IsAvatarBlocked`); `IUserRepository.GetAvatarUrlAsync` заменён на `GetAvatarAccessAsync`,
  `UserAvatarUrlLookup` удалён, `BuildAvatarUrlQuery` проецирует два столбца по PK.
  `AvatarMediaController`: прежняя проверка формата и точного совпадения URL, затем флаг —
  администратор получает файл с `Cache-Control: private, no-store`, все остальные (включая
  владельца) — тот же `404 avatarMedia.notFound`; видимый кадр сохраняет `public, max-age=86400`,
  `nosniff` и range. `[AllowAnonymous]` и reads-лимит не менялись.
- **Админские мутации и аудит.** `Application/Users/Admin/BlockUserAvatar/` и
  `UnblockUserAvatar/`: команда `(Guid UserId)`, `EnsureAdmin()` до чтения, `GetByIdAsync`,
  доменный метод, `UpdateAsync`, ошибки репозитория как есть. `AdminUsersController`:
  `POST`/`DELETE api/admin/users/{userId:guid}/avatar/block`, политика `AdminOnly` от класса,
  успех `204`; глобальный antiforgery. Логи — только `UserId` и состояние, без email/URL/пути.
  `AuditTrail`: в allowlist `User` добавлен ровно `nameof(User.IsAvatarBlocked)`; действие
  `user.updated`, payload `changes.isAvatarBlocked`; строка пишется тем же `SaveChanges`.
  `AvatarUrl` в allowlist не добавлен.
- **Тесты.** Домен — 9 кейсов в `Domain.UnitTests/UserTests.cs` (конфликт без URL, идемпотентность,
  снятие флага при новом URL/`null` и сохранение при том же, `UpdateProfile` аналогично).
  Application — новый `UserAvatarModerationTests` (10 кейсов: 403/401/404/409, успех, повтор,
  один `UpdateAsync`), плюс чтения админских строк/карточек в `AdminUserReadTests` и
  `AdminBarberReadTests`. Infrastructure — новый `AvatarMaskQueryTests` (`ToQueryString` для
  поиска и карточки) и обновлённый `UserAvatarUrlQueryTests` (два столбца, PK, без join).
  Integration — матрица «аноним/владелец/админ» для media, `users/me` с сырым URL и флагом,
  поиск/карточка/записи барбера с `null`, админские список/карточка с URL и флагом, строка
  аудита на блокировку и разблокировку, новая загрузка снимает флаг, сохранение имени — нет.

### Frontend

- **Типы и guard'ы.** `MyProfile.isAvatarBlocked`; `AdminUserRow`/`AdminBarber` — `avatarUrl` и
  флаг; `AdminUserDetail`/`AdminBarberDetail` — флаг. Все пять guard'ов (`isMyProfile`,
  `isAdminUserRow`, `isAdminUser`, `isAdminBarberRow`, `isAdminBarber`) требуют булев флаг;
  фикстуры обновлены.
- **Транспорт и ошибки.** `blockUserAvatar`/`unblockUserAvatar` в `admin-users.ts` через
  `mutateWithCsrf`; `admin-barbers.ts` их реэкспортирует, второго запроса нет. Общий тип
  `AvatarModerationAction` и `describeAvatarModerationError` (403/401/404/`user.blockAvatar.noAvatar`/
  CSRF/429 + русский фолбэк) живут в `src/lib/avatar-moderation.ts`; обе карточки используют их
  через свои `describe…ActionError`.
- **Админские списки.** `UserIdentity` и `BarberIdentity` показывают 24 px `AvatarImage` при
  наличии URL, инициалы остаются fallback'ом, при флаге — текстовый бейдж «Фото скрыто»; строка
  выдерживает 320 px.
- **Админские карточки.** Общий блок `src/components/admin/avatar-moderation.tsx` (аватар, бейдж,
  «Скрыть фото»/«Вернуть фото» и `alertdialog` «Скрыть фото?» с последствием) вставлен в
  `user-detail.tsx` и `barber-detail.tsx`; карточка барбера вызывает мутацию с `barber.userId`.
  «Скрыть» подтверждается диалогом, «Вернуть» — без диалога; нет URL — подсказка «У пользователя
  нет фото — скрывать нечего.» и кнопки нет. Обе карточки обновляют строку/карточку локально,
  затем тихо перечитывают, объявление предлагает обратное действие; ошибка — `role="alert"`.
  Обе кнопки — 44 px на мобильном (`h-11 sm:h-9`, кнопки диалога `h-11 sm:h-8`), `disabled` на
  время запроса.
- **Кабинет и шапка.** `HeaderAuth`, `AccountIdentity` и `AvatarCard` рендерят изображение только
  при `avatarUrl && !isAvatarBlocked`; карточка показывает «Фото скрыто администратором»,
  объяснение и «Загрузить другое фото», удаление остаётся. `ProfileProvider.setAvatar` выставляет
  флаг `false` (новая загрузка/удаление), `save` отправляет сырой URL и сохраняет флаг.
- **Аудит.** `ADMIN_AUDIT_FIELD_LABELS.isAvatarBlocked` — «Фото скрыто администратором»; boolean
  рендерится как «Да»/«Нет».
- **i18n.** Новые строки — только `t()`, английские каталоги `account.json`, `admin-libs.json`,
  `admin-queues.json`; гейт `--strict` — 0 пробелов.

### E2E

- `account-avatar-mutations.spec.ts` (serial, `mutations`): после сцены с админской карточкой
  админ в отдельном контексте скрывает фото; кабинет и шапка показывают инициалы и объяснение,
  `GET /api/users/me` — сырой `avatarUrl` + `isAvatarBlocked: true`, media-URL анонимом и
  владельцем — `404`, админом — `200` + `private, no-store`; возврат возвращает кадр до
  следующей (320 px) сцены. Axe сканирует подтверждающий `alertdialog` и скрытое состояние
  карточки; строка `/admin/users` на 320 px с бейджем не даёт горизонтального скролла.
- `public-barber-avatar-mutations.spec.ts` (`profileBarber`): админ скрывает фото владельца из
  `/admin/barbers/{id}`; поиск по `E2E поиск` и публичная карточка показывают инициалы, оба
  контракта (`/api/barbers?additionalService=…`, `/api/barbers/{id}`) — `avatarUrl: null`;
  строка `/admin/barbers` на 320 px с бейджем не переполняется; возврат возвращает и `src`,
  и контракт.
- `afterEach` при падении снимает блокировку админским контекстом (`browser.newContext` со
  `storageState('admin')` и `DELETE .../avatar/block`), клиента `mutations` для этого
  недостаточно. `e2e/fixtures/seed.sql` сбрасывает `is_avatar_blocked = false` во всех шести
  `ON CONFLICT … DO UPDATE` для `users`.
- `a11y.spec.ts` — явная сцена `/admin/users/{id}` (карточка без фото, подсказка «скрывать
  нечего»); `a11y-narrow.spec.ts` — проверка `scrollWidth` на `/admin/users` и новая сцена
  `/admin/barbers` на 320. Витринный барбер `…0001` не тронут; `page.route` для успешных тел не
  используется.

### Документация

`frontend/AGENT.md` (§5, §9.3, §12.1, §13, §14 #32, §15, §16), `frontend/README.md` («Avatar»,
«Admin users», «Admin barbers»), `backend/AGENT.md` (§4, §7.3, §8, §15.2, §16), этот отчёт.
§17.4 `backend/AGENT.md` не менялся: auth в срезе не трогали.

## Прогоны

| Команда | Результат |
|---|---|
| `dotnet build backend\BarberSearch\BarberSearch.sln` | успех, 0 предупреждений, 0 ошибок |
| `dotnet test BarberSearch.Domain.UnitTests` | 474 / 474 |
| `dotnet test BarberSearch.Application.UnitTests` | 761 / 761 |
| `dotnet test BarberSearch.Infrastructure.UnitTests` | 235 / 235 |
| `dotnet test BarberSearch.IntegrationTests` (`BARBERSEARCH_TEST_POSTGRES`, отдельные БД `barbersearch_it_<guid>`) | 248 / 248 |
| Все backend-тесты | **1718, 0 упало** |
| `dotnet ef migrations list` | 34 миграции, последняя `AddUserAvatarBlocked` |
| `dotnet ef migrations has-pending-model-changes` | «No changes have been made to the model since the last migration.» |
| `npm run lint` | 0 предупреждений / 0 ошибок, 488 файлов, 116 правил |
| `npm test` | **1230 тестов / 121 файл**, 0 упало (базовая линия 1202) |
| `npm run build` | успех, `tsc -b` + vite, **2580 модулей**, 7.23 с; `index-*.js` 352.18 / 114.15 kB, `admin-page-*.js` 271.14 / 56.88 kB, `router-*.js` 241.30 / 64.18 kB, `csrf-*.js` 94.04 / 31.31 kB, `account-profile-page-*.js` 12.93 / 4.33 kB, `index-*.css` 89.66 / 15.35 kB |
| `npm run check:i18n -- --strict` | t() 1676, переводов 1980, **нет перевода 0**, конфликтов 0, видимых русских строк в JSX 0, exit 0 |
| `npm run test:e2e -- --workers=2` | **293 прошло, 4 пропущено, 0 упало** |
| Точечный прогон avatar-сцен | 236 прошло, 4 пропущено, 0 упало |
| `a11y.spec.ts` + `a11y-narrow.spec.ts` (точечно) | 62 прошло, 0 упало |
| `npx tsc -p tsconfig.e2e.json --noEmit` | OK |

Четыре известных пропуска не изменились: mobile split-view `admin-reviews` ×2 и `admin-users`
(«выбранный отзыв/пользователь остаётся в URL…», «смена вкладки не открывает отзыв…») и
desktop-only `password-reset` на mobile («письмо со ссылкой → новый пароль…», лимитер auth; 320 px
покрыт axe-сканом). Новых skip и axe-исключений срез не добавил: `critical`/`serious` — 0.

Integration-прогон шёл на локальной PostgreSQL через `BARBERSEARCH_TEST_POSTGRES` (не Docker),
на уникальных `barbersearch_it_<guid>`; dev-база не трогалась.

## Отклонения от промпта

1. **`BuildCardAsync` общий у публичной и собственной карточки.** Требование C.1 маскировать
   публичную карточку выполнено в общей проекции, поэтому `GetOwnCardByIdAsync` тоже отдаёт
   `avatarUrl: null` у скрытого фото. Сырой URL остаётся только в `users/me` и админских
   поверхностях — это согласуется с D1/D3 и не даёт владельцу обойти модерацию; отдельной
   «своей» карточки с сырым URL не заводили.
2. **Доменные тесты аватара лежат в `Domain.UnitTests/UserTests.cs`.** Отдельного
   `Domain.UnitTests/UserAvatarTests.cs` нет; одноимённый файл `UserAvatarTests.cs` —
   интеграционный. Доменные кейсы добавлены в существующий файл `UserTests`, чтобы не плодить
   второй файл на ту же сущность.
3. **Новые `internal` билдеры.** `BarberProfileRepository.BuildSearchAvatarsQuery` и
   `BuildCardAvatarQuery` вынесены в `internal static`, чтобы `AvatarMaskQueryTests` проверил
   трансляцию `CASE` через `ToQueryString()` без БД. Число запросов и join'ов не изменилось.
4. **Frontend: общий тип и helper.** Вместо двух независимых `describe…ActionError` для аватара
   заведены `AvatarModerationAction` и `describeAvatarModerationError`, которые покрывают всю
   поверхность ошибок (403/401/404/`noAvatar`/CSRF/429); обе карточки делегируют в него.
   Бейдж «Фото скрыто» отрисован в строке действий блока аватара, а не поверх изображения:
   поверх наложения текста на фото не было в макете, а текст важнее для axe и 320 px.
5. **Axe скрытого состояния и диалога — в mutation-спеках.** Сидовые фикстуры не имеют аватаров,
   а read-only `a11y.spec.ts`/`a11y-narrow.spec.ts` не должны мутировать данные. Поэтому
   `alertdialog` и скрытое состояние карточки сканируются в `account-avatar-mutations.spec.ts`;
   read-only гейт получил сцену `/admin/users/{id}` без фото (1280) и `/admin/barbers` (320), а
   проверка переполнения `/admin/users` на 320 добавлена в существующую сцену.
6. **`seed.sql` сбрасывает флаг во всех шести `ON CONFLICT` для `users`.** Промпт говорил про
   «все `ON CONFLICT`», фактически их шесть (включая генерируемые наборы клиентов/барберов);
   сброшены все, иначе упавшая сцена оставляла бы фикстуру скрытой.
7. **Позиционные record'ы.** Новые поля добавлены последними параметрами (в `AdminBarberDetail`
   — после `Photos`), существующие параметры не сдвигались; JSON-порядок контрактом не является.
8. **`UserAvatarAccess` и удаление lookup'а.** Публичный record заменил `internal`
   `UserAvatarUrlLookup`; обновлены интерфейс, PostgreSQL-репозиторий, фейк, контроллер и
   `UserAvatarUrlQueryTests` — четыре места, как и предупреждал промпт.
9. **Окружение E2E.** База `barbersearch_e2e` была на 33 миграциях; 34-ю пришлось применить
   вручную через `dotnet ef database update` перед `test:e2e:seed`. Это состояние окружения, а
   не изменение кода; в репозиторий ничего не добавлено.
10. **Кнопки диалога.** После прогонов агентом добавлен `className="h-11 sm:h-8"` двум кнопкам
   футера `alertdialog` (44 px на мобильном); финальные lint/test/build/i18n пересняты на этом
   дереве, числа выше — уже с этой правкой.

## Ограничения

- **Постмодерация.** Публикация остаётся постмодерацией, очереди «на проверке» нет; блокировка
  скрывает текущий кадр, а новая загрузка — новый файл, который публикуется сразу и снимает
  флаг. Это осознанный обход, а не дыра: администратор реагирует на уже опубликованное фото.
- **24-часовой кэш.** `Cache-Control: public, max-age=86400` у видимого кадра не менялся; после
  скрытия/возврата старая картинка может ещё сутки жить в чужом браузере. UI убирает `<img>` из
  DOM по `isAvatarBlocked`, поэтому «битой» картинки нет, но прямой запрос из кэша возможен.
- **Открытые вкладки.** Блокировка — админское действие без push: вкладка владельца узнаёт о ней
  только при следующей загрузке профиля/страницы. Polling не добавляли.
- **Владельцу — `404`.** Скрытый файл не отдаётся и владельцу: его UI показывает инициалы и
  объяснение, а отдельный маршрут «владелец видит своё скрытое фото» противоречил бы смыслу
  модерации.
- **Нет счётчика, фильтра и очереди.** В `/admin/summary` нет числа скрытых аватаров, в списках —
  фильтра `avatar=blocked`; историю видно только в журнале. Это отдельный срез о находимости.
- **Нет миниатюр, `srcset`, WebP/AVIF и серверного кропа.** Клиент по-прежнему готовит 512×512.
- **Один диск.** `avatars/` лежит на том же локальном диске и в том же бэкапе, что `portfolio/`;
  S3/MinIO и общий volume для нескольких инстансов не появлялись.
- **Идемпотентный вызов всё равно сохраняет агрегат.** `UpdateAsync`/`SaveChanges` вызываются и
  на повторе, но домен не помечает сущность изменённой, поэтому `audit_log` второй строки не
  получает и `UpdatedAt` не меняется.

## Что осталось

- Премодерация и очередь «на проверке» (сейчас — постмодерация).
- Счётчик скрытых аватаров в `/admin/summary` и фильтр `avatar=blocked` в списках.
- Серверные миниатюры, `srcset`, WebP/AVIF, серверный кроп.
- S3/MinIO, общий volume и несколько инстансов.
- Аватар в остальных поверхностях: claims/`SessionUser`/`/auth/me`, отзывы, клиентская база
  барбера, уведомления, отдельное поле в журнале аудита (сейчас — только `isAvatarBlocked`).
- Тёмная тема, правки `src/components/ui/**`, новые пакеты и индексы — вне среза.

## Найдено попутно

- Первый запуск E2E после среза упёрся в отставшую схему `barbersearch_e2e` (33 миграции):
  `seed.sql` уже пишет `is_avatar_blocked`, поэтому без `dotnet ef database update` сид падал.
  Код не менялся — только локальная база; в README/AGENT это отражено как состояние окружения.
- Рост `admin-page` (+5.20 kB raw / +1.89 kB gzip) — это общий блок модерации, бейджи и действия
  в двух карточках и двух списках, а не новый экран; `router` (+1.56 kB raw) вырос из-за новых
  английских строк, а не из-за кода. Новых зависимостей нет.
- Read-only axe-гейт не может открыть скрытое состояние: сидовые аккаунты без аватаров, а
  мутировать фикстуры в гейте нельзя. Поэтому бейдж и диалог проверяются в mutation-спеках, а
  гейт покрывает статичную карточку без фото — это сознательное разделение, а не пропуск.
- `frontend/USER_AVATAR_PROMPT.md` лежит в рабочем дереве как **неотслеживаемый входной файл**;
  он не часть этого среза и **не должен попадать в коммит**. Актуальный промпт среза —
  `frontend/verification/issue-87-avatar-moderation-prompt.md`.
- `GetOwnCardByIdAsync` использует ту же проекцию, что публичная карточка, поэтому скрытое фото
  отсутствует и в собственной карточке барбера. Это зафиксировано как отклонение (D1/D3), чтобы
  следующая сессия не «чинила» это, добавив владельцу сырой URL в обход модерации.
- Расхождение имён: файл `UserAvatarTests.cs` в репозитории — интеграционный, а доменные тесты
  аватара живут в `UserTests.cs`; при поиске по имени легко открыть не тот файл.
- Каталог `frontend/verification/` указан в `.gitignore` (рабочие материалы прогонов), поэтому
  этот отчёт, как и предыдущие, попадёт в коммит только через `git add -f`; обычный `git add`
  его молча пропустит.
