# Issue 86 — Аватар в шапке, поиске и списках броней

Ветка `features/issue-86`, один PR в `main`. Срез продолжает #85: файл аватара уже
загружался, хранился и раздавался, но был виден только в кабинете и на публичной
карточке барбера. Теперь тот же `avatarUrl` приходит из трёх чтений, а `ProfileProvider`
стал единственным кэшем профиля: шапка и кабинет читают одно состояние, поэтому загрузка
или удаление фото сразу меняют обе поверхности.

Дата прогона: 2026-11-11.

## Что сделано

### Backend

- **Контракты.** `BarberSearchItemResponse` получил `string? AvatarUrl` последним
  параметром записи, `BarberBookingCustomerResponse` — `string? AvatarUrl`,
  `AdminBookingCustomerResponse` и `AdminBookingBarberResponse` — тоже. XML-доки
  по-русски: «аватар владельца профиля / клиента / барбера; `null`, если не загружен»
  (у клиента — ещё и «`null` у гостя»). Порядок остальных полей не менялся.
- **Поиск.** `BarberProfileRepository.SearchAsync` проецирует внутреннюю
  `BarberSearchRow` с `UserId` (в контракт он не попадает), после основного
  keyset-запроса делает **один** `Users.AsNoTracking()`-запрос по урезанному набору
  `userIds`, собирает `Dictionary<Guid, string?>` и применяет аватар вместе с полями
  салона одним `row with` и одним `ToList`. При `rows.Count == 0` возвращается пустой
  список — ни салонов, ни аватаров не запрашивается. `ApplySearchFilters`,
  `OrderBy/ThenBy/Take` и комментарий про индекс не менялись: `JOIN users` в keyset-выдачу
  не добавлялся.
- **Записи барбера.** В `GetBarberPageAsync` проекция `customers` читает
  `user.AvatarUrl`; `CustomerAvatarUrl` протянут через `joined` и `BarberBookingRow` в
  `ToBarberResponse`. Клиентский `LEFT JOIN` и оба скоупа не трогались, поэтому
  гостевая строка остаётся в списке с `avatarUrl: null`. Композиция SQL вынесена в
  `internal static BuildBarberBookingsQuery` (см. отклонение 2); поведение запроса то же.
- **Админские брони.** `BuildAdminBookingsQuery` берёт аватар клиента из уже
  существующего `LEFT JOIN` (`customer.AvatarUrl`), а аватар барбера — из `users` по
  `barber_profiles.user_id`: в проекцию профиля добавлен `UserId`, сделан **второй**
  `LEFT JOIN` на `Users.AsNoTracking()`. Оба поля протянуты в `AdminBookingRow` и
  `ToAdminResponse`; список `GET /api/admin/bookings` и деталь `/{id}` собираются одним
  запросом и отдают одинаковые аватары.
- **Миграции.** Новых нет: `dotnet ef migrations list` — 33, `has-pending-model-changes`
  — no changes. Все новые lookup'ы идут по первичному ключу, новых индексов и пакетов нет.
- **Тесты.** `Infrastructure.UnitTests/BarberBookingQueryTests.cs` и
  `AdminBookingQueryTests.cs` проверяют через `ToQueryString()` колонки `avatar_url`
  (клиент и владелец профиля), сохранение прежних условий и порядка. В
  `Application.UnitTests` обновлены вызовы `BarberSearchItemResponse` и фейк
  `FakeBookingRepository`. Новый `IntegrationTests/AvatarReadSurfacesTests.cs` (4 теста,
  реальная PostgreSQL) проверяет поиск до/после загрузки и после удаления, записи барбера
  с зарегистрированным клиентом и гостем, оба аватара в админском списке и детали, а также
  регресс `PUT users/{id}/profile` и сохранения карточки барбера.

### Frontend

- **Один провайдер.** Новый `src/components/profile-provider.tsx` смонтирован в
  `src/main.tsx` внутри `SessionProvider` и `I18nProvider`. Состояние —
  `status: 'idle' | 'loading' | 'ready' | 'error'`, `profile`, `error`,
  `requiresSignIn`; запрос только при `session.status === 'authenticated'`, один
  `GET users/me` на `user.id`, single-flight для StrictMode/повторного рендера, поколение
  отсекает поздний ответ, смена `user.id` перезапрашивает, выход/`unavailable`
  сбрасывают состояние без запроса. `401` даёт `profile: null`, `error: null`,
  `requiresSignIn: true` и не разлогинивает; прочие ошибки — `describeApiFailure`.
  Наружу `{ status, profile, error, requiresSignIn, reload, save, setAvatar }`.
