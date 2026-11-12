# Issue 92 — S3/MinIO как второе хранилище фото и аватаров (BarberSearch)

Ветка `features/issue-92`, один PR в `main` (только backend и инфраструктура; фронтенд не
менялся). Срез закрывает долги #31/#32 в части «файлы на одном диске одного инстанса»:
провайдер хранилища выбирается конфигурацией, а `Storage:Provider=S3` работает против MinIO.

Дата прогонов: 2026-11-12. Миграций нет: `dotnet ef migrations has-pending-model-changes` чист,
БД, контракты и публичные URL не менялись.

## Что сделано

### Backend

- **Настройки.** `PhotoStorageProvider` (`Local`/`S3`), `S3StorageOptions`,
  `PhotoStorageOptions.FromConfiguration`: провайдер читается строкой (значение `1` не станет
  `S3`), неизвестное значение и неполная S3-конфигурация роняют старт с перечнем допустимого,
  значения секретов в тексты не попадают. Для S3 каталог `App_Data` не создаётся.
- **Раскладка.** `PhotoMediaKeys` — единственное место с префиксами и именами:
  `portfolio/{profileId:N}/{mediaId:N}.{ext}`, `avatars/{userId:N}/{mediaId:N}.{ext}`,
  варианты `{mediaId:N}-{sm|md|lg}.webp`. `PhotoContentTypes` — MIME по расширению.
  `LocalPhotoStorage` переведён на них без изменения поведения.
- **Шов.** `IPhotoObjectStore`/`PhotoObject`: `GetAsync` (null = 404), `PutAsync`, `DeleteAsync`;
  ошибка хранилища — `IOException` без ключа, бакета, эндпоинта и секретов, отмена
  пробрасывается. Unit-тесты работают на `FakePhotoObjectStore` в памяти.
- **S3-адаптер.** `S3PhotoStorage` — зеркало локального: исходник одним `PUT`, best-effort
  варианты через тот же `IPhotoThumbnailer`, ленивая генерация на первом `?size=`, фолбэк на
  исходник, удаление исходника и трёх точных ключей. `S3ObjectStore` прячет `AWSSDK.S3` 4.1.0
  (`ServiceURL` + `ForcePathStyle=true` для MinIO) и переводит ошибки SDK/транспорта в
  `IOException`. `ETag` остаётся производным от `mediaId`.
- **DI и старт.** `AddPhotoStorage` выбирает провайдера; для S3 регистрирует клиент, шов,
  адаптер и `S3StorageInitializer`, который в `StartAsync` проверяет бакет и при
  `EnsureBucket=true` создаёт его. Недоступный бакет роняет старт.
- **Инфраструктура.** `docker-compose.yml`: сервис `minio` (9000/9001, том
  `barbersearch-minio-data`), `backend/.env.example` — `MINIO_ROOT_*` и набор `Storage__S3__*`,
  `frontend-e2e.yml` — запуск MinIO, `Wait for MinIO` и API с `Storage__Provider=S3`,
  `Dockerfile` — каталог `/app/App_Data/photos` принадлежит `$APP_UID` (иначе том для Local
  не проходит write-probe).
