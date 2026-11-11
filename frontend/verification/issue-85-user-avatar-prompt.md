# Промпт для следующей сессии: аватар пользователя (BarberSearch, #85)

## Роль и задача

Ты — senior full-stack разработчик в репозитории `D:\Projects\barbersearch`
(React 19 + TypeScript + Vite во `frontend`, ASP.NET Core 10 + EF Core + PostgreSQL
в `backend`). Сделай **аватар пользователя**: загрузку файла, показ в кабинете и на
публичной карточке барбера, удаление. Срез закрывает путь целиком, как #84 закрыл
портфолио: браузер готовит квадрат 512×512, API проверяет сигнатуру и размеры, файл
лежит на том же локальном диске, что и фотографии портфолио, а раздача идёт по
стабильному относительному URL.

Ветка `features/issue-85` (номер — следующий после смерженного #84), один PR в
`main`. Заголовок в стиле репозитория: `feat(avatar): upload, cabinet and public barber card`.
Работай по правилам `frontend/AGENT.md` и `backend/AGENT.md`; противоречия с этим
промптом фиксируй в отчёте как отклонения, а не решай молча.

## Порядок работы

1. Ветка строго от свежего `origin/main`:
   `git fetch origin && git checkout -b features/issue-85 origin/main`
   (не от соседней `features/*` и не от устаревшего локального `main`).
2. Прочитай `frontend/AGENT.md` (§9–§10, §13–§16), `backend/AGENT.md` (§16, §7, §15.2)
   и файлы из списка ниже; код не меняй, пока не сойдутся контракты из этого промпта.
3. Порядок реализации: домен и хранилище → Application → Web/контракты → фронтенд →
   тесты → документация. Один PR в `main`; срез **сознательно трогает оба каталога**
   (`frontend/` и `backend/`) — это разрешено задачей и должно быть сказано в описании PR
   (как в #83/#84). Коммиты — `feat(avatar): …` / `feat(frontend): …` / `docs: …`,
   один логический шаг на коммит.
4. Если контракт или ограничение из промпта не сходится с кодом — остановись на этом
   пункте, опиши расхождение в отчёте и предложи решение, а не подстраивай молча.

## Что уже есть (прочитай до кода)

Пути backend ниже указаны относительно `backend/BarberSearch/src/`; тестовые проекты
лежат в `backend/tests/`, решение — `backend/BarberSearch/BarberSearch.sln`.

**Backend**

- `BarberSearch.Domain/Users/User.cs`: `AvatarUrl` (`string?`) уже существует и правится
  через `UpdateProfile(firstName, lastName, phoneNumber, avatarUrl)`. Валидация —
  `Domain/Common/UrlValidator.cs`: **только абсолютный** `http`/`https`, без `userinfo`,
  длина ≤ `Constants.MAX_URL_LENGTH` (2048), пустая строка → `null`. `UrlValidator`
  общий с салонами и фотографиями — ослаблять его нельзя.
- `Infrastructure.Postgresql/Configurations/UserConfiguration.cs`: `avatar_url` с
  ограничением `MAX_URL_LENGTH`. **Миграция для среза не нужна.**
- `Contracts/Users/Me/MyProfileResponse.cs` уже отдаёт `AvatarUrl`; админские
  `AdminUserDetailResponse` и `AdminBarberDetailResponse` — тоже. Публичный
  `BarberProfileResponse` аватара **не содержит**.
- `IUserRepository` (`Application/Users/IUserRepository.cs`): `GetByIdAsync`,
  `GetMyProfileAsync`, `UpdateAsync` и т. д. Узкого чтения аватара нет.
- Pipeline #84, который надо переиспользовать, а не копировать:
  - `Application/PortfolioPhotos/ImageFileInspector.cs` — проверка по сигнатуре
    JPEG/PNG/WebP, размеры из заголовков, 1 КиБ..5 МиБ, 300..6000 px, ≤ 24 Мп;
  - `Application/PortfolioPhotos/IPhotoStorage.cs` + `Web/Storage/LocalPhotoStorage.cs` —
    запись `{Storage:RootPath}/portfolio/{profileId:N}/{mediaId:N}.{ext}`, атомарный
    `.tmp` + `File.Move`, проверка «путь внутри корня», `OpenReadAsync`/`DeleteAsync` по URL;
  - `Web/Controllers/PortfolioMediaController.cs` — раздача по БД-проверке, `nosniff`,
    `Cache-Control`, диапазоны, единый `404 portfolioMedia.notFound`;
  - `Application/PortfolioPhotos/UploadPortfolioPhotoHandler.cs` — порядок «права → файл →
    домен → БД» и компенсация удалением файла;
  - `Web/Storage/PhotoStorageOptions.cs` — секция `Storage`, каталог создаётся на старте.
- `Web/Controllers/UsersController.cs`: `[Route("api/users")]`, `GET me`, `PUT {id}/profile`.
  Глобальный antiforgery-фильтр требует `X-CSRF-TOKEN` у всех cookie-мутаций, включая
  multipart (так работает загрузка фото #84).

**Frontend**

- `src/lib/my-profile.ts`: `MyProfile.avatarUrl`, `updateMyProfile` (PUT заменяет поле
  целиком, поэтому `avatarUrl` отправляется обратно как есть); комментарии «аватар пока
  не редактируется».
- `src/hooks/use-my-profile.ts`: одно чтение `GET users/me`, модульный кэш по `userId`,
  `save` без второго GET; `src/components/account/account-profile-context.ts`.
- `src/components/account/profile-form.tsx` отправляет `avatarUrl: profile.avatarUrl`;
  `src/components/account/account-shell.tsx` (`AccountIdentity`) рисует инициалы;
  `src/components/barber/barber-hero.tsx` — инициалы, в docstring прямо написано
  «Аватара в контракте нет».
- `src/lib/image-prep.ts` (`preparePortfolioImage`: decode → canvas → JPEG 0.85/0.7,
  EXIF/GPS срезаются перекодированием) и `src/lib/portfolio-photos.ts` (FormData-транспорт,
  `mutateWithCsrf`, маппинг кодов) — образцы стиля.
- `src/lib/response-guards.ts`: `isMyProfile` уже допускает `avatarUrl: null`,
  `isBarberProfile` — нет (нужно добавить поле), `isCreatedResource` = `{ id }`.
- `src/components/ui/avatar.tsx` (Base UI) — `Avatar`/`AvatarImage`/`AvatarFallback`,
  руками не править.
- i18n: `t()` по русским исходным строкам, английские каталоги в `src/i18n/*.json`,
  гейт `npm run check:i18n -- --strict`.
- `src/pages/account-profile-page.tsx` — `ProfileForm` + `AccountDetails`, сюда встаёт
  карточка аватара.
- E2E: `e2e/specs/*mutations.spec.ts` идут проектом `mutations-chromium` последними,
  `serial`; аккаунты `mutations` (`e2e-mutations@…`) и `profileBarber`
  (`9e100000-…-0015`, изолированный профиль) уже есть; `e2e/fixtures/seed.sql`
  сбрасывает `avatar_url = EXCLUDED.avatar_url`, где значение `NULL`.
  `e2e/fixtures/portfolio-photo.png` — настоящий PNG 800×600, годится и для аватара.

## Границы среза

**Входит:** загрузка/удаление аватара (API + хранилище + раздача), карточка «Аватар»
в `/account/profile`, аватар в `AccountIdentity`, `avatarUrl` в публичной карточке
барбера и в её герое, проверка, что админские карточки уже показывают аватар,
дополнение политики, тесты, документация.

**Не входит (осознанно, отдельные срезы):**

- аватар в шапке: `SessionUser` строится из claims и адреса аватара не знает; глобальный
  провайдер `/users/me` для шапки — отдельное решение, а не побочный эффект среза;
- аватар в результатах поиска (`/api/barbers` без аватара) и в бронях;
- модерация/блокировка аватара админом, премодерация, очередь «на проверке»;
- серверные миниатюры, `srcset`, WebP/AVIF, кроп на сервере;
- S3/MinIO, общий volume, несколько инстансов;
- запись в `audit_log` (allowlist `User` не расширяем);
- миграции, новые NuGet/npm-пакеты, изменения `src/components/ui/**`.

## Обязательные требования

### A. Домен

1. `Constants.AVATAR_MEDIA_PATH_PREFIX = "/api/media/avatars"` рядом с
   `PORTFOLIO_MEDIA_PATH_PREFIX`.
2. `Domain/Users/UserAvatarMediaUrl.cs` — зеркало `PortfolioMediaUrl` со своими тестами:
   `Build(userId, mediaId, extension)`; `TryParse(url, userId, out mediaId, out extension)`;
   второй overload без ожидаемого `userId` (нужен хранилищу, которое получает только URL).
   Строго: канонический `Guid` `D` нижнего регистра (36 символов), имя файла — 32 hex
   нижнего регистра, `.jpg|.png|.webp`, ровно два сегмента после префикса, запрещены
   `%`, `?`, `#`, `\`, пробелы. XML-доки по-русски, как у соседей.
3. `User.SetAvatar(string? avatarUrl)` → `UnitResult<Failure>`:
   - `null`/пустая строка → `AvatarUrl = null`;
   - **свой** media-URL (`UserAvatarMediaUrl.TryParse(url, Id, …)`) принимается как есть;
   - абсолютный `http(s)` URL принимается через `UrlValidator` (легаси-путь админа:
     `PUT users/{id}/profile` исторически умеет внешнюю ссылку);
   - иначе `400 user.setAvatar.avatarUrl`, `InvalidField = "avatarUrl"`;
   - `MarkUpdated()` только если значение реально изменилось.
4. `UpdateProfile` продолжает принимать свой media-URL: кабинет отправляет `avatarUrl`
   обратно, и сохранение имени не должно падать с `400`. Вынеси общую проверку аватара в
   один приватный метод `User` и используй его в обоих местах. `UrlValidator` не меняй.
5. Домен не проверяет байты изображения — это Application.

### B. Хранилище

6. `IPhotoStorage` получает `SaveAvatarAsync(Guid userId, Guid mediaId, string extension,
   ReadOnlyMemory<byte> content, CancellationToken)`; `OpenReadAsync`/`DeleteAsync`
   разбирают **оба** префикса (сначала portfolio, затем avatars). Имя интерфейса не
   меняем, но в XML-доке объясни: один адаптер, один корень, один диск — два вида файлов.
7. `LocalPhotoStorage`:
   - путь `{Storage:RootPath}/avatars/{userId:N}/{mediaId:N}.{ext}`;
   - та же атомарная запись и та же проверка «внутри корня»;
   - ошибка записи аватара — `500 user.uploadAvatar.storage` без пути и имени файла в тексте;
   - чтение/удаление чужого или внешнего URL — молча `404`/no-op, как для портфолио;
   - код `404 avatarMedia.notFound` для раздачи.
8. Конфигурация не меняется (`Storage:RootPath`). В документации отметь, что каталог
   `avatars/` входит в тот же бэкап, что и `portfolio/` (долг #31 остаётся).

### C. Application

9. `UploadUserAvatarCommand(ReadOnlyMemory<byte> Content)` → `Result<string, Failure>`:
   порядок — `RequireUserId` → загрузка пользователя → `ImageFileInspector.Inspect` →
   сохранение файла → `SetAvatar` → `UpdateAsync` → удаление **предыдущего** файла
   best-effort. Любая ошибка после сохранения файла удаляет **новый** файл компенсацией;
   предыдущий не трогается, пока запись в БД не удалась.
10. `DeleteUserAvatarCommand` → `UnitResult<Failure>`: идемпотентно; `AvatarUrl == null` —
    успех без удаления файла; иначе запомнить URL, `SetAvatar(null)`, `UpdateAsync`,
    затем удалить файл best-effort.
11. Лимиты — **тот же** `ImageFileInspector` без второго набора проверок (1 КиБ..5 МиБ,
    300..6000 px, ≤ 24 Мп, JPEG/PNG/WebP по сигнатуре). В доке объясни, почему не заводим
    отдельный «аватарный» набор: одна проверка — один источник правды; клиент готовит 512×512.
12. Гонка двух параллельных загрузок: побеждает последняя запись, файл проигравшей может
    остаться сиротой — это допустимо и документируется (версии строки у `User` нет).
13. `IUserRepository` получает узкое чтение для раздачи:
    `Task<Result<string?, Failure>> GetAvatarUrlAsync(Guid userId, CancellationToken)`.
    Нет пользователя — `Error.NotFound("avatarMedia.notFound", …)`; пользователь без
    аватара — успех с `null`. Политика без `Include` и без отслеживания, как у
    `GetSecurityStampAsync`.
14. Логи: только `userId`, размер файла и `Width×Height`. Ни email, ни URL, ни пути,
    ни имени файла.

### D. Web / контракты

15. `POST api/users/me/avatar` (`[Authorize]`, multipart, поле `file`):
    `[RequestSizeLimit]`/`[RequestFormLimits]` = 6 МиБ, поток читается в память порциями с
    жёстким пределом `Constants.MAX_PHOTO_BYTES + 1` (как в `UploadPhoto`);
    пустое поле — `400 user.uploadAvatar.fileRequired`, превышение —
    `400 user.uploadAvatar.tooLarge`; успех — `201 { "avatarUrl": "<относительный URL>" }`
    (новый контракт `Contracts/Users/Me/AvatarUploadResponse.cs`), без `Location`.
    CSRF обязателен: multipart-мутация идёт через общий antiforgery-фильтр, исключений нет.
16. `DELETE api/users/me/avatar` (`[Authorize]`) → всегда `204`, в том числе когда аватара
    не было.
17. `Web/Controllers/AvatarMediaController.cs`: `[Route("api/media/avatars")]`,
    `[AllowAnonymous]`, `[EnableRateLimiting(RateLimitSettings.ReadsPolicyName)]`,
    `GET {userId:guid}/{fileName}`; имя проверяется `[GeneratedRegex("^[0-9a-f]{32}\\.(jpg|png|webp)$")]`;
    URL собирается `UserAvatarMediaUrl.Build`; доступ — только если
    `GetAvatarUrlAsync(userId)` вернул **строку, равную** запрошенному URL (`Ordinal`);
    иначе `404 avatarMedia.notFound`. Ответ: `File(photo.Stream, photo.ContentType,
    enableRangeProcessing: true)`, заголовки `Cache-Control: public, max-age=86400` и
    `X-Content-Type-Options: nosniff`. Никаких исключений «владельцу/админу»: аватар
    публичен по определению. Никакого `403`/`410` — существование файла не раскрывается.
18. `BarberProfileResponse` получает `string? AvatarUrl` сразу после `UserId` (одно новое
    поле, остальные не двигаются по смыслу); XML-док: «аватар владельца профиля, `null`
    если не загружен». `BuildCardAsync` читает его **одним** запросом по PK
    (`Context.Users.AsNoTracking().Where(u => u.Id == profile.UserId).Select(u => u.AvatarUrl)`);
    публичная и собственная карточки получают поле одинаково. В комменте зафиксируй цену:
    один индексный lookup на чтение карточки, отдельного join'а в тяжёлый запрос нет.
19. `BookingResponse`, поисковые и админские DTO не меняются (у админских аватар уже есть).

### E. Frontend

20. `src/lib/avatars.ts` (по образцу `portfolio-photos.ts`):
    - `AVATAR_MAX_BYTES = 5 * 1024 * 1024`, `AVATAR_SOURCE_MAX_BYTES = 15 * 1024 * 1024`,
      `AVATAR_SIDE = 512`;
    - `uploadAvatar(file): Promise<string>` → `POST /users/me/avatar` через `mutateWithCsrf`
      с `FormData` (поле `file`), guard `isAvatarUploadResponse`; `deleteAvatar(): Promise<void>`
      → `DELETE /users/me/avatar`;
    - `describeAvatarError(error): { field: 'file' | null; message: string; canRetry: boolean }`,
      маппинг кодов: общие `barberProfile.uploadPhoto.empty|tooLarge|unsupportedType|
      invalidImage|dimensions`, новые `user.uploadAvatar.fileRequired|tooLarge|storage`,
      `413` без ProblemDetails, `401`, `429`, `0`, `5xx` (`canRetry = status === 0 || 429 || >= 500`),
      иначе `describeApiFailure` с русским фолбэком. Английский `detail` не показывать.
21. `src/lib/response-guards.ts`: `isAvatarUploadResponse` — запись с непустой строкой
    `avatarUrl`, начинающейся с `/api/media/avatars/`; `isBarberProfile` получает
    `isNullableString(value.avatarUrl)`.
22. `src/lib/image-prep.ts`: `prepareAvatarImage(file: File): Promise<File>` рядом с
    `preparePortfolioImage`, без дублирования canvas-кода (переиспользуй `decode` и
    `encodeJpeg`; общий «кодируем 0.85, при перевесе повторяем 0.7» вынеси в один
    приватный хелпер и примени к обеим функциям, не меняя поведение портфолио):
    - те же проверки типа и исходного размера до декодирования;
    - **квадратный центр-кроп**: сторона = `min(width, height)`, смещения по центру;
    - масштаб до ровно 512×512 (в том числе вверх, чтобы выполнялся серверный минимум
      300 px), белая подложка, JPEG 0.85, повтор 0.7 если > `AVATAR_MAX_BYTES`;
    - имя результата `avatar.jpg`.
23. `src/hooks/use-my-profile.ts`: добавь `setAvatar(url: string | null)`, который правит
    модульный кэш и состояние без второго `GET`; `save()` продолжает отправлять
    `avatarUrl: cachedProfile.avatarUrl`.
24. `src/components/account/avatar-card.tsx` (новый) на `/account/profile` **выше**
    `ProfileForm`:
    - превью 96 px (`Avatar` + `AvatarImage` при `profile.avatarUrl`, иначе инициалы;
      изображение декоративно — `alt=""`, рядом имя из `AccountIdentity`/заголовка);
    - скрытый `<input type="file" accept="image/jpeg,image/png,image/webp">` и кнопки
      «Загрузить фото» (без аватара) / «Изменить фото» (с аватаром) / «Удалить»;
    - клиентские проверки до подготовки, подготовка `prepareAvatarImage`, спиннер и
      `disabled` только на время операции;
    - ошибка клиентской проверки — у поля/карточки, ошибка сервера — `role="alert"` с текстом из
      `describeAvatarError`; успех — `role="status"` «Аватар обновлён.» / «Аватар удалён.»;
    - удаление — через `alertdialog` («Аватар исчезнет из карточки и кабинета. Файл
      удаляется безвозвратно.»), фокус после закрытия возвращается на кнопку загрузки;
    - после успеха `setAvatar(newUrl | null)` и, если у профиля есть `barberProfileId`,
      `invalidateBarberProfile(id)` — публичная карточка не должна показывать старый кэш;
    - подсказка: «JPEG, PNG или WebP до 5 МБ. Квадрат 512×512 подготовит браузер.»
    - на 320 px кнопки 44 px, горизонтального скролла нет.
25. `AccountIdentity`: `AvatarImage` при `profile?.avatarUrl`.
26. `BarberHero`: `AvatarImage` при `profile.avatarUrl`; обнови docstring («раньше аватара
    в контракте не было»), fallback остаётся инициалами.
27. `src/lib/catalog.ts`: `BarberProfile.avatarUrl: string | null`; обнови фикстуры
    `BarberProfile` в тестах (`barber-profile-page.test.tsx`, `barbers-screen.test.tsx` и др.).
28. Шапку, поиск, админские экраны и `src/components/ui/**` не трогай; админские карточки
    проверь тестом/сценарием, что `AvatarImage` получает `src`.

### F. Политика и legal

29. `/privacy` §3 дополняется фотографией профиля (аватар загружает сам пользователь;
    у барбера она публикуется в открытой карточке); §4 — срок «до удаления пользователем
    или блокировки аккаунта».
30. Редакция поднимается до `2026-11-11`: `frontend/src/lib/legal.ts`
    (`PRIVACY_POLICY_DATE`, `PRIVACY_POLICY_VERSION`) и
    `backend/BarberSearch/src/BarberSearch.Application/Privacy/PrivacyPolicy.cs`
    (`Version`) — синхронно. Обнови
    тесты, которые жёстко ждут `2026-11-03` (`legal.test.ts`,
    `register-form.test.tsx` → `EXPECTED_CONSENT_VERSION`, серверные тесты согласия).
    Старые записи согласия не переписываются: у них остаётся прежняя редакция (долг #23) —
    это фиксируется в доках. Если владелец решит вынести правку политики отдельно —
    зафиксируй решение в отчёте, не оставляй политику без упоминания аватара.

### G. i18n, доступность, качество

31. Все видимые строки — через `t()`; английские переводы в подходящий неймспейс
    (`account.json`, `barber-public.json`, `privacy.json`); `check:i18n -- --strict` — 0 пробелов.
32. Один `h1` на странице, `aria-busy`/`role="status"`/`role="alert"` по месту, фокус
    после удаления, axe без новых исключений на 1280 и 320.
33. Никаких новых зависимостей и никаких правок `ui/**`.

## Точные контракты

```http
POST /api/users/me/avatar
Content-Type: multipart/form-data; boundary=…
X-CSRF-TOKEN: …
file=@avatar.jpg

201 Created
{ "avatarUrl": "/api/media/avatars/9e000000-0000-4000-8000-000000000004/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg" }
```

```http
DELETE /api/users/me/avatar
X-CSRF-TOKEN: …

204 No Content
```

```http
GET /api/media/avatars/{userId}/{mediaId}.{jpg|png|webp}

200 OK
Content-Type: image/jpeg
Cache-Control: public, max-age=86400
X-Content-Type-Options: nosniff
Accept-Ranges: bytes

404 Not Found → ProblemDetails { "code": "avatarMedia.notFound", … } (все причины отказа)
```

Коды: `user.uploadAvatar.fileRequired`, `user.uploadAvatar.tooLarge`,
`user.uploadAvatar.storage`, `user.setAvatar.avatarUrl` (домен),
`avatarMedia.notFound`; проверки файла — существующие `barberProfile.uploadPhoto.*`.

## Тесты (минимум)

**Domain**: `UserAvatarMediaUrlTests` — сборка/разбор, свой/чужой `userId`, верхний
регистр, `%`, `..`, лишний сегмент, недопустимое расширение, пустой `Guid`;
`UserTests` — `SetAvatar` своим media-URL, чужим URL (ошибка `user.setAvatar.avatarUrl`),
абсолютным URL, пустой строкой (`null`), без изменения `UpdatedAt` при том же значении;
`UpdateProfile` принимает свой media-URL и по-прежнему внешний/`null`.

**Application**: `UploadUserAvatarHandlerTests` — права проверяются до файла;
коды инспектора; ошибка хранилища; ошибка `SetAvatar` и ошибка `UpdateAsync` удаляют
новый файл; предыдущий файл удаляется после успеха; внешний/абсолютный предыдущий URL
не удаляется; логи без URL. `DeleteUserAvatarHandlerTests` — `null` → успех без storage;
успешное удаление; ошибка сохранения не удаляет файл.

**Infrastructure**: `GetAvatarUrlAsync` — URL, `null` без аватара, `NotFound` для
неизвестного id; запрос без отслеживания.

**Integration (PostgreSQL)**: `POST me/avatar` 201 и `avatarUrl` в `GET users/me`;
аноним — 401; без CSRF — 400 `auth.csrf.invalid`; слишком большой файл — 400;
неверный формат — 400; `GET /api/media/avatars/...` 200 с заголовками; замена аватара →
старый URL 404; `DELETE` → 204, затем URL 404; `DELETE` повторно → 204;
`GET /api/barbers/{id}` отдаёт `avatarUrl`; `PUT users/{id}/profile` с текущим
media-URL не ломается (регрессия на баг «сохранение имени после загрузки аватара»);
`PUT` с чужим media-URL отклоняется.

**Frontend unit**: `avatars.test.ts` (FormData-поле `file`, CSRF, guard, все ветки
`describeAvatarError`); `response-guards.test.ts` (`isAvatarUploadResponse`,
`isBarberProfile` с `avatarUrl`); `image-prep.test.ts` (кроп 1200×800 → квадрат 800 → 512;
300×300 → 512 вверх; центр-кроп смещения; fallback 0.7; ошибки типа/размера/декода);
`avatar-card.test.tsx` (загрузка и объявление, отказ типа, `413`, `401`, удаление через
`alertdialog`, возврат фокуса); `account-shell.test.tsx` (изображение при `avatarUrl`);
`barber-hero`/страница карточки (изображение при `avatarUrl`, инициалы без него).

**E2E**:
- `account-avatar-mutations.spec.ts` (`storageState: mutations`, `serial`, проект
  `mutations-chromium` по имени `*mutations.spec.ts`): загрузка настоящего PNG
  (`portfolio-photo.png`), `naturalWidth > 0` и `src`, начинающийся с `/api/media/avatars/`;
  изображение в `AccountIdentity`; reload сохраняет; админский контекст видит тот же `src`
  в карточке пользователя; axe на 1280 и 320; удаление через `alertdialog` возвращает
  инициалы и `404` у media-URL.
- `public-barber-avatar-mutations.spec.ts` (`storageState: profileBarber`): загрузка →
  `GET /barbers/{id}` и герой показывает изображение → удаление → снова инициалы.
  Витринного барбера `…0001` не трогать.
- `afterEach` при падении удаляет аватар через `DELETE /api/users/me/avatar` (по образцу
  уборки фото #84), `seed.sql` уже сбрасывает `avatar_url`; успешные сцены передают
  состояние следующей.
- `page.route` для успешных тел запрещён (правило §15 AGENT.md).

## Документация

- `frontend/AGENT.md`: §9.3 (две мутации и одна раздача), §12.1 (новые размеры чанков
  после build), §13 (кабинет/публичная карточка — аватар), §14 (уточнить #31: аватар
  сделан, ограничения остались; новая строка #32 — аватар в шапке и поиске, модерация
  аватара, аудит, миниатюры, один диск), §15 (перечень новых тестов), §16 (completed
  «2026-11-11: user avatar (#85)»), §17 при необходимости.
- `frontend/README.md`: раздел про аватар и E2E-сценарии.
- `backend/AGENT.md`: §4 (новых проектов нет), §7 (инвариант `AvatarUrl`), §16
  (эндпоинты и контракт `AvatarUploadResponse`), §15.2 (новых индексов нет, цена одного
  PK-lookup), §17.4 без изменений.
- `frontend/verification/issue-85-report.md`: что сделано, таблица прогонов, отклонения
  от промпта, оставшиеся долги (по образцу `issue-84-report.md`).

## Критерии готовности и проверка

```powershell
dotnet build D:\Projects\barbersearch\backend\BarberSearch\BarberSearch.sln
dotnet test D:\Projects\barbersearch\backend\tests\BarberSearch.Domain.UnitTests
dotnet test D:\Projects\barbersearch\backend\tests\BarberSearch.Application.UnitTests
dotnet test D:\Projects\barbersearch\backend\tests\BarberSearch.Infrastructure.UnitTests
# интеграционные — на отдельной БД barbersearch_it_*, не на dev
dotnet test D:\Projects\barbersearch\backend\tests\BarberSearch.IntegrationTests

cd D:\Projects\barbersearch\frontend
npm run lint                       # 0 warnings / 0 errors
npm test                           # все unit-тесты зелёные
npm run build                      # tsc -b + vite, без предупреждений
npm run check:i18n -- --strict     # 0 пробелов
npm run test:e2e -- --workers=2    # 0 failed, только известные skip
```

Руками: загрузить аватар → `App_Data/photos/avatars/{userId}/…` появился; reload и
кабинет показывают его; у барбера он виден на `/barbers/{id}` анониму; замена аватара
делает старый URL `404`; удаление убирает файл и возвращает инициалы; `GET users/me`
отдаёт `avatarUrl: null`. Миграций быть не должно: `dotnet ef migrations list` —
прежние 33.

## Чего не делать

- Не менять `UrlValidator` и не разрешать относительные URL другим агрегатам.
- Не хранить файл в PostgreSQL и не раздавать его через контроллер без проверки по БД.
- Не добавлять аватар в claims `/auth/me`, в `SessionUser` и в шапку.
- Не добавлять второй инспектор изображений, второй лимит или серверный кроп.
- Не добавлять `audit_log`, модерацию, миниатюры, S3, миграцию, пакеты.
- Не логировать email, URL, путь, имя файла и тело.
- Не править `src/components/ui/**` руками и не переформатировать соседний код.
- Не мокать успешные ответы в E2E и не трогать витринного барбера `…0001`.

## Подводные камни

1. **`PUT users/{id}/profile` после загрузки.** Форма отправляет `avatarUrl` обратно;
   если домен продолжит требовать только абсолютный URL, сохранение имени начнёт падать
   с `400`. Это главный регрессионный тест среза.
2. **Кэш публичной карточки** (`src/lib/catalog.ts`, `invalidateBarberProfile`) — без
   инвалидации барбер увидит старый аватар на своей публичной странице.
3. **Кэш браузера**: `public, max-age=86400` означает, что после замены/удаления старый
   кадр может ещё сутки жить в кэше другого браузера; это то же ограничение, что у
   портфолио — опиши в доке, не «лечи» случайным `no-store` для всех.
4. **Компенсация**: файл удаляется, только если строка не сохранилась; при неудачном
   `UpdateAsync` предыдущий аватар должен остаться в БД и на диске.
5. **Seed/E2E**: `mutations` и `profileBarber` — общие фикстуры; сценарии `serial`,
   уборка при падении, витрина `…0001` не трогается.
6. **`isBarberProfile`**: guard строгий; забытое поле `avatarUrl` в фикстурах тестов
   даст лавину падений — обнови фикстуры сразу.
7. **i18n**: английские каталоги обязательны, иначе `check:i18n -- --strict` упадёт.
8. **Политика**: бамп редакции меняет текст согласия в форме регистрации; старые записи
   согласия остаются с прежней редакцией (не бэкфиллим).

## Отчёт

В конце — `frontend/verification/issue-85-report.md`: что сделано по backend/frontend,
таблица прогонов (команда → результат, число тестов), отклонения от промпта с причинами,
что осталось (шапка, поиск, модерация, аудит, миниатюры, один диск) и что найдено попутно.
