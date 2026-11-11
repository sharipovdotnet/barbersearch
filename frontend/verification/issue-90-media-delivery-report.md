# Issue 90 — доставка медиа: миниатюры, `srcset` и честный кэш (BarberSearch)

Ветка `features/issue-90`, один PR в `main` (backend и frontend вместе). Срез закрывает
долги #31/#32: **картинка доставляется в том размере, в котором её показывают, а скрытый,
отклонённый или заменённый кадр перестаёт показываться сразу, а не через сутки.**

Дата прогонов: 2026-11-12. Миграций в срезе нет: всё, что нужно, — файлы на диске и заголовки
ответа.

## Что сделано

### Backend

- **Пакет.** `SkiaSharp` 4.156.0 + `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.156.0 в
  `BarberSearch.Web.csproj` (MIT) — первая нативная зависимость backend. Спайк до кода:
  JPEG 1000×800 → WebP 256×205, сигнатура `RIFF….WEBP`, `decoded=256x205`; в пакете Linux-ассетов
  есть `runtimes/linux-x64/native/libSkiaSharp.so` (12 324 784 B), значит `backend-ci` на
  `ubuntu-latest` и контейнер API подхватят библиотеку.
- **Варианты.** `Application/PortfolioPhotos/PhotoVariant.cs` (`Original/Small/Medium/Large`) и
  `PhotoVariantWidths.cs` — единственное место с числами и именами: портфолио `256/512/1024`,
  аватар `96/192/384`, файл `{mediaId:N}-{sm|md|lg}.webp` рядом с исходником, ETag
  `"<mediaId:N>"` / `"<mediaId:N>-sm|md|lg"`. Апскейла нет, кропа нет.
- **Генератор.** `Web/Storage/IPhotoThumbnailer.cs`, `PhotoThumbnailer.cs`, `PhotoThumbnail.cs`:
  `ImageFileInspector.Inspect` → `SKCodec.GetScaledDimensions` → декодирование сразу в целевой
  масштаб → отрисовка точного размера на белом фоне → `SKEncodedImageFormat.Webp` q80. Глобальный
  `SemaphoreSlim(2)`, отмена пробрасывается, ошибки — `Warning` без URL/пути/имени файла и
  `Failure` наружу (никогда не исключение). Прозрачный PNG получает белый фон, EXIF не
  переносится: байты сняты с пикселей.
- **Хранилище.** `IPhotoStorage.OpenReadAsync(url, variant, cancellationToken)`;
  `PhotoContent` получил `EntityTag` (только хранилище знает, что именно отдало). `LocalPhotoStorage`:
  запись исходника + best-effort генерация вариантов на загрузке (`.{mediaId:N}-{size}.tmp` +
  `File.Move`), ленивая генерация при первом `?size=`-чтении (файлы, загруженные до среза, и
  кадры, чья генерация упала), фолбэк на исходник при невозможном варианте, `DeleteAsync`
  удаляет исходник и все `{mediaId:N}-*.webp` по маске. Проверка «путь внутри корня» осталась
  на месте, вариант приходит типом, а не строкой.
- **Раздача.** Новый `Web/Common/MediaDelivery.cs` на оба контроллера: разбор `?size=`
  (ровно `sm|md|lg` в нижнем регистре, иначе `404`), `ETag`, `X-Content-Type-Options: nosniff`,
  `Accept-Ranges`, `If-None-Match` → `304` без тела (включая `*`, но не при `Range`), политика
  кэша. `Cache-Control`: видимый кадр `public, max-age=0, must-revalidate`, непубличный
  `private, no-store`, любой `404` — `no-store`. `Vary` не ставится. Порядок в действии:
  `size` → имя файла по регулярке → исходный URL → проверка доступа по БД (как раньше) →
  `OpenReadAsync(url, variant)` → заголовки. Правила видимости, владения и «отказ всегда 404»
  не менялись.
- **Чтения.** `PhotoThumbnails.Of(url, stored)` и `thumbnailUrl` = `{url}?size=md` для
  собственных media-URL, сохранённое легаси-значение либо `null` для внешних. Применяется после
  материализации строки в публичной, собственной и админской проекциях; SQL-проекции и порядок
  не тронуты. `portfolio_photos.thumbnail_url` остаётся как есть — БД и миграции не менялись.

### Frontend

- **`src/lib/media.ts`** (новый): `MediaSource`, `PORTFOLIO_WIDTHS` (256/512/1024),
  `AVATAR_WIDTHS` (96/192/384), `portfolioPhotoSource(url, sizes?)` (дефолт
  `(min-width: 1024px) 25vw, (min-width: 640px) 33vw, 50vw`, дескрипторы `256w/512w/1024w`),
  `avatarImageSource(url)` (`?size=sm 1x, ?size=md 2x, ?size=lg 3x`). `src` всегда исходник;
  внешние `https://…`/`data:`/`blob:` и строки с уже существующим `?`/`#` получают только `src`.
  В шапке файла зафиксировано, что ширины — часть контракта с `PhotoVariantWidths`.