- **`useMyProfile`.** Модульный кэш убран, хук стал чтением контекста
  `ProfileContext`; аргумент `userId` больше не нужен. Вне провайдера — понятная ошибка.
  `AccountProfileContext` и `AccountShellBody` сохранили форму, поправлен один вызов в
  `account-shell.tsx`.
- **Шапка.** `HeaderAuth` берёт профиль из `useMyProfile`: при `profile.avatarUrl` —
  `AvatarImage` в существующей `Avatar size="sm"`, иначе инициалы
  (`profileInitials(profile)`), а до загрузки профиля — первая буква email. Кнопка
  «Меню аккаунта» осталась одна, `aria-label`/`title` не менялись, изображение
  декоративно (`aria-hidden`, `alt=""`). Инициалы рисуются CSS-псевдоэлементом из
  `data-initials` (см. отклонение 1). После `setAvatar` в кабинете шапка меняется без
  перезагрузки — главный приёмочный сценарий среза.
- **Поиск.** `BarberSearchItem.avatarUrl: string | null`, `isBarberSearchItem` проверяет
  `isNullableString(value.avatarUrl)`; `BarberCard` показывает `AvatarImage` при
  наличии URL, инициалы остаются fallback'ом, новых подписей и кнопок нет.
- **Кабинет клиента.** `BookingView.barberAvatarUrl` берётся из уже загруженной
  карточки барбера (`profile?.avatarUrl`) — `BookingResponse` не менялся. Маленький
  аватар добавлен в `BookingRow`, большой — в `NextBookingCard` (там уже была
  `Avatar size="lg"`).
- **Кабинет барбера.** `BarberBookingCustomer.avatarUrl: string | null`;
  `CabinetBookingRow` (одна строка на все три вкладки) показывает аватар перед именем в
  одной flex-строке с телефоном: на 320 px телефон переносится, `tel:`-ссылка и 44 px
  не ломаются, у гостя — инициалы из `displayName`.
- **Админка.** `AdminBookingCustomer.avatarUrl` и `AdminBookingBarber.avatarUrl` в
  DTO и strict-guard'ах; аватары клиента и барбера добавлены в строку таблицы, карточный
  список и карточку детали. Минимальная ширина таблицы поднята 544 → 596 px, колонки
  клиента/барбера расширены (см. отклонение 5).
- **Строгие guard'ы и фикстуры.** Обновлены `isBarberSearchItem`, `isBarberBooking`,
  `isAdminBookingCustomer`, `isAdminBookingBarber`; все фикстуры этих чтений получили
  `avatarUrl`, иначе strict-проверки валят десятки тестов сразу.
- **Вес.** `ProfileProvider` и `useMyProfile` попадают в entry-чанк (нужны шапке на
  любом маршруте): `index` 349.12 / 113.26 → 352.14 / 114.12 kB (+3.02 / +0.86).
  `account-shell` уменьшился 6.63 / 2.71 → 4.68 / 1.94 kB — загрузчик профиля переехал
  из кабинетного чанка в entry, кода не убавилось. Сборка — 2578 модулей, CSS
  89.66 / 15.35 kB; новых зависимостей и правок `src/components/ui/**` нет.

### E2E

- `account-avatar-mutations.spec.ts` (`mutations`, serial): до загрузки шапка показывает
  инициалы (`data-initials`, изображения нет), после загрузки — тот же `src`, что в
  карточке и `AccountIdentity`; reload сохраняет кадр; после удаления через
  `alertdialog` шапка и каркас сразу возвращаются к инициалам, старый media URL отдаёт
  `404`. Локаторы шапки — inline в спеке.
- `public-barber-avatar-mutations.spec.ts` (`profileBarber`): после загрузки открывается
  `/`, в поле «Дополнительная услуга» вводится маркер `E2E поиск`, «Найти» даёт ровно
  одну карточку с `img` `/api/media/avatars/…`, `naturalWidth > 0` и `alt=""`; контракт
  `GET /api/barbers?additionalService=…` отдаёт тот же URL. После удаления карточка
  снова показывает инициалы, контракт — `avatarUrl: null`.
