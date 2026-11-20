using System.Net;
using BarberSearch.Mobile.Core.Api.Barber;
using BarberSearch.Mobile.Core.Media;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки разбора отказов портфолио: код домена важнее английского <c>detail</c>,
/// поле выбирается по причине, а признак повтора — по безопасности запроса.
/// </summary>
public sealed class PortfolioPhotoErrorsTests
{
    private const string FileTooLarge = "Файл слишком большой. Максимум — 5 МБ.";
    private const string FileRequired = "Выберите файл.";
    private const string UnsupportedType = "Поддерживаются только JPEG, PNG и WebP.";
    private const string InvalidImage = "Не удалось прочитать изображение. Выберите другой файл.";
    private const string Dimensions = "Стороны фото должны быть от 300 до 6000 px.";
    private const string DescriptionTooLong = "Подпись не длиннее 2000 символов.";
    private const string Limit = "В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.";
    private const string Reorder = "Не удалось сохранить порядок. Обновите страницу.";
    private const string NotFound = "Фото не найдено. Обновите страницу.";
    private const string Forbidden = "Нет доступа к портфолио этого барбера.";
    private const string Storage = "Не удалось сохранить файл. Попробуйте ещё раз.";
    private const string SessionExpired = "Сеанс истёк. Войдите заново.";
    private const string TooManyRequests = "Слишком много запросов. Подождите минуту и повторите.";
    private const string ServerFailed = "Сервер не справился. Повторите попытку.";
    private const string Network = "Нет связи с сервером. Проверьте подключение и повторите попытку.";
    private const string Fallback = "Не удалось выполнить действие с фото. Попробуйте ещё раз.";