- **Галерея и лайтбокс** (`barber-portfolio.tsx`): сетка — `{...portfolioPhotoSource(photo.url)}`,
  лайтбокс — исходник без `srcSet`/`sizes`; `eager`/`high` на первом кадре, `lazy` на остальных,
  `onError`-заглушка сохранена, разметка и CLS не менялись.
- **Кабинет и админка**: `barber-profile-photos.tsx` — `portfolioPhotoSource(url, '(min-width: 640px) 50vw, 100vw')`;
  `photos-screen.tsx` — превью строки `80px` для портфолио и `avatarImageSource` для аватара,
  крупный просмотр — исходник; `barber-detail.tsx` — превью `96px`, крупного просмотра в блоке нет.
- **Аватары**: все 16 мест `<AvatarImage>` (шапка, меню, кабинет, поиск, строки и детали броней,
  админские списки, очередь аватаров) используют `avatarImageSource`; вендорный
  `components/ui/**` не тронут, `alt=""`/`AvatarFallback`/«нет URL — нет картинки» сохранены.
- **Комментарии**: `PortfolioPhoto.thumbnailUrl` описывает `{url}?size=md`; в `image-prep.ts`
  абзац «серверных миниатюр нет» переписан (браузерная подготовка осталась из-за трафика, EXIF и
  лимитов доступа).

## Проверка

### Backend

```
> dotnet build BarberSearch/BarberSearch.sln
Сборка успешно завершена.  Предупреждений: 0  Ошибок: 0

> dotnet test tests/BarberSearch.Domain.UnitTests        → пройдено 588
> dotnet test tests/BarberSearch.Application.UnitTests   → пройдено 873
> dotnet test tests/BarberSearch.Infrastructure.UnitTests → пройдено 293
> dotnet test tests/BarberSearch.IntegrationTests        → пройдено 279
итого 2033, падений и пропусков нет

> dotnet ef migrations has-pending-model-changes --project BarberSearch/src/BarberSearch.Infrastructure.Postgresql \
      --startup-project BarberSearch/src/BarberSearch.Web
No changes have been made to the model since the last migration.
```

Новые тесты: `PhotoThumbnailerTests` (ширины и пропорции, WebP-сигнатура, апскейла нет,
прозрачность → белый фон, битый файл → `Failure`), `MediaDeliveryTests` (разбор `size`),
расширенные `LocalPhotoStorageTests` (варианты при загрузке, ленивая генерация, фолбэк, удаление
вариантов) и интеграционные сцены `PortfolioPhotoUploadTests` / `UserAvatarTests` /
`PhotoModerationTests` (`?size=`, ETag, `304`, политика кэша, матрица видимости, ленивая
догенерация, «исходник уже» → исходник, удаление вариантов, отсутствие `Vary`).

### Заголовки и размеры (живой API, кадр, загруженный до среза)

Исходник `01a1ca13…68e.jpg` (182 568 B, JPEG 800×600, загружен до появления вариантов) — после
первого `?size=`-запроса варианты догенерированы лениво:

