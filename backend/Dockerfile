# syntax=docker/dockerfile:1
#
# Образ API BarberSearch. Контекст сборки — каталог backend/:
#   docker build -t barbersearch-api:local -f Dockerfile .
#
# Многостадийная сборка: SDK-образ нужен только для restore/publish, в рантайм
# попадает aspnet-образ без компилятора и NuGet-кэша. Приложение запускается
# от непривилегированного пользователя ($APP_UID из базового образа).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Общие настройки сборки и версия SDK — те же, что локально и в CI.
# .editorconfig обязателен: в нём лежат severity анализаторов, без него publish
# падает на TreatWarningsAsErrors.
COPY global.json Directory.Build.props .editorconfig ./

# Сначала только csproj: слой restore переиспользуется, пока не изменились зависимости.
COPY BarberSearch/src/BarberSearch.Web/BarberSearch.Web.csproj BarberSearch/src/BarberSearch.Web/
COPY BarberSearch/src/BarberSearch.Application/BarberSearch.Application.csproj BarberSearch/src/BarberSearch.Application/
COPY BarberSearch/src/BarberSearch.Infrastructure.Postgresql/BarberSearch.Infrastructure.Postgresql.csproj BarberSearch/src/BarberSearch.Infrastructure.Postgresql/
COPY BarberSearch/src/BarberSearch.Domain/BarberSearch.Domain.csproj BarberSearch/src/BarberSearch.Domain/
COPY BarberSearch/src/BarberSearch.Contracts/BarberSearch.Contracts.csproj BarberSearch/src/BarberSearch.Contracts/
COPY BarberSearch/src/Shared/Shared.csproj BarberSearch/src/Shared/
RUN dotnet restore BarberSearch/src/BarberSearch.Web/BarberSearch.Web.csproj

# Исходники и publish в Release. UseAppHost=false — в образе только dll,
# точка входа задаётся ENTRYPOINT ["dotnet", ...].
COPY BarberSearch/src/ BarberSearch/src/
RUN dotnet publish BarberSearch/src/BarberSearch.Web/BarberSearch.Web.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Порт 8080 — тот же, что в DEPLOY.md; ASPNETCORE_URLS можно переопределить при запуске.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

# Каталог журналов: приложение пишет сюда по Logging:File:Path (logs/barbersearch-.log).
# Каталог создаётся заранее и принадлежит непривилегированному пользователю, иначе запись
# в него невозможна. Подключите том (-v barbersearch-logs:/app/logs), чтобы журнал пережил
# контейнер, и при нескольких инстансах на одном томе включите Logging__File__Shared=true.
RUN mkdir -p /app/logs && chown $APP_UID /app/logs

# Каталог фото для локального провайдера (Storage:Provider=Local). Том монтируется сюда,
# поэтому каталог создаётся в образе и принадлежит непривилегированному пользователю: иначе
# write-probe на старте не прошёл бы, а точка монтирования осталась бы root-owned. При
# Storage:Provider=S3 каталог не используется, но безвреден.
RUN mkdir -p /app/App_Data/photos && chown -R $APP_UID /app/App_Data

# Непривилегированный пользователь, предопределённый в официальном aspnet-образе.
USER $APP_UID

ENTRYPOINT ["dotnet", "BarberSearch.Web.dll"]
