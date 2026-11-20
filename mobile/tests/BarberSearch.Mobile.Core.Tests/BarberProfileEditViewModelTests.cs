using System.Net;
using BarberSearch.Contracts.BarberProfiles.UpdateBarberProfile;
using BarberSearch.Domain.BarberProfiles.Enums;
using BarberSearch.Mobile.Core.Api;
using BarberSearch.Mobile.Core.ViewModels.Barber;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки правки карточки мастера: клиентские границы полей, раскладка серверных кодов
/// по полям, полное тело замены и отсутствие повтора записи.
/// </summary>
/// <remarks>
/// Грейд в форме только читается: его меняет администратор, и в теле запроса его нет.
/// Отмену правки делает страница — у формы нет команды «отменить», поэтому здесь она
/// не проверяется.
/// </remarks>
public sealed class BarberProfileEditViewModelTests
{
    private const string SaveFailedText = "Не удалось сохранить профиль. Попробуйте ещё раз.";

    [Fact]
    public void Initialize_FillsFormFromProfile()
    {
        var harness = new Harness();
        var profile = BarberCabinetData.Profile(
            certificates: ["Сертификат", "Курс"],
            achievements: ["Победа"],
            additionalServices: ["Отец и сын"]);

        harness.ViewModel.Initialize(profile);

        var viewModel = harness.ViewModel;

        Assert.Equal("Тимур Мастеров", viewModel.DisplayName);
        Assert.Equal("8", viewModel.ExperienceYears);
        Assert.Equal("Стригу с 2014 года.", viewModel.Bio);
        Assert.Equal("Опаздывающих жду 10 минут.", viewModel.LatePolicy);
        Assert.Equal("Старший мастер", viewModel.GradeText);
        Assert.Equal(string.Concat("Сертификат", Environment.NewLine, "Курс"), viewModel.Certificates);
        Assert.Equal("Победа", viewModel.Achievements);
        Assert.Equal("Отец и сын", viewModel.AdditionalServices);

        Assert.Equal(6, viewModel.Languages.Count);
        Assert.Equal(
            new[] { Language.Ru, Language.En },
            viewModel.Languages.Where(option => option.IsSelected).Select(option => option.Language));
        Assert.Equal(
            new[] { PaymentMethod.Cash, PaymentMethod.Sbp },
            viewModel.PaymentMethods
                .Where(option => option.IsSelected)
                .Select(option => option.Method));

        Assert.Null(viewModel.DisplayNameError);
        Assert.Null(viewModel.ExperienceError);
        Assert.Null(viewModel.FormError);
        Assert.True(viewModel.CanSave);
    }

    [Fact]
    public async Task SaveAsync_InvalidName_ShowsFieldErrorWithoutRequest()
    {
        var harness = new Harness();

        harness.ViewModel.Initialize(BarberCabinetData.Profile());

        var viewModel = harness.ViewModel;

        viewModel.DisplayName = "   ";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Укажите, как вас представить.", viewModel.DisplayNameError);
        Assert.Empty(harness.Profile.UpdateRequests);

        viewModel.DisplayName = new string('А', 121);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Не длиннее 120 символов.", viewModel.DisplayNameError);
        Assert.Empty(harness.Profile.UpdateRequests);
    }

    [Fact]
    public async Task SaveAsync_ExperienceOutOfRange_ShowsRangeError()
    {
        var harness = new Harness();

        harness.ViewModel.Initialize(BarberCabinetData.Profile());

        var viewModel = harness.ViewModel;

        viewModel.ExperienceYears = "71";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Опыт — от 1 до 70 лет.", viewModel.ExperienceError);
        Assert.Empty(harness.Profile.UpdateRequests);
    }

    [Fact]
    public async Task SaveAsync_NoLanguageSelected_ShowsLanguagesError()
    {
        var harness = new Harness();

        harness.ViewModel.Initialize(BarberCabinetData.Profile());

        var viewModel = harness.ViewModel;

        foreach (var language in viewModel.Languages)
        {
            language.IsSelected = false;
        }

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Выберите хотя бы один язык.", viewModel.LanguagesError);
        Assert.Empty(harness.Profile.UpdateRequests);
    }

    [Fact]
    public async Task SaveAsync_BioTooLong_ShowsBioError()
    {
        var harness = new Harness();

        harness.ViewModel.Initialize(BarberCabinetData.Profile());

        var viewModel = harness.ViewModel;

        viewModel.Bio = new string('а', 2001);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("О себе — не длиннее 2000 символов.", viewModel.BioError);
        Assert.Empty(harness.Profile.UpdateRequests);
    }