| Запрос | Статус | Content-Type | ETag | Cache-Control | Байт |
|---|---|---|---|---|---|
| без `size` | 200 | image/jpeg | `"01a1ca13…68e"` | `public, max-age=0, must-revalidate` | 182 568 |
| `?size=sm` | 200 | image/webp | `"01a1ca13…68e-sm"` | `public, max-age=0, must-revalidate` | 5 310 |
| `?size=md` | 200 | image/webp | `"01a1ca13…68e-md"` | `public, max-age=0, must-revalidate` | 16 650 |
| `?size=lg` | 200 | image/webp | `"01a1ca13…68e-lg"` | `public, max-age=0, must-revalidate` | 40 192 |
| `?size=xx` | 404 | application/problem+json | — | `no-store` | `portfolioMedia.notFound` |
| `?size=sm&size=md` | 404 | application/problem+json | — | `no-store` | то же |

```
файлы рядом с исходником после ленивой генерации:
01a1ca13….jpg   182 568
01a1ca13…-sm.webp  5 310
01a1ca13…-md.webp 16 650
01a1ca13…-lg.webp 40 192
```

Условный запрос (совпадающий ETag) и чужой ETag:

```
If-None-Match: "01a1ca13…68e-md" → 304, тело 0 B, ETag и Cache-Control на месте
If-None-Match: "01a1ca13…68e-sm" → 200, image/webp, 16 650 B
If-None-Match: *                 → 304
Range: bytes=0-9                 → 206 Partial Content, Accept-Ranges: bytes
```

Скрытый кадр (`is_blocked = true`) анониму: `404`, `Cache-Control: no-store`,
`errorCodes: ["portfolioMedia.notFound"]`; `nosniff` и `Accept-Ranges` — у `200`, `Vary` в ответах
раздачи нет.

### Frontend

```
> npm run lint        → Found 0 warnings and 0 errors. (510 files, 116 rules)
> npm run build       → ✓ 2589 modules transformed
                        index 353.11 kB / gzip 114.49 (было 352.59 / 114.28 → +0.52 / +0.21)
                        admin-page 300.24 / 61.64 · barber-page 35.52 / 9.38
                        barber-profile-page 31.51 / 7.34 · account-profile-page 18.65 / 5.62
                        account-shell 4.72 / 1.96 · index.css 90.01 / 15.39 (без изменений)
                        суммарный прирост JS ≈ +0.37 kB gzip
> npm test            → Test Files 131 passed (131) / Tests 1382 passed (1382)
> npm run check:i18n  → EXIT=0; t() строк 1721, нет перевода 0, конфликтов 0, видимых в JSX 0
> npx tsc --noEmit -p tsconfig.e2e.json → exit 0
```

Новые фронтовые тесты: `src/lib/media.test.ts` (13 кейсов), расширенные
`barber-portfolio.test.tsx` (три дескриптора + `sizes` у сетки, исходник без `srcSet` в
лайтбоксе, `onError`), `barber-profile-photos.test.tsx`, `photos-screen.test.tsx` и тесты
аватаров (`?size=sm 1x`, `?size=md 2x`, `src` — исходник).

### E2E (Playwright, реальный API + PostgreSQL + Mailpit)

Полный прогон (4 воркера, база пересеяна `npm run test:e2e:seed`):
`304 passed, 6 skipped, 1 failed`; единственное падение — `public-barber-avatar-mutations.spec.ts`
(«На проверке» осталось в админской карточке), сцена вне среза и разная в каждом прогоне.
Все сцены обоих media-спеков зелёные:

