using System.Net;
using BarberSearch.Contracts.BarberProfiles.GetBarberProfile;
using BarberSearch.Contracts.Specializations.GetSpecializations;
using BarberSearch.Contracts.Users.Me;
using BarberSearch.Domain.Common;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.Media;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки раздела «Профиль» кабинета: карточка, баннер проверки, прайс, специализации,
/// ленивый справочник, автоповтор чтения и срез аватара.
/// </summary>
/// <remarks>
/// Фотографии в этом срезе только читаются, поэтому проверяется их состояние, а не команды.
/// Записи (скрытие услуги, специализация, аватар) не повторяются: после сбоя исход неизвестен.
/// Аватар читается из профиля аккаунта: карточка отдаёт только одобренный и не скрытый кадр,
/// а «На проверке», отказ и блокировку даёт <c>GET api/users/me</c>.
/// </remarks>
public sealed class BarberCabinetProfileViewModelTests
{
    private const string NetworkText =
        "Нет связи с сервером. Проверьте подключение и повторите попытку.";

    private const string CatalogFailedText =
        "Не удалось загрузить справочник специализаций.";

    [Fact]
    public async Task ActivateAsync_LoadsAndFormatsCardFields()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Profile.OwnCalls);
        Assert.True(viewModel.IsReady);
        Assert.False(viewModel.IsFirstLoading);
        Assert.False(viewModel.IsRefreshing);
        Assert.Equal("Тимур Мастеров", viewModel.DisplayName);
        Assert.Equal("ТМ", viewModel.Initials);
        Assert.Equal("Старший мастер", viewModel.GradeText);
        Assert.Equal("Стригу с 2014 года.", viewModel.Bio);
        Assert.Equal("8 лет", viewModel.ExperienceText);
        Assert.Equal("4,8", viewModel.RatingText);
        Assert.Equal("12 отзывов", viewModel.ReviewsText);
        Assert.Equal("Русский, English", viewModel.LanguagesText);
        Assert.Equal("Наличные, СБП", viewModel.PaymentMethodsText);
        Assert.Equal("Опаздывающих жду 10 минут.", viewModel.LatePolicyText);
        Assert.StartsWith("Обновлено в ", viewModel.UpdatedAtText, StringComparison.Ordinal);
        Assert.True(viewModel.IsVerified);
        Assert.True(viewModel.IsActive);
    }

    [Fact]
    public async Task ActivateAsync_Unverified_ShowsHintAndBlocksPreview()
    {
        var harness = new Harness();
        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(isVerified: false));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.False(viewModel.IsVerified);
        Assert.Equal(
            "Профиль на проверке. Заполняйте карточку — она появится в поиске после подтверждения администратором.",
            viewModel.VerificationHint);
        Assert.False(viewModel.CanPreviewPublic);
    }

    [Fact]
    public async Task ActivateAsync_VerifiedAndActive_AllowsPreviewWithoutHint()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        Assert.True(harness.ViewModel.CanPreviewPublic);
        Assert.Equal(string.Empty, harness.ViewModel.VerificationHint);
    }

    [Fact]
    public async Task ActivateAsync_LoadsHiddenServiceAndPhotoStates()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            services:
            [
                BarberCabinetFixtures.Service(),
                BarberCabinetFixtures.Service(1800m, 45) with { IsActive = false },
            ],
            specializations: [BarberCabinetData.ProfileSpecialization()],
            photos:
            [
                BarberCabinetData.Photo("Работа"),
                BarberCabinetData.Photo(
                    isBlocked: true,
                    status: PhotoModerationStatus.Pending),
            ]));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(2, viewModel.Services.Count);
        Assert.True(viewModel.Services[0].IsActive);
        Assert.True(viewModel.Services[1].IsHidden);
        Assert.Equal("Скрыта", viewModel.Services[1].StatusText);

        // Строка прайса помнит специализацию из ответа: по ней собирается заголовок группы.
        Assert.Equal(
            BarberCabinetFixtures.ServiceSpecializationName,
            viewModel.Services[0].SpecializationName);

        // Строка прайса начинается с маркера «• », а чистое имя остаётся для правки.
        Assert.Equal("• Стрижка", viewModel.Services[0].DisplayName);
        Assert.Equal("Стрижка", viewModel.Services[0].Name);

        Assert.True(viewModel.HasServices);

        Assert.Single(viewModel.Specializations);
        Assert.Equal("Фейд", viewModel.Specializations[0].Name);

        Assert.Equal(2, viewModel.Photos.Count);
        Assert.Equal("Опубликовано", viewModel.Photos[0].StatusText);
        Assert.Equal("Скрыто администратором", viewModel.Photos[1].StatusText);
        Assert.True(viewModel.HasPhotos);
    }

    [Fact]
    public async Task ActivateAsync_Services_BuildsServiceGroupsInApiOrderIncludingHidden()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(services:
        [
            CabinetService("Балаяж", "Окрашивание", isActive: false),
            CabinetService("Фейд классический", "Фейд"),
            CabinetService("Стрижка машинкой", "Стрижка"),
            CabinetService("Фейд с выбриванием", "Фейд"),
        ]));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        // Группы идут в порядке ответа API; кабинет не пересортировывает прайс.
        Assert.Equal(
            ["Окрашивание", "Фейд", "Стрижка"],
            viewModel.ServiceGroups.Select(group => group.Title));

        // Скрытая услуга остаётся в своей группе: владелец видит прайс целиком.
        var hidden = Assert.Single(viewModel.ServiceGroups[0].Services);
        Assert.True(hidden.IsHidden);
        Assert.Equal("Балаяж", hidden.Name);
        Assert.Equal("Балаяж", viewModel.Services[0].Name);

        // Чередование API не разрывает группу и не меняет порядок строк в ней.
        Assert.Equal(
            ["Фейд классический", "Фейд с выбриванием"],
            viewModel.ServiceGroups[1].Services.Select(service => service.Name));
    }

    [Fact]
    public async Task ActivateAsync_ServiceWithoutSpecialization_BuildsOtherServicesGroup()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(services:
        [
            CabinetService("Стрижка наголо", " "),
            CabinetService("Фейд классический", "Фейд"),
        ]));

        await harness.ViewModel.ActivateAsync();

        // Строка без специализации не теряется: она замыкает список «Прочими услугами».
        Assert.Equal(
            ["Фейд", "Прочие услуги"],
            harness.ViewModel.ServiceGroups.Select(group => group.Title));
        Assert.Equal(
            ["Стрижка наголо"],
            harness.ViewModel.ServiceGroups[1].Services.Select(service => service.Name));
    }

    [Fact]
    public async Task ActivateAsync_Reload_RebuildsServiceGroups()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(services:
        [
            CabinetService("Фейд классический", "Фейд"),
        ]));

        await harness.ViewModel.ActivateAsync();

        Assert.Single(harness.ViewModel.ServiceGroups);

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(services:
        [
            CabinetService("Фейд классический", "Фейд"),
            CabinetService("Стрижка машинкой", "Стрижка"),
        ]));

        await harness.ViewModel.ActivateAsync();

        // Возврат в раздел собирает группы заново, а не дописывает новые к прежним.
        Assert.Equal(
            ["Фейд", "Стрижка"],
            harness.ViewModel.ServiceGroups.Select(group => group.Title));
    }

    [Fact]
    public async Task HideServiceAsync_SendsDeleteAndReloadsCard()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;
        var item = viewModel.Services[0];

        await viewModel.HideServiceCommand.ExecuteAsync(item);

        var removal = Assert.Single(harness.Profile.ServiceRemovals);

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, removal.ProfileId);
        Assert.Equal(BarberCabinetFixtures.HairServiceId, removal.HairServiceId);
        Assert.Null(viewModel.ActionError);
        Assert.False(viewModel.IsMutating);
        Assert.Equal(2, harness.Profile.OwnCalls);
    }

    [Fact]
    public async Task RestoreServiceAsync_SendsCurrentPriceAndDuration()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            services: [BarberCabinetFixtures.Service(1800m, 45) with { IsActive = false }]));

        await viewModel.ActivateAsync();

        var hidden = viewModel.Services[0];

        await viewModel.RestoreServiceCommand.ExecuteAsync(hidden);

        var restore = Assert.Single(harness.Profile.ServiceAdds);

        Assert.Equal(BarberCabinetFixtures.HairServiceId, restore.Request.HairServiceId);
        Assert.Equal(1800m, restore.Request.Price);
        Assert.Equal(45, restore.Request.DurationMinutes);
    }

    [Fact]
    public async Task HideServiceAsync_LastActiveService_RemovesSpecialization()
    {
        var harness = new Harness();
        var state = BarberCabinetData.Profile(
            services: [BarberCabinetFixtures.Service()],
            specializations:
            [
                BarberCabinetData.ProfileSpecialization(
                    BarberCabinetFixtures.ServiceSpecializationId),
            ]);

        harness.Profile.Own = _ => Task.FromResult(state);
        harness.Profile.RemoveService = (_, _) =>
        {
            state = state with
            {
                Services = [BarberCabinetFixtures.Service() with { IsActive = false }],
            };

            return Task.CompletedTask;
        };
        harness.Profile.RemoveSpecialization = (_, specializationId) =>
        {
            state = state with
            {
                Specializations = state.Specializations
                    .Where(item => item.SpecializationId != specializationId)
                    .ToArray(),
            };

            return Task.CompletedTask;
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Single(viewModel.Specializations);

        await viewModel.HideServiceCommand.ExecuteAsync(viewModel.Services[0]);

        var removal = Assert.Single(harness.Profile.SpecializationRemovals);

        Assert.Equal(BarberCabinetFixtures.ServiceSpecializationId, removal.SpecializationId);
        Assert.Empty(viewModel.Specializations);
        Assert.Null(viewModel.ActionError);
        Assert.False(viewModel.IsMutating);

        // Первое чтение, карточка после скрытия и карточка после снятия тега.
        Assert.Equal(3, harness.Profile.OwnCalls);
    }

    [Fact]
    public async Task HideServiceAsync_OtherActiveServiceKeepsSpecialization()
    {
        var harness = new Harness();
        var state = BarberCabinetData.Profile(
            services:
            [
                ServiceFor(BarberCabinetFixtures.HairServiceId, BarberCabinetFixtures.ServiceSpecializationId),
                ServiceFor(BarberCabinetData.SecondHairServiceId, BarberCabinetFixtures.ServiceSpecializationId),
            ],
            specializations:
            [
                BarberCabinetData.ProfileSpecialization(
                    BarberCabinetFixtures.ServiceSpecializationId),
            ]);

        harness.Profile.Own = _ => Task.FromResult(state);
        harness.Profile.RemoveService = (_, hairServiceId) =>
        {
            state = state with
            {
                Services = state.Services
                    .Select(service => service.HairServiceId == hairServiceId
                        ? service with { IsActive = false }
                        : service)
                    .ToArray(),
            };

            return Task.CompletedTask;
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;
        var item = viewModel.Services.First(
            service => service.HairServiceId == BarberCabinetFixtures.HairServiceId);

        await viewModel.HideServiceCommand.ExecuteAsync(item);

        // Вторая активная услуга того же направления держит специализацию.
        Assert.Empty(harness.Profile.SpecializationRemovals);
        Assert.Single(viewModel.Specializations);
        Assert.Equal(2, harness.Profile.OwnCalls);
    }

    [Fact]
    public async Task RestoreServiceAsync_MissingSpecialization_AddsItBack()
    {
        var harness = new Harness();
        var state = BarberCabinetData.Profile(
            services:
            [
                ServiceFor(
                    BarberCabinetFixtures.HairServiceId,
                    BarberCabinetFixtures.ServiceSpecializationId,
                    isActive: false),
            ],
            specializations: []);

        harness.Profile.Own = _ => Task.FromResult(state);
        harness.Profile.AddService = (_, request) =>
        {
            state = state with
            {
                Services = state.Services
                    .Select(service => service.HairServiceId == request.HairServiceId
                        ? service with { IsActive = true }
                        : service)
                    .ToArray(),
            };

            return Task.CompletedTask;
        };
        harness.Profile.AddSpecialization = (_, specializationId) =>
        {
            state = state with
            {
                Specializations =
                [
                    .. state.Specializations,
                    new BarberProfileSpecializationResponse(
                        Guid.CreateVersion7(),
                        specializationId,
                        "Фейд",
                        null),
                ],
            };

            return Task.CompletedTask;
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Empty(viewModel.Specializations);

        await viewModel.RestoreServiceCommand.ExecuteAsync(viewModel.Services[0]);

        var addition = Assert.Single(harness.Profile.SpecializationAdds);

        Assert.Equal(BarberCabinetFixtures.ServiceSpecializationId, addition.SpecializationId);
        Assert.Single(viewModel.Specializations);
        Assert.Null(viewModel.ActionError);
        Assert.Equal(3, harness.Profile.OwnCalls);
    }

    [Fact]
    public async Task ActivateAfterServiceChangeAsync_MissingSpecialization_AddsIt()
    {
        var harness = new Harness();
        var state = BarberCabinetData.Profile(
            services: [BarberCabinetFixtures.Service()],
            specializations: []);

        harness.Profile.Own = _ => Task.FromResult(state);
        harness.Profile.AddSpecialization = (_, specializationId) =>
        {
            state = state with
            {
                Specializations =
                [
                    .. state.Specializations,
                    new BarberProfileSpecializationResponse(
                        Guid.CreateVersion7(),
                        specializationId,
                        "Фейд",
                        null),
                ],
            };

            return Task.CompletedTask;
        };

        await harness.ViewModel.ActivateAfterServiceChangeAsync();

        var addition = Assert.Single(harness.Profile.SpecializationAdds);

        Assert.Equal(BarberCabinetFixtures.ServiceSpecializationId, addition.SpecializationId);
        Assert.Single(harness.ViewModel.Specializations);
        Assert.Equal(2, harness.Profile.OwnCalls);
    }

    [Fact]
    public async Task HideServiceAsync_ManualSpecializationWithoutServices_IsKept()
    {
        var harness = new Harness();
        var state = BarberCabinetData.Profile(
            services: [BarberCabinetFixtures.Service()],
            specializations:
            [
                BarberCabinetData.ProfileSpecialization(
                    BarberCabinetFixtures.ServiceSpecializationId),
                BarberCabinetData.ProfileSpecialization(BarberCabinetData.SpecializationId, "Борода"),
            ]);

        harness.Profile.Own = _ => Task.FromResult(state);
        harness.Profile.RemoveService = (_, _) =>
        {
            state = state with
            {
                Services = [BarberCabinetFixtures.Service() with { IsActive = false }],
            };

            return Task.CompletedTask;
        };
        harness.Profile.RemoveSpecialization = (_, specializationId) =>
        {
            state = state with
            {
                Specializations = state.Specializations
                    .Where(item => item.SpecializationId != specializationId)
                    .ToArray(),
            };

            return Task.CompletedTask;
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.HideServiceCommand.ExecuteAsync(viewModel.Services[0]);

        // Тег «Борода» добавлен вручную и услуг не имеет: сверка его не трогает.
        Assert.Equal(
            BarberCabinetData.SpecializationId,
            Assert.Single(viewModel.Specializations).SpecializationId);
        Assert.Equal(
            BarberCabinetFixtures.ServiceSpecializationId,
            Assert.Single(harness.Profile.SpecializationRemovals).SpecializationId);
    }

    [Fact]
    public async Task HideServiceAsync_SyncFailure_ShowsActionErrorAndKeepsServerState()
    {
        var harness = new Harness();
        var state = BarberCabinetData.Profile(
            services: [BarberCabinetFixtures.Service()],
            specializations:
            [
                BarberCabinetData.ProfileSpecialization(
                    BarberCabinetFixtures.ServiceSpecializationId),
            ]);

        harness.Profile.Own = _ => Task.FromResult(state);
        harness.Profile.RemoveService = (_, _) =>
        {
            state = state with
            {
                Services = [BarberCabinetFixtures.Service() with { IsActive = false }],
            };

            return Task.CompletedTask;
        };
        harness.Profile.RemoveSpecialization = (_, _) =>
            Task.FromException(new HttpRequestException("offline"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.HideServiceCommand.ExecuteAsync(viewModel.Services[0]);

        // Услуга уже скрыта: сбой сверки не отменяет действие, но и не молчит.
        Assert.Equal(
            "Не удалось обновить специализации. Попробуйте ещё раз.",
            viewModel.ActionError);
        Assert.True(viewModel.Services[0].IsHidden);
        Assert.Single(viewModel.Specializations);
        Assert.False(viewModel.IsMutating);
    }

    [Fact]
    public async Task RestoreServiceAsync_DuplicateSpecialization_IsNotAnError()
    {
        var harness = new Harness();
        var state = BarberCabinetData.Profile(
            services:
            [
                ServiceFor(
                    BarberCabinetFixtures.HairServiceId,
                    BarberCabinetFixtures.ServiceSpecializationId,
                    isActive: false),
            ],
            specializations: []);

        harness.Profile.Own = _ => Task.FromResult(state);
        harness.Profile.AddService = (_, request) =>
        {
            state = state with
            {
                Services = state.Services
                    .Select(service => service.HairServiceId == request.HairServiceId
                        ? service with { IsActive = true }
                        : service)
                    .ToArray(),
            };

            return Task.CompletedTask;
        };
        harness.Profile.AddSpecialization = (_, specializationId) =>
        {
            // Тег уже добавлен с другого устройства: сервер отвечает duplicate, карточка его знает.
            state = state with
            {
                Specializations =
                [
                    .. state.Specializations,
                    new BarberProfileSpecializationResponse(
                        Guid.CreateVersion7(),
                        specializationId,
                        "Фейд",
                        null),
                ],
            };

            return Task.FromException(
                BarberCabinetApiError.Create(
                    HttpStatusCode.Conflict,
                    "barberProfile.addSpecialization.duplicate"));
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.RestoreServiceCommand.ExecuteAsync(viewModel.Services[0]);

        Assert.Null(viewModel.ActionError);
        Assert.Single(viewModel.Specializations);
    }

    [Fact]
    public async Task ActivateAsync_DoesNotSyncSpecializations()
    {
        var harness = new Harness();
        var state = BarberCabinetData.Profile(
            services: [BarberCabinetFixtures.Service() with { IsActive = false }],
            specializations:
            [
                BarberCabinetData.ProfileSpecialization(
                    BarberCabinetFixtures.ServiceSpecializationId),
            ]);

        harness.Profile.Own = _ => Task.FromResult(state);

        await harness.ViewModel.ActivateAsync();

        // Простое открытие раздела ничего не пишет: сверка — только после действий с услугой.
        Assert.Empty(harness.Profile.SpecializationRemovals);
        Assert.Single(harness.ViewModel.Specializations);
    }

    private static BarberProfileServiceResponse ServiceFor(
        Guid hairServiceId,
        Guid specializationId,
        bool isActive = true) =>
        BarberCabinetFixtures.Service() with
        {
            BarberServiceId = Guid.CreateVersion7(),
            HairServiceId = hairServiceId,
            IsActive = isActive,
            SpecializationId = specializationId,
        };

    [Fact]
    public async Task ActivateAsync_TransportFailure_RetriesUntilLoaded()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = new Harness(BarberCabinetRetry.Gated(gate.Task));
        var calls = 0;

        harness.Profile.Own = _ =>
        {
            calls++;

            return calls == 1
                ? Task.FromException<BarberProfileResponse>(new HttpRequestException("offline"))
                : Task.FromResult(BarberCabinetData.Profile());
        };

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(NetworkText, viewModel.ErrorMessage);
        Assert.True(viewModel.IsAutoRetrying);
        Assert.False(viewModel.IsReady);

        gate.SetResult();

        await viewModel.ReadRetryCompletion;

        Assert.Equal(2, calls);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.IsReady);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task ActivateAsync_Unauthorized_RequiresSignInWithoutRetry()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromException<BarberProfileResponse>(
            BarberCabinetApiError.Create(HttpStatusCode.Unauthorized, "auth.unauthorized"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsSignInRequired);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsReady);
        Assert.False(viewModel.IsAutoRetrying);
        Assert.Equal(0, harness.ViewModel.ReadAutoRetry.Attempts);
    }

    [Fact]
    public void Initialize_ExposesProfileIdForThePhotoPage()
    {
        var harness = new Harness();

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, harness.ViewModel.BarberProfileId);
    }

    [Fact]
    public async Task ActivateAsync_TenPhotos_ReachesLimitAndNotifies()
    {
        var harness = new Harness();
        var changed = new List<string?>();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            photos: Photos(PortfolioPhotoLimits.MaxPhotos)));

        harness.ViewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await harness.ViewModel.ActivateAsync();

        Assert.True(harness.ViewModel.IsPhotoLimitReached);
        Assert.False(harness.ViewModel.CanAddPhoto);
        Assert.Contains(nameof(BarberCabinetProfileViewModel.CanAddPhoto), changed);
        Assert.Contains(nameof(BarberCabinetProfileViewModel.IsPhotoLimitReached), changed);
    }

    [Fact]
    public async Task ActivateAsync_NinePhotos_AllowsAdding()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            photos: Photos(PortfolioPhotoLimits.MaxPhotos - 1)));

        await harness.ViewModel.ActivateAsync();

        Assert.False(harness.ViewModel.IsPhotoLimitReached);
        Assert.True(harness.ViewModel.CanAddPhoto);
    }

    [Fact]
    public async Task ActivateAsync_FilledPortfolio_ReevaluatesLimit()
    {
        var harness = new Harness();
        var changed = new List<string?>();

        harness.ViewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            photos: Photos(PortfolioPhotoLimits.MaxPhotos)));

        await harness.ViewModel.ActivateAsync();

        Assert.Contains(nameof(BarberCabinetProfileViewModel.IsPhotoLimitReached), changed);
    }

    [Fact]
    public async Task ActivateAsync_ApprovedAccountAvatar_ShowsPhotoAndOffersChange()
    {
        var harness = new Harness();

        harness.Avatar.MyProfile = _ => Task.FromResult(BarberCabinetData.AccountProfile(
            "/media/avatar.jpg",
            avatarStatus: PhotoModerationStatus.Approved));

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            avatarUrl: "/media/card-avatar.jpg"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal(1, harness.Avatar.ProfileCalls);
        Assert.Equal("/media/avatar.jpg", viewModel.AvatarUrl);
        Assert.True(viewModel.HasAvatar);
        Assert.False(viewModel.ShowInitials);
        Assert.False(viewModel.IsAvatarBlocked);
        Assert.False(viewModel.HasAvatarStatus);
        Assert.Null(viewModel.AvatarStatusText);
        Assert.Equal(string.Empty, viewModel.AvatarStatusKind);
        Assert.False(viewModel.HasAvatarRejection);
        Assert.Null(viewModel.AvatarRejectionText);
        Assert.True(viewModel.CanDeleteAvatar);
        Assert.True(viewModel.CanChangeAvatar);
        Assert.Equal("Изменить фото", viewModel.AvatarActionText);
    }

    [Fact]
    public async Task ActivateAsync_PendingAccountAvatar_ShowsRawUrlAndPendingBadge()
    {
        var harness = new Harness();

        harness.Avatar.MyProfile = _ => Task.FromResult(BarberCabinetData.AccountProfile(
            "/media/avatar-pending.jpg",
            avatarStatus: PhotoModerationStatus.Pending));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal("/media/avatar-pending.jpg", viewModel.AvatarUrl);
        Assert.True(viewModel.HasAvatar);
        Assert.False(viewModel.ShowInitials);
        Assert.True(viewModel.HasAvatarStatus);
        Assert.Equal("На проверке", viewModel.AvatarStatusText);
        Assert.Equal("pending", viewModel.AvatarStatusKind);
        Assert.False(viewModel.HasAvatarRejection);
        Assert.True(viewModel.CanDeleteAvatar);
        Assert.Equal("Изменить фото", viewModel.AvatarActionText);
    }

    [Fact]
    public async Task ActivateAsync_RejectedAccountAvatar_ShowsReasonAndOffersAnotherPhoto()
    {
        var harness = new Harness();

        harness.Avatar.MyProfile = _ => Task.FromResult(BarberCabinetData.AccountProfile(
            "/media/avatar-rejected.jpg",
            avatarStatus: PhotoModerationStatus.Rejected,
            rejectionReason: "Слишком размыто"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal("Отклонено", viewModel.AvatarStatusText);
        Assert.Equal("rejected", viewModel.AvatarStatusKind);
        Assert.True(viewModel.HasAvatarStatus);
        Assert.True(viewModel.HasAvatarRejection);

        var rejectionText = Assert.IsType<string>(viewModel.AvatarRejectionText);

        Assert.Contains("Слишком размыто", rejectionText, StringComparison.Ordinal);
        Assert.Equal("Загрузить другое фото", viewModel.AvatarActionText);
    }

    [Fact]
    public async Task ActivateAsync_BlockedAvatar_HidesPhotoButKeepsDeleteAvailable()
    {
        var harness = new Harness();

        harness.Avatar.MyProfile = _ => Task.FromResult(BarberCabinetData.AccountProfile(
            "/media/avatar-blocked.jpg",
            isAvatarBlocked: true));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal("/media/avatar-blocked.jpg", viewModel.AvatarUrl);
        Assert.True(viewModel.IsAvatarBlocked);
        Assert.False(viewModel.HasAvatar);
        Assert.True(viewModel.ShowInitials);
        Assert.True(viewModel.CanDeleteAvatar);
        Assert.False(viewModel.HasAvatarStatus);
        Assert.Equal("Загрузить другое фото", viewModel.AvatarActionText);
        Assert.Equal("Фото скрыто администратором", viewModel.AvatarBlockedText);
        Assert.Equal(
            "Его не видно публично. Новое фото появится публично после проверки, а вернуть старое может только администратор.",
            viewModel.AvatarBlockedHint);
    }

    [Fact]
    public async Task ActivateAsync_AccountProfileFailure_FallsBackToCardAvatar()
    {
        var harness = new Harness();

        harness.Profile.Own = _ => Task.FromResult(BarberCabinetData.Profile(
            avatarUrl: "/media/card-avatar.jpg"));

        harness.Avatar.MyProfile = _ => Task.FromException<MyProfileResponse>(
            new HttpRequestException("offline"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        Assert.Equal("/media/card-avatar.jpg", viewModel.AvatarUrl);
        Assert.True(viewModel.HasAvatar);
        Assert.False(viewModel.ShowInitials);
        Assert.True(viewModel.CanDeleteAvatar);
        Assert.True(viewModel.IsReady);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsAutoRetrying);
    }

    [Fact]
    public async Task ChangeAvatarAsync_UploadsPickedPhotoAndShowsPendingState()
    {
        var harness = new Harness();
        var image = new PortfolioImage(new byte[] { 1, 2, 3 }, "avatar.jpg", "image/jpeg");

        await harness.ViewModel.ActivateAsync();

        harness.Picker.Pick = _ => Task.FromResult<PortfolioImage?>(image);
        harness.Avatar.Upload = (_, _) => Task.FromResult(new AvatarUploadResponse(
            "/media/avatar-uploaded.jpg",
            PhotoModerationStatus.Pending));

        var viewModel = harness.ViewModel;

        await viewModel.ChangeAvatarCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Picker.PickCalls);
        Assert.Equal(1, harness.Avatar.UploadCalls);
        Assert.Equal(image, Assert.Single(harness.Avatar.Uploads));
        Assert.Equal("/media/avatar-uploaded.jpg", viewModel.AvatarUrl);
        Assert.True(viewModel.HasAvatar);
        Assert.True(viewModel.HasAvatarStatus);
        Assert.Equal("На проверке", viewModel.AvatarStatusText);
        Assert.Equal("pending", viewModel.AvatarStatusKind);
        Assert.Equal("Изменить фото", viewModel.AvatarActionText);
        Assert.Equal("Аватар загружен и ждёт проверки.", viewModel.AvatarAnnouncement);
        Assert.Null(viewModel.AvatarError);
        Assert.False(viewModel.IsAvatarUploading);
        Assert.False(viewModel.IsAvatarBusy);
    }

    [Fact]
    public async Task ChangeAvatarAsync_PickerClosed_DoesNotUploadAndShowsNoError()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.ChangeAvatarCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Picker.PickCalls);
        Assert.Equal(0, harness.Avatar.UploadCalls);
        Assert.Null(viewModel.AvatarError);
        Assert.Null(viewModel.AvatarAnnouncement);
        Assert.False(viewModel.IsAvatarUploading);
    }

    [Fact]
    public async Task ChangeAvatarAsync_UnsupportedPick_ShowsFileTextAndSkipsUpload()
    {
        var harness = new Harness();

        await harness.ViewModel.ActivateAsync();

        harness.Picker.Pick = _ => Task.FromException<PortfolioImage?>(
            new PortfolioImageException(PortfolioImageError.UnsupportedType));

        var viewModel = harness.ViewModel;

        await viewModel.ChangeAvatarCommand.ExecuteAsync(null);

        Assert.Equal("Поддерживаются только JPEG, PNG и WebP.", viewModel.AvatarError);
        Assert.Equal(0, harness.Avatar.UploadCalls);
        Assert.False(viewModel.IsAvatarUploading);
    }

    [Fact]
    public async Task ChangeAvatarAsync_UploadFailure_KeepsStateAndShowsReason()
    {
        var harness = new Harness();
        var image = new PortfolioImage(new byte[] { 1, 2, 3 }, "avatar.jpg", "image/jpeg");

        await harness.ViewModel.ActivateAsync();

        harness.Picker.Pick = _ => Task.FromResult<PortfolioImage?>(image);
        harness.Avatar.Upload = (_, _) => Task.FromException<AvatarUploadResponse>(
            new HttpRequestException("offline"));

        var viewModel = harness.ViewModel;
        var avatarBefore = viewModel.AvatarUrl;

        await viewModel.ChangeAvatarCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Avatar.UploadCalls);
        Assert.Equal(NetworkText, viewModel.AvatarError);
        Assert.Equal(avatarBefore, viewModel.AvatarUrl);
        Assert.False(viewModel.HasAvatar);
        Assert.Null(viewModel.AvatarAnnouncement);
        Assert.False(viewModel.IsAvatarUploading);
    }

    [Fact]
    public async Task DeleteAvatarAsync_ClearsAvatarAndAnnouncesRemoval()
    {
        var harness = new Harness();

        harness.Avatar.MyProfile = _ => Task.FromResult(BarberCabinetData.AccountProfile(
            "/media/avatar.jpg"));

        await harness.ViewModel.ActivateAsync();

        var viewModel = harness.ViewModel;

        await viewModel.DeleteAvatarCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Avatar.DeleteCalls);
        Assert.Null(viewModel.AvatarUrl);
        Assert.False(viewModel.HasAvatar);
        Assert.True(viewModel.ShowInitials);
        Assert.False(viewModel.CanDeleteAvatar);
        Assert.Equal("Загрузить фото", viewModel.AvatarActionText);
        Assert.Equal("Аватар удалён.", viewModel.AvatarAnnouncement);
        Assert.Null(viewModel.AvatarError);
        Assert.False(viewModel.IsAvatarDeleting);
    }

    private static IReadOnlyList<BarberProfilePhotoResponse> Photos(int count) =>
    [
        .. Enumerable.Range(0, count).Select(index => BarberCabinetData.Photo(
            $"Работа {index}",
            id: Guid.Parse($"9e100000-0000-4000-8000-{index + 1:D12}"))),
    ];

    /// <summary>
    /// Услуга прайса с собственными идентификаторами: группы проверяются на нескольких строках.
    /// </summary>
    /// <param name="name">Название услуги.</param>
    /// <param name="specializationName">Специализация услуги.</param>
    /// <param name="isActive">Видима ли услуга клиентам.</param>
    /// <returns>Строка прайса кабинета.</returns>
    private static BarberProfileServiceResponse CabinetService(
        string name,
        string specializationName,
        bool isActive = true) =>
        BarberCabinetFixtures.Service(specializationName: specializationName) with
        {
            BarberServiceId = Guid.CreateVersion7(),
            HairServiceId = Guid.CreateVersion7(),
            Name = name,
            IsActive = isActive,
        };

    /// <summary>
    /// Собранный раздел: подменённые клиенты карточки и аватара, выбор файла, часы и цикл повтора.
    /// </summary>
    private sealed class Harness
    {
        public Harness(AutoRetryLoop? retry = null)
        {
            ViewModel = new BarberCabinetProfileViewModel(
                Profile,
                Avatar,
                Picker,
                new FixedTimeProvider(BarberCabinetFixtures.Now),
                retry);

            ViewModel.Initialize(BarberCabinetFixtures.BarberProfileId);
        }

        public FakeBarberProfileApi Profile { get; } = new();

        public FakeAccountAvatarApi Avatar { get; } = new();

        public FakePortfolioPhotoPicker Picker { get; } = new();

        public BarberCabinetProfileViewModel ViewModel { get; }
    }
}