    [Fact]
    public async Task SaveAsync_Valid_SendsWholePayloadAndRaisesSaved()
    {
        var harness = new Harness();
        var saved = 0;

        harness.ViewModel.Initialize(BarberCabinetData.Profile());
        harness.ViewModel.Saved += (_, _) => saved++;

        var viewModel = harness.ViewModel;

        viewModel.DisplayName = "  Тимур Мастеров  ";
        viewModel.ExperienceYears = " 9 ";
        viewModel.Bio = "  Новая история  ";
        viewModel.LatePolicy = "  Жду 5 минут  ";
        viewModel.Certificates = " Сертификат \r\n\r\n Курс ";
        viewModel.Achievements = "Победа";
        viewModel.AdditionalServices = "Отец и сын";

        foreach (var language in viewModel.Languages)
        {
            language.IsSelected = language.Language is Language.Ru or Language.De;
        }

        foreach (var method in viewModel.PaymentMethods)
        {
            method.IsSelected = method.Method == PaymentMethod.Card;
        }

        await viewModel.SaveCommand.ExecuteAsync(null);

        var write = Assert.Single(harness.Profile.UpdateRequests);
        var request = write.Request;

        Assert.Equal(BarberCabinetFixtures.BarberProfileId, write.ProfileId);
        Assert.Equal("Тимур Мастеров", request.DisplayName);
        Assert.Equal(9, request.YearsOfExperience);
        Assert.Equal("Новая история", request.Bio);
        Assert.Equal("Жду 5 минут", request.LatePolicy);
        Assert.Equal([Language.Ru, Language.De], request.Languages);
        Assert.Equal([PaymentMethod.Card], request.PaymentMethods);
        Assert.Equal(["Сертификат", "Курс"], request.CertificatesAndTraining);
        Assert.Equal(["Победа"], request.Achievements);
        Assert.Equal(["Отец и сын"], request.AdditionalServices);
        Assert.Equal(1, saved);
        Assert.Null(viewModel.FormError);
        Assert.False(viewModel.IsSaving);
        Assert.True(viewModel.CanSave);
    }

    [Fact]
    public async Task SaveAsync_ServerFieldCodes_LandOnTheirFields()
    {
        var harness = new Harness();

        harness.ViewModel.Initialize(BarberCabinetData.Profile());

        var viewModel = harness.ViewModel;

        harness.Profile.Update = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(HttpStatusCode.BadRequest, "barberProfile.displayName"));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Укажите, как вас представить.", viewModel.DisplayNameError);
        Assert.Null(viewModel.FormError);

        harness.Profile.Update = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(HttpStatusCode.BadRequest, "barberProfile.yearsOfExperience"));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Опыт — от 1 до 70 лет.", viewModel.ExperienceError);
        Assert.Equal(2, harness.Profile.UpdateRequests.Count);
    }

    [Fact]
    public async Task SaveAsync_Forbidden_ShowsPermissionReason()
    {
        var harness = new Harness();

        harness.ViewModel.Initialize(BarberCabinetData.Profile());

        harness.Profile.Update = (_, _) => Task.FromException(
            BarberCabinetApiError.Create(HttpStatusCode.Forbidden, "barberProfile.forbidden"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Нет прав на изменение профиля.", harness.ViewModel.FormError);
    }

    [Fact]
    public async Task SaveAsync_TransportFailure_AttemptsOnceAndShowsGenericReason()
    {
        var harness = new Harness();

        harness.ViewModel.Initialize(BarberCabinetData.Profile());

        harness.Profile.Update = (_, _) => Task.FromException(
            new HttpRequestException("offline"));

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        // Запись не повторяется сама: после сбоя исход неизвестен, решает человек.
        Assert.Single(harness.Profile.UpdateRequests);
        Assert.Equal(SaveFailedText, harness.ViewModel.FormError);
        Assert.False(harness.ViewModel.IsSaving);
    }

    /// <summary>
    /// Собранная форма: подменённый клиент профиля и заполненная карточка.
    /// </summary>
    private sealed class Harness
    {
        public FakeBarberProfileApi Profile { get; } = new();

        public BarberProfileEditViewModel ViewModel => _viewModel ??= new BarberProfileEditViewModel(Profile);

        private BarberProfileEditViewModel? _viewModel;
    }
}