- `e2e/fixtures/seed.sql` у профиля `…0015` получил
  `additional_services = ARRAY['E2E поиск']` в `INSERT` и в `ON CONFLICT DO UPDATE`:
  маркер изолирует карточку от остальной выдачи. Салон, город и расписание не менялись
  (`search-login.spec.ts` по-прежнему ждёт 12 + 2 карточки), витринный `…0001` не
  трогался. `page.route` для успешных тел не использовался.

### Документация

`frontend/AGENT.md` (§5 snapshot, §9.3 — три чтения, §12.1 — новые числа, §13 — поверхности
и единственный кэш, §14 — переписан #32, §15 — новые тесты, §16 — completed #86),
`frontend/README.md` (где виден аватар и чем платит `ProfileProvider`), этот отчёт.
`backend/AGENT.md` (§7.3/§16, §15.2) обновлён в этом же срезе — поля контрактов и цена
lookup'ов; здесь он не дублируется.

## Прогоны

| Команда | Результат |
|---|---|
| `dotnet build backend\BarberSearch\BarberSearch.sln` | успешно, 0 warnings, 0 errors (после остановки старого API-процесса, державшего `bin`; до этого были только MSB3027 copy errors) |
| `dotnet test tests/BarberSearch.Domain.UnitTests` | 465 passed, 0 failed |
| `dotnet test tests/BarberSearch.Application.UnitTests` | 747 passed, 0 failed |
| `dotnet test tests/BarberSearch.Infrastructure.UnitTests` | 229 passed, 0 failed |
| `dotnet test tests/BarberSearch.IntegrationTests` (PostgreSQL, Testcontainers `postgres:18`) | 242 passed, 0 failed |
| `dotnet ef migrations list` / `has-pending-model-changes` | 33 миграции; no changes |
| `npm run lint` | 0 warnings, 0 errors (484 файла, 116 правил) |
| `npm test` | 1202 tests / 119 files, все зелёные (свежий прогон после финальной правки шапки, 71 с) |
| `npm run build` | `tsc -b` + `vite build` OK, 2578 modules; entry `dist/assets/index-*.js` 352.14 kB (gzip 114.12 kB), CSS 89.66 kB (gzip 15.35 kB) |
| `npm run check:i18n -- --strict` | t() 1664, переводов 1968, нет перевода 0, конфликтов 0, видимых русских строк в JSX 0 |
| `npm run test:e2e -- --workers=2` | 287 passed, 4 skipped, 0 failed, exit 0; известные skip — три mobile split-view проверки и desktop-only password-reset на mobile |

Backend-тесты запускались с `-p:BaseOutputPath` во временном каталоге: bare `dotnet test`
в этом harness падает на `testhost` (`Win32Exception (5): Access denied`), ограничение
описано в §5.2 `backend/AGENT.md`. Интеграционные тесты шли на отдельной БД
`barbersearch_it_*`, dev-база не трогалась.

## Отклонения от промпта

1. **Инициалы в шапке — CSS `::before`, а не текстовый узел.** Axe-правило
   `label-content-name-mismatch` (serious) валило все авторизованные страницы: видимый
   двухбуквенный текст «МП» не входит в доступное имя кнопки «Меню аккаунта». Поэтому
   `AvatarFallback` получает `data-initials`, а буквы рисует
   `before:content-[attr(data-initials)]`; `aria-label`/`title` не менялись,
   axe-исключений не добавляли, визуально инициалы на месте. Тесты (и E2E) проверяют
   `data-initials`, а не текстовый узел.
2. **`BuildBarberBookingsQuery` вынесен в `internal static`.** Это дало unit-тесту
   проверить колонку `avatar_url` через `ToQueryString()` без базы: в
   `Infrastructure.UnitTests` нет провайдера БД, а добавлять пакет запрещено. SQL,
   join'ы, оба скоупа и порядок не менялись.
3. **Аватар барбера в админских бронях — второй `LEFT JOIN`.** Промпт не фиксировал
   inner/outer; выбран левый join, чтобы строка брони не исчезала, если владелец профиля
   когда-нибудь будет отсутствовать. Ключ — `barber_profiles.user_id`, а не клиентский
   `bookings.user_id`.
4. **Интеграционный поиск — на изолированном барбере, созданном тестом.** E2E-фикстура
   `…0015` в integration-БД не сеется, поэтому тест создаёт профиль с уникальным городом
   и получает ровно одну карточку. Витринный `…0001` не трогали.
5. **Админская таблица расширена.** Аватар 24 px с отступом сжимал имена в прежних
   колонках, поэтому минимальная ширина таблицы поднята 544 → 596 px, а колонки
   клиента/барбера — с `w-23`/`w-19` до `w-30`/`w-25`. Ниже порога по-прежнему
   показывается карточный список, горизонтального скролла нет.
6. **`401` в провайдере сохраняет `error: null`.** В шапке ошибки нет: выставляется
   только `requiresSignIn`, состоянием сессии владеет `SessionProvider`. Текст остальных
   ошибок — из `describeApiFailure`; шапка молча остаётся на инициалах.
7. **Тестовые обвязки монтируют `ProfileProvider`.** `account-shell.test.tsx`,
   `account-page.test.tsx` и `account-security-page.test.tsx` получили провайдер, иначе
   `useMyProfile` бросает ошибку вне контекста. `account-page.test.tsx` получил только
   обёртку; avatar-проверки вынесены в новый `next-booking-card.test.tsx`, чтобы не
   раздувать тест страницы.
8. **Чужие фикстуры получили `avatarUrl: null`.** `barber-booking-action-dialog.test.tsx`,
   `use-barber-bookings.test.tsx`, `use-barber-day.test.tsx` и `lib/barber-day.test.ts`
   не относятся к аватару, но их объекты проходят через строгий `isBarberBooking`; без
   нового поля они падали. Правка — одна строка в каждой фикстуре.
9. **E2E-локаторы шапки оставлены inline.** `page.route` не использовался, page objects
   не менялись: два локатора (`avatar-image` и `avatar-fallback` внутри кнопки меню)
   живут в самой спеке, потому что шапка проверяется только в двух сценариях и
   отдельный page object ради этого не нужен.

## Долги и что осталось

- **Модерация и аудит аватара.** Файл публикуется сразу, очереди «на проверке» и
  счётчика в `/admin/summary` нет, скрыть аватар без блокировки аккаунта нельзя;
  действия с аватаром не пишутся в `audit_log` (allowlist `User` не расширялся).
- **Серверные миниатюры и `srcset`.** Нет WebP/AVIF и серверного кропа; браузер готовит
  512×512, а `Cache-Control: public, max-age=86400` позволяет старому кадру ещё сутки
  жить в чужом браузере после замены или удаления.
- **Админские списки.** Аватара по-прежнему нет в строках `AdminUserResponse` и
  `AdminBarberResponse` — там свои контракты и счётчики.
- **Хранилище.** Один инстанс API и один диск: `avatars/` лежит рядом с `portfolio/` и
  входит в тот же бэкап; S3/MinIO и общий volume — отдельный срез.
- **Другие поверхности.** Аватара нет в `BarberClientResponse` (клиентская база барбера),
  в отзывах, уведомлениях и журнале аудита; в claims/`SessionUser`/`/auth/me` его
  сознательно не добавляли.

## Найдено попутно, вне среза

- **Строгие guard'ы действительно валят пачкой.** Забытое поле в одной фикстуре
  (`barber-booking-action-dialog`, `use-barber-bookings`, `use-barber-day`,
  `lib/barber-day`) роняло весь набор соответствующих тестов, а не один — обновлять
  фикстуры нужно вместе с типом.
- **Axe и видимое имя кнопки.** Двухбуквенные инициалы внутри `button` с
  `aria-label="Меню аккаунта"` дают `label-content-name-mismatch`; это не лечится
  `aria-hidden` на тексте: axe считает видимый текст по DOM и для этого правила
  `aria-hidden` не учитывает, а прятать инициалы визуально нельзя; поэтому буквы
  рисуются псевдоэлементом. Правило стоит держать в голове для любой иконки/инициалов
  внутри кнопки с собственным именем.
- **Layout админской таблицы.** 24-пиксельный аватар с отступом требует ~30 px на
  колонку; без расширения таблицы имена начинали ломаться по буквам. Порог 596 px и
  карточный список ниже него — следствие, а не отдельная правка дизайна.
- **Harness и `dotnet test`.** Bare `dotnet test` в этом окружении не стартует
  `testhost` (`Win32Exception (5): Access denied`); рабочий запуск — с
  `-p:BaseOutputPath` во временном каталоге (§5.2 `backend/AGENT.md`). Это ограничение
  среды, а не проекта.
- **Вес провайдера.** `ProfileProvider` в entry стоит +3.02 kB raw / +0.86 kB gzip, но
  `account-shell` одновременно уменьшился на 1.95 / 0.77 kB: профильный загрузчик
  переехал из кабинетного чанка в общий. Это переезд кода, а не удаление.