```
ok 292 barber-profile-photos-mutations › барбер загружает фото: «На проверке», публичной галереи нет
ok 296 barber-profile-photos-mutations › очередь портфолио: поиск по имени барбера и «Одобрить»
ok 300 barber-profile-photos-mutations › публичная галерея: лайтбокс, края, Escape и возврат фокуса
ok 304 barber-profile-photos-mutations › 320 px: без горизонтального скролла, axe и лайтбокс
ok 308 barber-profile-photos-mutations › второй кадр отклонён с причиной: владелец видит её, публично — только первый
ok 310 barber-profile-photos-mutations › админ скрывает фото, публичная карточка теряет галерею, затем возвращает
ok 311 barber-profile-photos-mutations › барбер удаляет оба фото: пустое состояние и карточка без «Работы»
ok 268 account-avatar-mutations › загрузка: превью «На проверке», объявление, каркас и reload
ok 273 account-avatar-mutations › админская карточка пользователя видит тот же src и «На проверке»
ok 274 account-avatar-mutations › очередь аватаров: поиск по email, превью крупно и «Одобрить»
ok 281 account-avatar-mutations › админ скрывает фото: инициалы в кабинете и шапке, media — 404 и private
ok 287 account-avatar-mutations › админ возвращает фото: кадр и публичный кэш возвращаются
ok 290 account-avatar-mutations › 320 px: без горизонтального скролла, кнопки 44 px и axe
ok 292 account-avatar-mutations › очередь отклоняет новый кадр: причина у владельца и `Rejected` в users/me
ok 298 account-avatar-mutations › удаление через alertdialog возвращает инициалы и отдаёт 404
```

Что именно проверяют сцены: у первого `<img>` галереи `srcset` с `?size=sm 256w, ?size=md 512w,
?size=lg 1024w`; `?size=sm` отдаёт `image/webp` с `ETag` и `If-None-Match` → `304`; лайтбокс
грузит исходник; «Скрыть» (админ) → тот же `?size=`-URL отвечает `404` с `no-store`, галерея
исчезает; «Разблокировать» → снова `200` с `public, max-age=0, must-revalidate`. Аватар: в шапке
`srcset` `?size=sm 1x / ?size=md 2x`, `?size=sm` → `image/webp`, скрытие/замена/удаление гасят
старые `?size=`-URL, публичный кэш — новый.

Локальная среда flaky вне среза: в разных прогонах по одному падению в сценах почты (письмо не
успевает за 10 с: доставка 2–12 с под нагрузкой) или в админских счётчиках; media-спеки в этих же
прогонах стабильно зелёные.

**CI (`ubuntu-latest`, PR #90)** — это и есть проверка Linux-ассетов SkiaSharp:

```
Build, unit and integration tests   pass  3m22s
Lint and build                     pass  1m27s
Playwright and axe                 pass  6m26s
```

Полный E2E-прогон в CI зелёный: локальные падения почты/счётчиков были особенностью окружения,
а не среза.

Важная находка при написании E2E: `browser.newContext()` внутри файла с
`test.use({ storageState })` получает cookie владельца, поэтому «гостевой» контекст отдавал скрытый
кадр владельцу с `private, no-store` (200) и сцена ложно падала. Гостевые контексты в спеке теперь
создаются с явным `storageState: { cookies: [], origins: [] }`.

## Риски и замечания

- **Нативная зависимость.** `restore` теперь требует сети (в confined shell её нет), а для
  Linux нужен `SkiaSharp.NativeAssets.Linux.NoDependencies` — без него генерация упадёт в
  рантайме, а не на сборке. Пакет добавлен ровно в `BarberSearch.Web.csproj`.
- **Место на диске.** Варианты — это ещё ~35 % от исходника рядом с ним (182 568 → 64 452 B в
  трёх файлах). Ретеншн и очистка осиротевших файлов — отдельный долг, как и S3/MinIO.
- **Контракт ширин** продублирован в `PhotoVariantWidths` и `frontend/src/lib/media.ts`
  сознательно: фронтенд не запрашивает ширины у сервера. Оба файла ссылаются друг на друга.
- **Ленивая генерация** держит один запрос до окончания декодирования: первый `?size=`-запрос на
  старом файле — это декод (под семафором на 2). Дальше файл лежит на диске.
- **E2E-флаки вне среза**: три сценария почты (`email-verification`, `email-change`,
  `password-reset`) в этом локальном окружении иногда не успевают получить письмо за 10 с —
  outbox-доставка под нагрузкой идёт 2–12 с даже с `EmailOutbox__IntervalSeconds=1`. Срез их не
  трогает; на `ubuntu-latest` в CI те же настройки дают зелёный прогон.
