# Issue 85 — Аватар пользователя: загрузка, кабинет и публичная карточка барбера

Ветка `features/issue-85` от свежего `origin/main` (cd4d8fa), один PR в `main`.
Срез закрывает путь целиком, как #84 закрыл портфолио: браузер готовит квадрат 512×512,
API проверяет сигнатуру и размеры, файл лежит на том же локальном диске, что фотографии
портфолио, раздача идёт по стабильному относительному URL, а аватар виден в кабинете,
в каркасе кабинета и в публичной карточке барбера.

Дата прогона: 2026-11-11.

## Что сделано

### Backend

- **Домен.** `Constants.AVATAR_MEDIA_PATH_PREFIX = /api/media/avatars`;
  `Domain/Users/UserAvatarMediaUrl.cs` — зеркало `PortfolioMediaUrl`: `Build(userId, mediaId,
  extension)` и два `TryParse` (со своим `userId` и без него), строгий канонический разбор
  (Guid `D` нижнего регистра, 32 hex-символа, `.jpg|.png|.webp`, ровно два сегмента, запрещены
  `%`, `?`, `#`, `\`, пробелы). `User.SetAvatar(string?)`: `null`/пустая строка — снять аватар,
  **свой** media-URL — как есть, абсолютный `http(s)` — через `UrlValidator` (легаси-путь
  администратора), иначе `400 user.setAvatar.avatarUrl` с `invalidField = "avatarUrl"`;
  `MarkUpdated` только при реальном изменении. Общая приватная проверка `ValidateAvatarUrl`
  используется и в `SetAvatar`, и в `UpdateProfile`, поэтому `PUT users/{id}/profile` продолжает
  принимать текущий media-URL (регрессия «сохранение имени после загрузки аватара»).
  `UrlValidator` не менялся. Байты изображения домен не смотрит.
- **Хранилище.** `IPhotoStorage.SaveAvatarAsync(userId, mediaId, extension, content, ct)`;
  `OpenReadAsync`/`DeleteAsync` разбирают оба префикса (portfolio, затем avatars).
  `LocalPhotoStorage` пишет `{Storage:RootPath}/avatars/{userId:N}/{mediaId:N}.{ext}` тем же
  атомарным `.tmp` + `File.Move` и с той же проверкой «путь внутри корня»; ошибка записи аватара —
  `500 user.uploadAvatar.storage` без пути и имени файла; чтение/удаление чужого и внешнего URL —
  молча `404`/no-op; код чтения — `avatarMedia.notFound`. Конфигурация (`Storage:RootPath`)
  не менялась, изменились только XML-доки.
- **Application.** `UploadUserAvatarCommand`/`UploadUserAvatarHandler` и
  `DeleteUserAvatarCommand`/`DeleteUserAvatarHandler` с точным порядком:
  `RequireUserId → user → ImageFileInspector.Inspect → SaveAvatarAsync → SetAvatar → UpdateAsync →
  best-effort удаление предыдущего файла`; любая ошибка после записи удаляет **новый** файл,
  предыдущий не трогается, пока строка не сохранена. Удаление идемпотентно: `null` — успех без
  обращения к диску. Лимиты — тот же `ImageFileInspector` (1 КиБ..5 МиБ, 300..6000 px, ≤ 24 Мп,
  JPEG/PNG/WebP), второго набора проверок нет. Логи: `userId`, размер, `Width×Height` — без
  email, URL, пути и имени файла. `IUserRepository.GetAvatarUrlAsync` — узкий `SELECT avatar_url`
  по PK без `Include` и отслеживания; нет пользователя — `avatarMedia.notFound`, нет аватара —
  успех с `null`.
- **Web / контракты.** `POST api/users/me/avatar` (`[Authorize]`, multipart `file`, 6 МиБ на
  запрос, чтение потока порциями с жёстким пределом `MAX_PHOTO_BYTES + 1`): пустое поле —
  `400 user.uploadAvatar.fileRequired`, превышение — `400 user.uploadAvatar.tooLarge`, успех —
  `201 { "avatarUrl": "…" }` (`AvatarUploadResponse`, без `Location`); CSRF обязателен и
  обслуживается общим antiforgery-фильтром. `DELETE api/users/me/avatar` — всегда `204`.
  `AvatarMediaController` (`api/media/avatars`, `[AllowAnonymous]`, reads-лимит): имя файла по
  `GeneratedRegex`, доступ только когда сохранённая строка **равна** запрошенному URL (Ordinal),
  иначе единый `404 avatarMedia.notFound`; ответ — `File(... enableRangeProcessing: true)`,
  `Cache-Control: public, max-age=86400`, `X-Content-Type-Options: nosniff`, без исключений
  «владельцу/админу». `BarberProfileResponse` получил `string? AvatarUrl` сразу после `UserId`;
  `BuildCardAsync` читает его **одним** индексным lookup по `users_pkey` (комментарий о цене в
  коде), публичная и собственная карточки получают поле одинаково. `BookingResponse`, поиск и
  админские DTO не менялись; `audit_log` и миграции не трогались.
- **Политика и legal.** `PrivacyPolicy.Version = 2026-11-11`; текст `/privacy` §3 дополнен
  фотографией профиля, §4 — сроком «до удаления пользователем или блокировки аккаунта».
  Старые записи согласия не переписываются (долг #23).

### Frontend

- **Данные.** `src/lib/avatars.ts`: константы, `uploadAvatar` (FormData с полем `file`, через
  `mutateWithCsrf`, guard `isAvatarUploadResponse`), `deleteAvatar`, `describeAvatarError`
  (`field`, русское сообщение, `canRetry = status === 0 || 429 || ≥ 500`; коды
  `barberProfile.uploadPhoto.*`, `user.uploadAvatar.*`, `413`, `401`, `429`, сеть, `5xx`).
  `response-guards.ts`: `isAvatarUploadResponse`, `isBarberProfile` с `avatarUrl`.
  `catalog.ts`: `BarberProfile.avatarUrl: string | null`; фикстуры всех тестов обновлены.
- **Устойчивость транспорта.** `readJson` больше не выпускает сырой `TypeError`, если тело
  ответа оборвано (прокси закрывает соединение после `413`; в тестах mock отвечает, пока
  multipart ещё пишется): статус сохраняется, тело считается пустым, поэтому `413` остаётся
  «файл слишком большой», а не нейтральным fallback. `AbortError` по-прежнему пробрасывается.
  Покрыто двумя тестами в `api.test.ts`.
- **Подготовка файла.** `image-prep.ts`: `prepareAvatarImage` рядом с `preparePortfolioImage` —
  общий `decode`, общий приватный `encodeWithinLimit` (0.85, повтор 0.7), проверки типа и
  исходного размера до декодирования, квадратный центр-кроп по `min(width, height)`, масштаб
  ровно до 512×512 (в том числе вверх), белая подложка, `avatar.jpg`. Поведение портфолио не
  изменилось: все прежние тесты зелёные.
- **Кабинет.** Новая карточка «Аватар» выше `ProfileForm`: превью 96 px (`Avatar`/`AvatarImage`
  при `avatarUrl`, иначе инициалы; изображение декоративно), скрытый файловый input и кнопки
  «Загрузить фото»/«Изменить фото»/«Удалить», спиннер и `disabled` только на время операции,
  ошибки `role="alert"`, успех `role="status"` («Аватар обновлён.»/«Аватар удалён.»), удаление
  через `alertdialog` с возвратом фокуса на кнопку загрузки. После успеха `setAvatar(...)`
  правит модульный кэш без второго `GET`, а барберу ещё и сбрасывается кэш публичной карточки
  (`invalidateBarberProfile`). На 320 px кнопки 44 px, горизонтального скролла нет.
- **Каркас и публичная карточка.** `AccountIdentity` и `BarberHero` показывают `AvatarImage`
  при наличии `avatarUrl`, инициалы остались fallback'ом; docstring героя обновлён
  («раньше аватара в контракте не было»). Админские карточки уже отдавали аватар — проверены
  тестом и E2E, код админки не менялся.
- **i18n/доступность.** Новые строки через `t()`, английские каталоги `account.json`,
  `errors.json`, `privacy.json`; один `h1` на странице, axe на 1280 и 320 без новых исключений.

### E2E

- `account-avatar-mutations.spec.ts` (`mutations`, serial, проект `mutations-chromium`):
  загрузка настоящего PNG, `naturalWidth > 0` и `src` с `/api/media/avatars/`, тот же кадр в
  `AccountIdentity`, reload сохраняет, админская карточка пользователя видит тот же `src`,
  axe 1280 и 320, удаление через `alertdialog` возвращает инициалы и старый URL отдаёт `404`.
- `public-barber-avatar-mutations.spec.ts` (`profileBarber`): загрузка → `GET /api/barbers/{id}`
  и герой показывают файл → удаление → инициалы и `avatarUrl: null`. Витринный барбер `…0001`
  не трогается; `afterEach` при падении убирает аватар через `DELETE /api/users/me/avatar`,
  успешные сцены передают состояние дальше; `page.route` для успешных тел не используется.
- `seed.sql` сбрасывает `avatar_url` и у изолированного `profileBarber` (у `mutations` блок уже
  сбрасывал), поэтому повторный прогон начинается с инициалов.

### Документация

`backend/AGENT.md` (§4, §7.3, §8, §15.2, §16), `frontend/AGENT.md` (§9.1/§9.3, §12.1, §13,
§14 — долг #31 и новая строка #32, §15, §16, §17), `frontend/README.md` (аватар и E2E-сценарии),
этот отчёт.

## Прогоны

| Команда | Результат |
|---|---|
| `dotnet build backend/BarberSearch/BarberSearch.sln` | 0 ошибок, 0 предупреждений |
| `dotnet test BarberSearch.Domain.UnitTests` | 465 / 465 |
| `dotnet test BarberSearch.Application.UnitTests` | 747 / 747 |
| `dotnet test BarberSearch.Infrastructure.UnitTests` | 228 / 228 |
| `dotnet test BarberSearch.IntegrationTests` (`BARBERSEARCH_TEST_POSTGRES`, отдельные БД `barbersearch_it_*`) | 238 / 238, 5 м 10 с |
| `dotnet ef`/SQL `__EFMigrationsHistory` | 33 миграции, новых нет |
| `npm run lint` | 0 предупреждений / 0 ошибок (480 файлов, 116 правил) |
| `npm test` | 1166 тестов / 116 файлов, все зелёные |
| `npm run build` | успех, 2577 модулей, `tsc -b` чисто, 3.29 с |
| `npm run check:i18n -- --strict` | t() 1665, переводов 1968, **0 пропусков**, 0 конфликтов, exit 0 |
| `npm run test:e2e -- --workers=2` | **287 прошло, 4 пропущено** (известные mobile/desktop), 0 упало, 5.4 мин; axe 1280/320 без новых исключений |
| API-проверка вручную | `GET /api/media/avatars/...` — `200` с `Cache-Control`/`nosniff`/`Range`, после замены и удаления — `404 avatarMedia.notFound`; `GET users/me` после удаления — `avatarUrl: null`; `GET /api/barbers/{id}` отдаёт `avatarUrl`; файлы писались в `{Storage:RootPath}/avatars/{userId}/…` |

Backend-тесты запускались с `-p:BaseOutputPath` во временном каталоге: тест-хост в этом
окружении получает `Access is denied` при открытии handle родителя, если `bin` лежит внутри
репозитория (то же ограничение отмечено в `issue-84-report.md`). Интеграционные тесты — на
выделенной БД `barbersearch_it_*`, dev-база не трогалась.

## Отклонения от промпта

1. Поведение `GetAvatarUrlAsync` (URL / `null` / `NotFound` / без отслеживания) проверяется в
   `IntegrationTests.UserAvatarTests`, а не в `Infrastructure.UnitTests`: в этом проекте нет
   провайдера БД, а добавлять пакет запрещено. В `Infrastructure.UnitTests` остался
   `UserAvatarUrlQueryTests`, который проверяет трансляцию запроса (один столбец, PK, без join).
2. Полностью пустая multipart-форма отклоняется биндером до действия; случай «нет поля `file`»
   в интеграционном тесте посылает текстовое поле, а 0-байтовый файл покрыт отдельно и даёт
   `user.uploadAvatar.fileRequired`.
3. Добавлены `Web/Controllers/Users/UploadUserAvatarForm.cs` (нужен для `[FromForm]`, зеркало
   `UploadPortfolioPhotoForm` из #84) и `internal` запись `UserAvatarUrlLookup` — она отличает
   «пользователя нет» от «аватара нет».
4. Внешний абсолютный предыдущий URL никогда не передаётся в `IPhotoStorage.DeleteAsync`:
   локального файла у него нет, удалять нечего (совпадает с требованием «внешний URL не удаляем»).
5. `image-prep.ts` экспортирует `SUPPORTED_IMAGE_TYPES`: карточка проверяет тип и исходный
   размер до декодирования, не дублируя список MIME. Общий `encodeWithinLimit` (0.85 → 0.7)
   применён к обеим функциям без изменения поведения портфолио.
6. Подъём редакции Политики потребовал правок, которых не было в списке промпта:
   `e2e/specs/registration-consent.spec.ts` (`EXPECTED_CONSENT_VERSION`),
   `admin/user-detail.test.tsx` (текст «аккаунт создан до …» выводится из редакции) и примеры
   дат в `README.md`.
7. `seed.sql` не сбрасывал `avatar_url` у изолированного `profileBarber` — добавлено, иначе
   второй прогон `public-barber-avatar-mutations` начинался бы не с инициалов.
8. В E2E инициалы кабинета и героя берутся из разных источников: кабинет — из имени/фамилии
   аккаунта (`profileInitials`), герой — из `displayName` профиля (`getInitials`). Поэтому в
   спеке две константы (`ACCOUNT_INITIALS`, `HERO_INITIALS`), а локатор аватара героя привязан к
   карточке с `h1`: в шапке сайта тоже есть `Avatar`, но без файла.
9. `describeAvatarError` отдельно маппит `barberProfile.uploadPhoto.empty` (код для файла
   меньше 1 КиБ, промпт его перечисляет) — текст «Изображение пустое или слишком маленькое…».
10. `PhotoStorageOptions` и регистрация хранилища изменились только в XML-доках; ключи
    конфигурации прежние.
11. Kestrel-овский `413` до действия по-прежнему не покрыт автотестом бэкенда (как и в #84);
    клиентская обработка покрыта unit-тестом.

## Долги и что осталось

- Аватара в шапке нет: `SessionUser` строится из claims и адреса не знает, глобальный
  провайдер `/users/me` для шапки — отдельное решение (строка #32 в `frontend/AGENT.md` §14).
- Аватар не отдаётся в результатах поиска (`/api/barbers`), в бронях и в админских списках
  (только в карточках пользователя и барбера, где он уже был).
- Модерации/блокировки аватара админом, премодерации и очереди «на проверке» нет.
- Серверных миниатюр, `srcset`, WebP/AVIF и серверного кропа нет; клиент готовит 512×512.
- Один инстанс API и один диск: `avatars/` входит в тот же бэкап, что `portfolio/`,
  общий volume/object storage — долг #31.
- `Cache-Control: public, max-age=86400`: после замены/удаления старый кадр может ещё сутки
  жить в кэше другого браузера — та же цена, что у портфолио; лечится сменой `mediaId`,
  а не `no-store` для всех.
- Гонка двух параллельных загрузок: побеждает последняя запись, файл проигравшей может
  остаться сиротой — допустимо, версии строки у `User` нет.
- `audit_log` для аватара не пишется (allowlist `User` не расширяли).
- Старые записи согласия остаются с прежней редакцией (долг #23), backfill не делается.

## Найдено попутно, вне среза

- Первый прогон CI (`frontend-ci`) упал на юнит-тесте `413`: на Linux mock-ответ приходит,
  пока multipart-тело ещё пишется, и `response.text()` падает с `TypeError: terminated`.
  Локально на Windows тот же код проходил, потому что оборванное тело не читалось; в
  контейнере `node:24` (Linux) падение воспроизвелось. Это вскрыло реальную дыру транспорта —
  см. «Устойчивость транспорта» в разделе Frontend.

- Сборка #85 перегруппировала общие чанки: UI-примитивы отделились от `csrf` в новый
  `card-*.js`, `image-prep` стал общим чанком, `barber-profile-form` — тоже. Числа §12.1
  `frontend/AGENT.md` пересняты по факту, «уменьшение» `barber-profile-page` — это переезд
  общих модулей, а не удаление кода.
- Первый прогон новых E2E-сцен выявил две хрупкие привязки теста (инициалы и `Avatar` в шапке);
  обе исправлены в page object/спеке, код продукта не менялся.
- `frontend/USER_AVATAR_PROMPT.md` оставлен в рабочем дереве как входной файл задачи;
  в репозиторий добавлена копия `frontend/verification/issue-85-user-avatar-prompt.md`.
