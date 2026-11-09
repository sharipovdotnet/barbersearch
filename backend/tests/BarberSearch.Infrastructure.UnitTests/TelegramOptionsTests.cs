using BarberSearch.Infrastructure.Telegram;
using Xunit;

namespace BarberSearch.Infrastructure.UnitTests;

/// <summary>
/// Настройки Telegram: канал настроен только при токене и имени бота, а ссылка привязки
/// нормализует ведущий <c>@</c> и всегда ведёт на <c>t.me</c>.
/// </summary>
public class TelegramOptionsTests
{
    [Fact]
    public void IsConfigured_RequiresBothTokenAndUsername()
    {
        Assert.True(new TelegramOptions
        {
            BotToken = "123456:test",
            BotUsername = "barbersearch_bot"
        }.IsConfigured);

        Assert.False(new TelegramOptions { BotToken = "123456:test" }.IsConfigured);
        Assert.False(new TelegramOptions { BotUsername = "barbersearch_bot" }.IsConfigured);
        Assert.False(new TelegramOptions { BotToken = " ", BotUsername = " " }.IsConfigured);
    }

    [Fact]
    public void Defaults_AreNotConfigured()
    {
        var options = new TelegramOptions();

        Assert.False(options.IsConfigured);
        Assert.Null(options.BotToken);
        Assert.Null(options.BotUsername);
        Assert.Null(options.WebhookSecret);
    }

    [Fact]
    public void BuildDeepLink_StripsLeadingAt()
    {
        var options = new TelegramOptions
        {
            BotToken = "123456:test",
            BotUsername = " @barbersearch_bot "
        };

        var link = options.BuildDeepLink("ABCDEF");

        Assert.Equal("https://t.me/barbersearch_bot?start=ABCDEF", link);
    }

    [Fact]
    public void BuildDeepLink_WithoutBotUsername_Throws()
    {
        var options = new TelegramOptions { BotToken = "123456:test" };

        Assert.Throws<InvalidOperationException>(() => options.BuildDeepLink("ABCDEF"));
    }
}
