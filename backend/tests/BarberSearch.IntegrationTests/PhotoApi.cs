using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSearch.Contracts.BarberProfiles.ReorderBarberProfilePhotos;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.PortfolioPhotos.UpdatePortfolioPhotoDescription;
using BarberSearch.Contracts.Users.Me;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// Загрузки и мутации фотографий для интеграционных тестов: портфолио и аватар.
/// </summary>
/// <remarks>
/// <para>
/// Помощник маленький и повторяет подход <see cref="AvatarModerationApi"/>: владелец ходит
/// Bearer-клиентом, решения администратора — cookie-сессией с <c>X-CSRF-TOKEN</c>.
/// Возвращается сырой ответ там, где тесту нужен отказ (<c>403</c>/<c>409</c>), и
/// проверяется <c>204</c>, когда сценарий ожидает успех.
/// </para>
/// <para>
/// URL и идентификаторы не угадываются: загрузка возвращает <c>201 { id }</c>, а avatar —
/// <c>{ avatarUrl, moderationStatus }</c>, поэтому проверки привязаны к тому, что реально
/// записал API.
/// </para>
/// </remarks>
internal static class PhotoApi
{
    public static async Task<Guid> UploadPortfolioPhotoAsync(
        HttpClient client,
        Guid barberProfileId,
        string? description = null)
    {
        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(PngBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");

        if (description is not null)
        {
            form.Add(new StringContent(description), "description");
        }

        using var response = await client.PostAsync(
            $"api/barber-profiles/{barberProfileId}/photos/upload",
            form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedResourceResponse>(
            TestApp.JsonOptions);
        Assert.NotNull(body);

        return body.Id;
    }

    /// <summary>
    /// Загружает файл в портфолио и возвращает ответ как есть: негативный сценарий
    /// (<c>403</c>) проверяет статус сам.
    /// </summary>
    public static Task<HttpResponseMessage> SendPortfolioUploadAsync(
        HttpClient client,
        Guid barberProfileId)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(PngBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");

        return client.PostAsync($"api/barber-profiles/{barberProfileId}/photos/upload", form);
    }

    public static async Task UpdateDescriptionAsync(
        HttpClient client,
        Guid barberProfileId,
        Guid photoId,
        string? description)
    {
        using var response = await client.PutAsJsonAsync(
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}/description",
            new UpdatePortfolioPhotoDescriptionRequest(description));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task ReorderAsync(
        HttpClient client,
        Guid barberProfileId,
        IReadOnlyList<Guid> photoIds)
    {
        using var response = await client.PutAsJsonAsync(
            $"api/barber-profiles/{barberProfileId}/photos/order",
            new ReorderBarberProfilePhotosRequest(photoIds));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task PublishAsync(
        HttpClient client,
        Guid barberProfileId,
        Guid photoId)
    {
        using var response = await client.PostAsync(
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}/publication",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task UnpublishAsync(
        HttpClient client,
        Guid barberProfileId,
        Guid photoId)
    {
        using var response = await client.DeleteAsync(
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}/publication");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task RemoveAsync(
        HttpClient client,
        Guid barberProfileId,
        Guid photoId)
    {
        using var response = await client.DeleteAsync(
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static async Task ApproveAsync(
        AdminAvatarSession admin,
        Guid barberProfileId,
        Guid photoId)
    {
        using var response = await SendApprovalAsync(admin, barberProfileId, photoId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    public static Task<HttpResponseMessage> SendApprovalAsync(
        AdminAvatarSession admin,
        Guid barberProfileId,
        Guid photoId)
        => AvatarModerationApi.SendAdminAsync(
            admin,
            HttpMethod.Post,
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}/approval");

    public static Task<HttpResponseMessage> SendRejectionAsync(
        AdminAvatarSession admin,
        Guid barberProfileId,
        Guid photoId,
        string? reason)
        => AvatarModerationApi.SendAdminAsync(
            admin,
            HttpMethod.Post,
            $"api/barber-profiles/{barberProfileId}/photos/{photoId}/rejection",
            new { reason });

    public static async Task<AvatarUploadResponse> UploadAvatarAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();

        var file = new ByteArrayContent(PngBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "avatar.png");

        using var response = await client.PostAsync("api/users/me/avatar", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AvatarUploadResponse>(
            TestApp.JsonOptions);
        Assert.NotNull(body);

        return body;
    }

    public static async Task DeleteAvatarAsync(HttpClient client)
    {
        using var response = await client.DeleteAsync("api/users/me/avatar");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// Минимальный PNG с настоящими сигнатурой и <c>IHDR</c>: инспектор читает размеры
    /// из заголовка и не декодирует файл.
    /// </summary>
    private static byte[] PngBytes(int width = 800, int height = 600)
    {
        var content = new byte[2048];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(content);

        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(8), 13);
        "IHDR"u8.CopyTo(content.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(20), (uint)height);
        content[24] = 8;
        content[25] = 6;

        return content;
    }
}