    [Fact]
    public void Describe_RequestEntityTooLarge_BlamesFileWithoutRetry()
    {
        var failure = Describe(HttpStatusCode.RequestEntityTooLarge);

        Assert.Equal(PortfolioPhotoField.File, failure.Field);
        Assert.Equal(FileTooLarge, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Theory]
    [InlineData("barberProfile.uploadPhoto.fileRequired", FileRequired)]
    [InlineData("barberProfile.uploadPhoto.tooLarge", FileTooLarge)]
    [InlineData("barberProfile.uploadPhoto.unsupportedType", UnsupportedType)]
    [InlineData("barberProfile.uploadPhoto.invalidImage", InvalidImage)]
    [InlineData("barberProfile.uploadPhoto.dimensions", Dimensions)]
    public void Describe_FileCodes_BlameFileWithoutRetry(string code, string message)
    {
        var failure = Describe(HttpStatusCode.BadRequest, code);

        Assert.Equal(PortfolioPhotoField.File, failure.Field);
        Assert.Equal(message, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void Describe_DescriptionCode_BlamesCaptionWithoutRetry()
    {
        var failure = Describe(
            HttpStatusCode.BadRequest,
            "barberProfile.addPhoto.description");

        Assert.Equal(PortfolioPhotoField.Description, failure.Field);
        Assert.Equal(DescriptionTooLong, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void Describe_Limit_ShowsGeneralTextWithoutRetry()
    {
        var failure = Describe(HttpStatusCode.Conflict, "barberProfile.addPhoto.limit");

        Assert.Equal(PortfolioPhotoField.None, failure.Field);
        Assert.Equal(Limit, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void Describe_ReorderInvalid_ShowsGeneralTextWithoutRetry()
    {
        var failure = Describe(
            HttpStatusCode.BadRequest,
            "barberProfile.reorderPhotos.invalid");

        Assert.Equal(PortfolioPhotoField.None, failure.Field);
        Assert.Equal(Reorder, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Theory]
    [InlineData("portfolioPhoto.block.notFound")]
    [InlineData("portfolioPhoto.unblock.notFound")]
    public void Describe_NotFoundCodes_ShowNotFound(string code)
    {
        var failure = Describe(HttpStatusCode.Conflict, code);

        Assert.Equal(PortfolioPhotoField.None, failure.Field);
        Assert.Equal(NotFound, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void Describe_NotFoundStatus_ShowsNotFound()
    {
        var failure = Describe(HttpStatusCode.NotFound);

        Assert.Equal(NotFound, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Theory]
    [InlineData("auth.forbidden")]
    public void Describe_ForbiddenCode_ShowsAccessDenied(string code)
    {
        var failure = Describe(HttpStatusCode.BadRequest, code);

        Assert.Equal(Forbidden, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void Describe_ForbiddenStatus_ShowsAccessDenied()
    {
        var failure = Describe(HttpStatusCode.Forbidden);

        Assert.Equal(Forbidden, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void Describe_Storage_OffersRetry()
    {
        var failure = Describe(
            HttpStatusCode.InternalServerError,
            "barberProfile.uploadPhoto.storage");

        Assert.Equal(Storage, failure.Message);
        Assert.True(failure.CanRetry);
    }

    [Fact]
    public void Describe_Unauthorized_AsksToSignInWithoutRetry()
    {
        var failure = Describe(HttpStatusCode.Unauthorized);

        Assert.Equal(SessionExpired, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public void Describe_TooManyRequests_OffersRetry()
    {
        var failure = Describe(HttpStatusCode.TooManyRequests);

        Assert.Equal(TooManyRequests, failure.Message);
        Assert.True(failure.CanRetry);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Describe_ServerError_OffersRetry(HttpStatusCode statusCode)
    {
        var failure = Describe(statusCode);

        Assert.Equal(ServerFailed, failure.Message);
        Assert.True(failure.CanRetry);
    }

    [Fact]
    public void Describe_HttpRequestException_ShowsNetworkAndRetries()
    {
        var failure = PortfolioPhotoErrors.Describe(new HttpRequestException("down"));

        Assert.Equal(PortfolioPhotoField.None, failure.Field);
        Assert.Equal(Network, failure.Message);
        Assert.True(failure.CanRetry);
    }

    [Fact]
    public void Describe_TaskCanceledException_ShowsNetworkAndRetries()
    {
        // Таймаут HttpClient приходит как TaskCanceledException без отмены токена.
        var failure = PortfolioPhotoErrors.Describe(new TaskCanceledException("timeout"));

        Assert.Equal(Network, failure.Message);
        Assert.True(failure.CanRetry);
    }

    [Fact]
    public void Describe_TimeoutException_ShowsNetworkAndRetries()
    {
        var failure = PortfolioPhotoErrors.Describe(new TimeoutException());

        Assert.Equal(Network, failure.Message);
        Assert.True(failure.CanRetry);
    }

    [Fact]
    public void Describe_UnknownException_ShowsFallbackWithoutRetry()
    {
        var failure = PortfolioPhotoErrors.Describe(new InvalidOperationException("?"));

        Assert.Equal(PortfolioPhotoField.None, failure.Field);
        Assert.Equal(Fallback, failure.Message);
        Assert.False(failure.CanRetry);
    }

    [Theory]
    [InlineData(PortfolioImageError.SourceTooLarge, "Файл слишком большой. Максимум — 15 МБ.")]
    [InlineData(PortfolioImageError.UnsupportedType, UnsupportedType)]
    [InlineData(PortfolioImageError.InvalidImage, InvalidImage)]
    [InlineData(PortfolioImageError.TooLarge, "Не удалось уместить фото в 5 МБ. Выберите другое фото.")]
    public void DescribePicker_Kind_BlamesFileWithoutRetry(
        PortfolioImageError kind,
        string message)
    {
        var failure = PortfolioPhotoErrors.DescribePicker(kind);

        Assert.Equal(PortfolioPhotoField.File, failure.Field);
        Assert.Equal(message, failure.Message);
        Assert.False(failure.CanRetry);
    }

    private static PortfolioPhotoFailure Describe(
        HttpStatusCode statusCode,
        params string[] codes) =>
        PortfolioPhotoErrors.Describe(BarberCabinetApiError.Create(statusCode, codes));
}
