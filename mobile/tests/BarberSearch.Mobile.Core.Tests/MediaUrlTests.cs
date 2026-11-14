using BarberSearch.Mobile.Core.Media;

namespace BarberSearch.Mobile.Core.Tests;

/// <summary>
/// Проверки адресов картинок: склейка с адресом API и варианты размера.
/// </summary>
/// <remarks>
/// <para>
/// Адреса сверяются дословно с <c>frontend/src/lib/media.ts</c>: сервер отдаёт относительные
/// media-URL, а <c>Image</c> ждёт абсолютный. База берётся с портом — без него склейка
/// «съела» бы порт и тест не заметил бы регрессии.
/// </para>
/// <para>
/// Вариант размера — отдельный query у собственной раздачи. Внешние легаси-ссылки и чужие
/// префиксы обязаны возвращаться байт в байт: дописанный в чужой URL query сломал бы картинку.
/// </para>
/// </remarks>
public sealed class MediaUrlTests
{
    private static readonly Uri BaseAddress = new("http://localhost:5243/");

    [Theory]
    [InlineData("https://cdn.example.com/legacy/photo.jpg")]
    [InlineData("http://images.example.com/portfolio/1.webp")]
    public void Resolve_AbsoluteUrl_ReturnsItUntouched(string url)
    {
        // Легаси-картинки живут на чужих доменах: подменять им хост нельзя.
        Assert.Equal(url, MediaUrl.Resolve(BaseAddress, url));
    }

    [Fact]
    public void Resolve_RootRelativeUrl_JoinsBaseWithPort()
    {
        // Порт — часть адреса API: без завершающего слэша базы Uri отбросил бы последний
        // сегмент вместе с портом, и картинка ушла бы не туда.
        Assert.Equal(
            "http://localhost:5243/api/media/portfolio/photo.webp",
            MediaUrl.Resolve(BaseAddress, "/api/media/portfolio/photo.webp"));
    }

    [Theory]
    [InlineData("sm")]
    [InlineData("md")]
    [InlineData("lg")]
    public void Resize_OwnPortfolioUrl_AppendsRequestedSize(string size)
    {
        Assert.Equal(
            string.Concat("/api/media/portfolio/photo.webp?size=", size),
            MediaUrl.Resize("/api/media/portfolio/photo.webp", size));
    }

    [Theory]
    [InlineData("sm")]
    [InlineData("md")]
    [InlineData("lg")]
    public void Resize_OwnAvatarUrl_AppendsRequestedSize(string size)
    {
        Assert.Equal(
            string.Concat("/api/media/avatars/avatar.webp?size=", size),
            MediaUrl.Resize("/api/media/avatars/avatar.webp", size));
    }

    [Fact]
    public void Resize_UrlWithQuery_ReturnsUrlUnchanged()
    {
        const string url = "/api/media/portfolio/photo.webp?size=lg";

        // Второй query-параметр сделал бы адрес неоднозначным: сервер взял бы не тот вариант.
        Assert.Equal(url, MediaUrl.Resize(url, "md"));
    }

    [Fact]
    public void Resize_UrlWithFragment_ReturnsUrlUnchanged()
    {
        const string url = "/api/media/portfolio/photo.webp#frame";

        // Query после фрагмента — уже не часть пути: дописывать его нельзя.
        Assert.Equal(url, MediaUrl.Resize(url, "md"));
    }

    [Theory]
    [InlineData("xl")]
    [InlineData("original")]
    [InlineData("MD")]
    public void Resize_UnknownSize_ReturnsUrlUnchanged(string size)
    {
        // Набор sm|md|lg — часть контракта с сервером; «похожий» размер не угадывается.
        Assert.Equal("/api/media/portfolio/photo.webp", MediaUrl.Resize("/api/media/portfolio/photo.webp", size));
    }

    [Fact]
    public void Resize_ExternalLegacyUrl_ReturnsUrlUnchanged()
    {
        const string url = "https://cdn.example.com/legacy/photo.jpg";

        Assert.Equal(url, MediaUrl.Resize(url, "md"));
    }

    [Fact]
    public void Resize_ForeignMediaPrefix_ReturnsUrlUnchanged()
    {
        const string url = "/api/media/users/photo.webp";

        // Чужой префикс сервер вариантами не обслуживает: ушёл бы исходник, а не 404.
        Assert.Equal(url, MediaUrl.Resize(url, "md"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyUrl_ThrowsArgumentException(string url)
    {
        // Пустой адрес — дефект ответа, а не «нет картинки»: молчаливая заглушка скрыла бы его.
        Assert.Throws<ArgumentException>(() => MediaUrl.Resolve(BaseAddress, url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resize_EmptyUrl_ThrowsArgumentException(string url)
    {
        Assert.Throws<ArgumentException>(() => MediaUrl.Resize(url, "md"));
    }
}
