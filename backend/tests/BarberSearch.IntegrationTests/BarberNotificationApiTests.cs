using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarberSearch.Application.Notifications;
using BarberSearch.Contracts.Common;
using BarberSearch.Contracts.Notifications;
using BarberSearch.Contracts.Notifications.CreateTelegramLink;
using BarberSearch.Contracts.Notifications.UpdateNotificationSettings;
using BarberSearch.Domain.Notifications;
using BarberSearch.Domain.Users;
using BarberSearch.Web.Controllers;
using CSharpFunctionalExtensions;
using Shared;
using Xunit;

namespace BarberSearch.IntegrationTests;

/// <summary>
/// HTTP-контракт уведомлений: права, валидация настроек, инбокс, вебхук Telegram и тексты.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class BarberNotificationApiTests
{
    private const string WebhookSecret = "integration-webhook-secret";
    private const long ChatId = 777_001;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _database;

    public BarberNotificationApiTests(PostgresFixture database)
    {
        _database = database;
    }

    [Fact(Timeout = 180_000)]
    public async Task Anonymous_is_unauthorized_and_customer_is_forbidden()
    {
        await using var app = await TestApp.StartAsync(_database);
        var customer = await app.CreateUserAsync(
            $"notification-api-customer-{Guid.NewGuid():N}@barbersearch.test");

        using var anonymous = await app.Client.GetAsync("api/barber/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await app.AuthenticateAsync(customer.Email);

        using var asCustomer = await app.Client.GetAsync("api/barber/notifications");
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);

        using var settingsAsCustomer = await app.Client.GetAsync("api/barber/notification-settings");
        Assert.Equal(HttpStatusCode.Forbidden, settingsAsCustomer.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Settings_read_creates_defaults_and_validation_returns_field_codes()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var barber = await data.CreateBookableBarberAsync("Notification Settings Barber");
        await app.AuthenticateAsync(barber.User.Email);

        using var created = await app.Client.GetAsync("api/barber/notification-settings");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var defaults = await created.Content.ReadFromJsonAsync<NotificationSettingsResponse>(JsonOptions);
        Assert.NotNull(defaults);
        Assert.True(defaults.IsEnabled);
        Assert.True(defaults.DigestEnabled);
        Assert.Equal("08:00", defaults.DigestTime);
        Assert.Equal(30, defaults.LeadMinutes);
        Assert.False(defaults.TelegramLinked);
        Assert.Null(defaults.TelegramLinkedAt);
        Assert.False(defaults.BotConfigured);

        var body = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("chatId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("telegramChatId", body, StringComparison.OrdinalIgnoreCase);

        using var badLead = await PutSettingsAsync(app.Client, true, true, "08:00", 20);
        Assert.Equal(HttpStatusCode.BadRequest, badLead.StatusCode);
        Assert.Contains(
            "notificationSettings.leadMinutes",
            await badLead.Content.ReadAsStringAsync());

        using var badTime = await PutSettingsAsync(app.Client, true, true, "8:00", 30);
        Assert.Equal(HttpStatusCode.BadRequest, badTime.StatusCode);
        Assert.Contains("notificationSettings.digestTime", await badTime.Content.ReadAsStringAsync());

        using var updated = await PutSettingsAsync(app.Client, true, false, "09:15", 60);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var saved = await updated.Content.ReadFromJsonAsync<NotificationSettingsResponse>(JsonOptions);
        Assert.NotNull(saved);
        Assert.True(saved.IsEnabled);
        Assert.False(saved.DigestEnabled);
        Assert.Equal("09:15", saved.DigestTime);
        Assert.Equal(60, saved.LeadMinutes);
    }

    [Fact(Timeout = 180_000)]
    public async Task Inbox_reads_are_owned_and_read_all_is_idempotent()
    {
        await using var app = await TestApp.StartAsync(_database);
        var data = new TestData(app);
        var notifications = new NotificationTestData(app);

        var barber = await data.CreateBookableBarberAsync("Notification Inbox Barber");
        var otherBarber = await data.CreateBookableBarberAsync("Foreign Notification Barber");
        await app.AuthenticateAsync(barber.User.Email);

        using var empty = await app.Client.GetAsync("api/barber/notifications");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);

        var emptyPage = await empty.Content
            .ReadFromJsonAsync<PagedResponse<BarberNotificationResponse>>(JsonOptions);
        Assert.NotNull(emptyPage);
        Assert.Empty(emptyPage.Items);
        Assert.Null(emptyPage.NextCursor);

        var foreign = Require(BarberNotification.Upcoming(
            otherBarber.User.Id,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(30),
            "Haircut",
            "Salon",
            "Europe/Berlin"));
        await notifications.AddNotificationAsync(foreign);

        using var foreignRead = await app.Client.PostAsync(
            $"api/barber/notifications/{foreign.Id}/read",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Contains("notification.notFound", await foreignRead.Content.ReadAsStringAsync());

        var own = Require(BarberNotification.Upcoming(
            barber.User.Id,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(30),
            "Haircut Inbox",
            "Salon Inbox",
            "Europe/Berlin"));
        await notifications.AddNotificationAsync(own);

        using var page = await app.Client.GetAsync("api/barber/notifications?pageSize=20");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var pageBody = await page.Content
            .ReadFromJsonAsync<PagedResponse<BarberNotificationResponse>>(JsonOptions);
        Assert.NotNull(pageBody);
        var item = Assert.Single(pageBody.Items);
        Assert.Equal(own.Id, item.Id);
        Assert.Equal(BarberNotificationKind.Upcoming, item.Kind);
        Assert.Equal("Haircut Inbox", item.ServiceName);
        Assert.Equal("Salon Inbox", item.SalonName);

        using var unread = await app.Client.GetAsync("api/barber/notifications/unread-count");
        var unreadBody = await unread.Content
            .ReadFromJsonAsync<UnreadNotificationsCountResponse>(JsonOptions);
        Assert.NotNull(unreadBody);
        Assert.Equal(1, unreadBody.Count);

        using var read = await app.Client.PostAsync(
            $"api/barber/notifications/{own.Id}/read",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        using var afterRead = await app.Client.GetAsync("api/barber/notifications/unread-count");
        var afterReadBody = await afterRead.Content
            .ReadFromJsonAsync<UnreadNotificationsCountResponse>(JsonOptions);
        Assert.NotNull(afterReadBody);
        Assert.Equal(0, afterReadBody.Count);

        using var readAll = await app.Client.PostAsync("api/barber/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.NoContent, readAll.StatusCode);

        using var readAllAgain = await app.Client.PostAsync("api/barber/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.NoContent, readAllAgain.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Webhook_requires_secret_and_links_chat_once_with_exact_texts()
    {
        var telegram = new CapturingTelegramSender();

        await using var app = await TestApp.StartAsync(
            _database,
            TelegramConfiguration(),
            telegramSender: telegram);

        using var webhookClient = app.CreateClient();

        using (var wrongSecret = await SendWebhookAsync(
                   webhookClient,
                   "wrong-secret",
                   9001,
                   "UNKNOWNCODE",
                   ChatId))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongSecret.StatusCode);
        }

        using (var noSecret = await SendWebhookAsync(
                   webhookClient,
                   null,
                   9002,
                   "UNKNOWNCODE",
                   ChatId))
        {
            Assert.Equal(HttpStatusCode.Forbidden, noSecret.StatusCode);
        }

        using (var unknownCode = await SendWebhookAsync(
                   webhookClient,
                   WebhookSecret,
                   9003,
                   "UNKNOWNCODE",
                   ChatId))
        {
            Assert.Equal(HttpStatusCode.OK, unknownCode.StatusCode);
        }

        Assert.Equal(
            NotificationMessages.TELEGRAM_LINK_CODE_EXPIRED,
            Assert.Single(telegram.Messages).Text);

        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        var barber = await data.CreateBookableBarberAsync("Telegram Link Barber");
        await app.AuthenticateAsync(barber.User.Email);

        using var link = await app.Client.PostAsync("api/barber/notifications/telegram/link", null);
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);

        var linkBody = await link.Content.ReadFromJsonAsync<TelegramLinkResponse>(JsonOptions);
        Assert.NotNull(linkBody);
        Assert.Equal(32, linkBody.Code.Length);
        Assert.Equal(
            $"https://t.me/integration_test_bot?start={linkBody.Code}",
            linkBody.DeepLink);
        Assert.True(linkBody.ExpiresAt > DateTimeOffset.UtcNow);

        using (var webhook = await SendWebhookAsync(
                   webhookClient,
                   WebhookSecret,
                   9004,
                   linkBody.Code,
                   ChatId))
        {
            Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        }

        var settings = await notifications.FindSettingsAsync(barber.User.Id);
        Assert.NotNull(settings);
        Assert.Equal(ChatId, settings.TelegramChatId);
        Assert.NotNull(settings.TelegramLinkedAt);
        Assert.Null(settings.TelegramLinkCodeHash);
        Assert.Equal(NotificationMessages.TELEGRAM_LINKED, telegram.Messages[^1].Text);

        var messageCount = telegram.Messages.Count;

        // Тот же update_id — Telegram ретраит: состояние не меняется и ответ снова 200.
        using (var retry = await SendWebhookAsync(
                   webhookClient,
                   WebhookSecret,
                   9004,
                   linkBody.Code,
                   ChatId))
        {
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        }

        Assert.Equal(messageCount, telegram.Messages.Count);

        using var secondLink = await app.Client.PostAsync(
            "api/barber/notifications/telegram/link",
            null);
        Assert.Equal(HttpStatusCode.Conflict, secondLink.StatusCode);
        Assert.Contains(
            "notification.telegram.alreadyLinked",
            await secondLink.Content.ReadAsStringAsync());

        using var unlink = await app.Client.PostAsync(
            "api/barber/notifications/telegram/unlink",
            null);
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        using var unlinkAgain = await app.Client.PostAsync(
            "api/barber/notifications/telegram/unlink",
            null);
        Assert.Equal(HttpStatusCode.NoContent, unlinkAgain.StatusCode);
    }

    [Fact(Timeout = 180_000)]
    public async Task Webhook_refuses_a_chat_already_linked_to_another_cabinet()
    {
        var telegram = new CapturingTelegramSender();

        await using var app = await TestApp.StartAsync(
            _database,
            TelegramConfiguration(),
            telegramSender: telegram);

        var data = new TestData(app);
        var notifications = new NotificationTestData(app);
        using var webhookClient = app.CreateClient();

        var first = await data.CreateBookableBarberAsync("First Telegram Barber");
        var second = await data.CreateBookableBarberAsync("Second Telegram Barber");

        await app.AuthenticateAsync(first.User.Email);
        using var firstLink = await app.Client.PostAsync(
            "api/barber/notifications/telegram/link",
            null);
        var firstCode = await firstLink.Content.ReadFromJsonAsync<TelegramLinkResponse>(JsonOptions);
        Assert.NotNull(firstCode);

        using (var firstWebhook = await SendWebhookAsync(
                   webhookClient,
                   WebhookSecret,
                   9011,
                   firstCode.Code,
                   ChatId))
        {
            Assert.Equal(HttpStatusCode.OK, firstWebhook.StatusCode);
        }

        await app.AuthenticateAsync(second.User.Email);
        using var secondLink = await app.Client.PostAsync(
            "api/barber/notifications/telegram/link",
            null);
        var secondCode = await secondLink.Content.ReadFromJsonAsync<TelegramLinkResponse>(JsonOptions);
        Assert.NotNull(secondCode);

        using (var secondWebhook = await SendWebhookAsync(
                   webhookClient,
                   WebhookSecret,
                   9012,
                   secondCode.Code,
                   ChatId))
        {
            Assert.Equal(HttpStatusCode.OK, secondWebhook.StatusCode);
        }

        Assert.Equal(
            NotificationMessages.TELEGRAM_CHAT_ALREADY_LINKED,
            telegram.Messages[^1].Text);

        var secondSettings = await notifications.FindSettingsAsync(second.User.Id);
        Assert.NotNull(secondSettings);
        Assert.Null(secondSettings.TelegramChatId);

        var firstSettings = await notifications.FindSettingsAsync(first.User.Id);
        Assert.NotNull(firstSettings);
        Assert.Equal(ChatId, firstSettings.TelegramChatId);
    }

    private static async Task<HttpResponseMessage> PutSettingsAsync(
        HttpClient client,
        bool isEnabled,
        bool digestEnabled,
        string digestTime,
        int leadMinutes)
    {
        var request = new UpdateNotificationSettingsRequest(
            isEnabled,
            digestEnabled,
            digestTime,
            leadMinutes);

        return await client.PutAsync(
            "api/barber/notification-settings",
            JsonContent.Create(request, options: JsonOptions));
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        string? secret,
        long updateId,
        string code,
        long chatId)
    {
        ArgumentNullException.ThrowIfNull(client);

        var json = $$"""
            {
              "update_id": {{updateId}},
              "message": {
                "text": "/start {{code}}",
                "chat": { "id": {{chatId}} }
              }
            }
            """;

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/telegram/webhook")
        {
            Content = content
        };

        if (secret is not null)
        {
            request.Headers.Add(TelegramWebhookController.SECRET_HEADER_NAME, secret);
        }

        return await client.SendAsync(request);
    }

    private static Dictionary<string, string?> TelegramConfiguration()
        => new(StringComparer.Ordinal)
        {
            ["Telegram__BotToken"] = "123456:integration-test-token",
            ["Telegram__BotUsername"] = "integration_test_bot",
            ["Telegram__WebhookSecret"] = WebhookSecret
        };

    private static T Require<T>(Result<T, Failure> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Seeding failed: {string.Join("; ", result.Error.Errors.Select(error => $"{error.Code}: {error.Message}"))}");
        }

        return result.Value;
    }
}