- **Документация.** `backend/AGENT.md` (§4, §5, §8, §9.14, §11.12, §15.2, §17.15),
  `frontend/AGENT.md` (§14 #31/#32, §E2E), `frontend/README.md` (фото/аватары, E2E).

### Ключи в бакете (фактический листинг MinIO)

Загрузка аватара через API (`POST /api/users/me/avatar`), исходник — PNG 101 467 B:

```
avatars/9e000000000040008000000000000002/01a1d0a765707ab69b9f5fbc0186aafb.png      99KiB
avatars/9e000000000040008000000000000002/01a1d0a765707ab69b9f5fbc0186aafb-sm.webp  1.4KiB
avatars/9e000000000040008000000000000002/01a1d0a765707ab69b9f5fbc0186aafb-md.webp  2.8KiB
avatars/9e000000000040008000000000000002/01a1d0a765707ab69b9f5fbc0186aafb-lg.webp  5.5KiB
```

Загрузка фотографии портфолио (`POST /api/barber-profiles/{id}/photos/upload`), PNG 101 467 B,
800×600 (вариант `lg` не создаётся — апскейла нет):

```
portfolio/9e100000000040008000000000000001/01a1d0a909a47a22808cf383d959b355.png      99KiB
portfolio/9e100000000040008000000000000001/01a1d0a909a47a22808cf383d959b355-sm.webp  3.5KiB
portfolio/9e100000000040008000000000000001/01a1d0a909a47a22808cf383d959b355-md.webp  6.8KiB
```

После `DELETE` обоих кадров листинг бакета пуст, а медиа-URL отвечает `404` — исходник и
варианты удалены точными ключами, без `ListObjects`.

### Заголовки раздачи (curl, API на MinIO)

Владелец, непубличный (Pending) кадр, `Cache-Control: private, no-store`:

```
HTTP/1.1 200 OK
Content-Length: 101467
Content-Type: image/png
Cache-Control: private, no-store
ETag: "01a1d0a765707ab69b9f5fbc0186aafb"
X-Content-Type-Options: nosniff
```

Публичный (Approved) кадр и его вариант:

```
HTTP/1.1 200 OK
Content-Length: 101467
Content-Type: image/png
Cache-Control: public, max-age=0, must-revalidate
ETag: "01a1d0a765707ab69b9f5fbc0186aafb"
X-Content-Type-Options: nosniff

HTTP/1.1 200 OK                     # ?size=md
Content-Length: 2856
Content-Type: image/webp
ETag: "01a1d0a765707ab69b9f5fbc0186aafb-md"

HTTP/1.1 304 Not Modified           # If-None-Match: "<mediaId>-md"
Cache-Control: public, max-age=0, must-revalidate
ETag: "01a1d0a909a47a22808cf383d959b355-md"

HTTP/1.1 206 Partial Content        # Range: bytes=0-99
Content-Range: bytes 0-99/2856
```

Гость на скрытый/неодобренный кадр получает `404` с `Cache-Control: no-store`; чужой и внешний
URL дают тот же `404` (`portfolioMedia.notFound` / `avatarMedia.notFound`) без обращения к
хранилищу.

После рестарта API оба кадра отдаются по прежним URL и ETag; второй инстанс API на другом порту
против того же бакета отдаёт те же байты — файлы не привязаны к процессу.

### Проверка конфигурации

```
Storage:Provider 'minio' is not supported. Allowed values: 'Local' and 'S3'.
Storage:Provider=S3 requires Storage:S3:Bucket, Storage:S3:AccessKey, Storage:S3:SecretKey. ...
Storage:Provider=S3 is selected, but the object storage is unavailable: the configured bucket
could not be checked or created. ... the values are not printed.
```

Все три случая роняют именно старт; в тексте нет ни адреса, ни имени бакета, ни ключей.

## Прогоны

### Backend

```text
dotnet build BarberSearch.sln --nologo                    0 warnings / 0 errors
BarberSearch.Domain.UnitTests                            588 passed
BarberSearch.Application.UnitTests                       878 passed
BarberSearch.Infrastructure.UnitTests                    344 passed  (+27: S3PhotoStorageTests, PhotoStorageOptionsTests)
BarberSearch.IntegrationTests (Testcontainers Postgres)  292 passed  (Local, без правок сценариев)
итого                                                    2102 passed / 0 failed
dotnet ef migrations has-pending-model-changes           No changes have been made to the model
```

### Frontend (регресс; код не менялся)

```text
npm run lint          0
npm test              1394 passed в 131 файле
npm run build         ok (бандл не вырос: index 353.11 kB / gzip 114.50 kB)
npm run check:i18n    0
npx tsc -p tsconfig.e2e.json --noEmit  0
```

### E2E

Полный прогон локально не запускался: порт `5243` занят другим экземпляром API общего dev-
окружения, а подменять его в чужой сессии нельзя. Обязательный CI-workflow `frontend-e2e`
переключён на MinIO (§7.3), поэтому media-сценарии (`account-avatar-mutations`,
`barber-profile-photos-mutations`, `public-barber-avatar-mutations`) проверяют S3-ветку
в CI. Ручной HTTP-сценарий §11.4 пройден полностью против MinIO — он и есть доказательство
для этого окружения.

## Что не проверено и почему

- **Настоящий AWS S3.** Проверялся MinIO (S3-совместимый API); различия, которые могут
  проявиться на AWS: IAM-политики, регион бакета и поведение `HeadBucket`/`PutBucket`.
  Конфигурация для AWS S3 отличается только `ServiceUrl`/`Region`/ключами.
- **Перенос уже загруженных файлов.** В коде не автоматизируется по решению среза:
  операторская операция `mc mirror` / `aws s3 sync` описана в `backend/AGENT.md` §17.15.
- **Нагрузка и память.** Чтение буферизует объект (≤ 5 МиБ) ради `Range`/`304`; отдельного
  нагрузочного прогона S3-провайдера не делалось.

## Отклонения от исходного документа (зафиксированы в PR)

1. **Образ MinIO.** Официальный `minio/minio` удалён из Docker Hub, а `quay.io/minio/minio` с
   2026-09-24 не отдаётся анонимно. В compose и CI используется публичное зеркало
   `coollabsio/minio:RELEASE.2025-10-15T17-29-55Z` с пин-тегом релиза; S3 API и консоль те же.
2. **`Dockerfile`.** Добавлен каталог `/app/App_Data/photos` с владельцем `$APP_UID`: без него
   документированный запуск Local с томом не проходит write-probe у непривилегированного
   пользователя. S3-режиму каталог не нужен.
3. **CI.** MinIO в `frontend-e2e.yml` запускается шагом `docker run`, а не сервис-контейнером:
   у образа нет команды по умолчанию, а GitHub Actions не позволяет переопределить `command`
   в `services`. Добавлен отдельный шаг `Wait for MinIO` перед стартом API.
