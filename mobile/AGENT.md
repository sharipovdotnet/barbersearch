# AGENT.md — BarberSearch (mobile)

> Контекст для агентов и разработчиков. Читать вместе с `README.md` рядом и с
> `backend/AGENT.md` (§7 — домен, §16 — API, §17 — авторизация).

---

**Содержание:** [§1. Что это](#1-что-это) · [§2. Где что лежит](#2-где-что-лежит) ·
[§3. Стек](#3-стек) · [§4. Как собирать и проверять](#4-как-собирать-и-проверять) ·
[§5. Решения](#5-решения-принятые-и-почему) · [§6. Чего ещё нет](#6-чего-ещё-нет-в-бэкенде) ·
[§7. Правила](#7-правила-работы)

---

## 1. Что это

Клиентское мобильное приложение BarberSearch для iOS и Android на .NET MAUI (.NET 10).
Пользователь ищет барбера, смотрит карточку и свободные слоты, записывается, видит свои записи.

Кабинет барбера и админка живут в вебе (`frontend/`). В мобилке их не будет: расписание и
график с телефона не заполняют.

## 2. Где что лежит

| Что | Путь |
|---|---|
| Решение | `mobile/BarberSearch.Mobile.sln` |
| Приложение (MAUI) | `mobile/src/BarberSearch.Mobile` |
| Ядро без MAUI (HTTP-клиент, ViewModel) | `mobile/src/BarberSearch.Mobile.Core` |
| Тесты ядра | `mobile/tests/BarberSearch.Mobile.Core.Tests` |
| Контракты API (ссылка на проект) | `backend/BarberSearch/src/BarberSearch.Contracts` |
| Git-корень | `D:\Projects\barbersearch` — мобилка внутри репозитория, не рядом |

## 3. Стек

| Компонент | Версия / значение |
|---|---|
| TFM | `net10.0-android`, `net10.0-ios` (ядро и тесты — `net10.0`) |
| SDK | `10.0.303` (`global.json`) |
| MAUI | workload из Visual Studio 18.8 (`Microsoft.Maui.Controls` 10.0.20) |
| MVVM | CommunityToolkit.Mvvm 8.4.0, `[ObservableProperty]` на полях |
| Android | `SupportedOSPlatformVersion` 24, `ApplicationId` `ru.barbersearch.mobile` |
| iOS | `SupportedOSPlatformVersion` 15.0 |

## 4. Как собирать и проверять

```powershell
cd mobile
dotnet test tests/BarberSearch.Mobile.Core.Tests                       # ядро без эмулятора
dotnet build src/BarberSearch.Mobile/BarberSearch.Mobile.csproj -f net10.0-android
dotnet build src/BarberSearch.Mobile/BarberSearch.Mobile.csproj -f net10.0-ios   # только macOS
```

На Windows собирается только Android: iOS-цель требует macOS с Xcode 26. Решение целиком
(`dotnet build BarberSearch.Mobile.sln`) на Windows падает на iOS — это ожидаемо, собирайте
проект приложения с `-f net10.0-android`.

Перед запуском нужен локальный бэкенд (`backend/docker compose up -d` +
`dotnet run --project BarberSearch/src/BarberSearch.Web`). Эмулятор Android видит хост как
`10.0.2.2:5243`, симулятор iOS — как `localhost:5243`.

## 5. Решения, принятые и почему

- **MAUI, а не Avalonia/Uno/Blazor Hybrid.** Только iOS и Android, стандартный дизайн и
  требование фоновых фич (геозоны, пуши) — нативные контролы и first-party API выигрывают.
- **Ядро отдельным проектом.** ViewModel и HTTP-клиент проверяются xunit без эмулятора;
  MAUI-зависимости остаются в приложении.
- **Контракты ссылкой на проект, а не копией.** Расхождение API ломает сборку сразу.
- **Путь в репозитории.** `mobile/` рядом с `backend/` и `frontend/`: один git-корень, один CI,
  `ProjectReference` на контракты работает и локально, и в GitHub Actions.
- **`NuGet.config` внутри `mobile/`.** В глобальном конфиге машины включён `packageSourceMapping`
  без MAUI-пакетов, из-за чего restore падал с NU1100. Локальный конфиг ограничен подкаталогом.
- **`ApplicationId` `ru.barbersearch.mobile`** — менять до первой публикации; после выпуска в
  сторах его сменить нельзя.

## 6. Чего ещё нет в бэкенде

Мобильный MVP упирается в три доработки на стороне `backend/`, которых пока нет:

1. **Гео-поиск.** Координаты у `Salon` есть, но публичный каталог их не отдаёт, поиск
   фильтрует только по городу. Нужен эндпоинт точек в радиусе/bbox для экрана карты.
2. **Чтение избранного.** Есть `POST`/`DELETE /api/users/{id}/favorites/{barberProfileId}`,
   нет `GET` списка.
3. **Пуши.** Инфраструктуры нет вовсе: ни таблицы устройств/токенов, ни FCM/APNs, ни
   клиентских уведомлений. Напоминания сейчас — Telegram-бот для барберов и email-outbox.

До этого экраны «Карта», «Избранное» и пуши делать не на чем.

## 7. Правила работы

- Комментарии и XML-доки — по-русски, как в `backend/`. Публичные типы описываются: почему
  так, а не пересказ имени.
- Любое предупреждение — ошибка сборки (`TreatWarningsAsErrors`). Глушить правило — только
  точечно в `.editorconfig` с обоснованием.
- Имена тестов — `Method_Scenario_Expected`; CA1707 для них отключён.
- Новые экраны: ViewModel в `BarberSearch.Mobile.Core`, страница — в приложении, зависимости
  через конструктор, регистрация в `MauiProgram`.
- `dotnet sln add` в `backend/` запрещён (backend/AGENT.md §5.1); в `mobile/` решение ведётся
  обычным `dotnet sln`.
