using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Core.Push;
using BarberSearch.Mobile.Push;
using BarberSearch.Mobile.Theming;

namespace BarberSearch.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _services;
	private readonly ThemeService _themes;

	public App(
		IServiceProvider services,
		ILocalization localization,
		ThemeService themes,
		PushOptions pushOptions)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(localization);
		ArgumentNullException.ThrowIfNull(themes);
		ArgumentNullException.ThrowIfNull(pushOptions);

		// Язык читается здесь, а не в MauiProgram: к этому моменту платформенные настройки
		// уже доступны. До создания Shell остаётся выставить модульный Loc, которым
		// пользуются XAML-разметка и статические форматтеры.
		localization.Initialize();
		Loc.Current = localization;

#if ANDROID
		// Канал уведомлений создаётся после инициализации языка: его имя и описание видны
		// в настройках телефона и должны совпадать с языком интерфейса. Без push.json канал
		// не создаётся вовсе — ненастроенная функция не должна появляться в системе.
		if (pushOptions.Enabled)
		{
			AndroidNotifications.EnsureChannel(
				Android.App.Application.Context,
				Loc.T("Напоминания"),
				Loc.T("Напоминания о записях"));
		}
#endif

		InitializeComponent();

		_services = services;
		_themes = themes;

		// Системные панели Android не участвуют в AppThemeBinding: их красит PlatformTheme
		// после каждой смены темы — и по выбору в «Профиле», и когда тему меняет система.
		RequestedThemeChanged += (_, args) => PlatformTheme.Apply(args.RequestedTheme == AppTheme.Dark);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Тема применяется до сборки Shell: страницы сразу строятся в нужной палитре и не
		// успевают мигнуть светлым. Здесь Application.Current уже задан, в отличие от
		// конструктора App.
		_themes.ApplyStored();

		// Shell собирается из контейнера: страницы получают свои ViewModel через конструктор,
		// а не через Activator.CreateInstance из DataTemplate.
		var shell = _services.GetRequiredService<AppShell>();
		var window = new Window(shell);

		// Напоминание могло открыть приложение с холодного старта: цель нажатия уже лежит
		// в регистраторе переходов, но Shell в этот момент только собирается. Flush на
		// Created выполняется, когда окно и Shell уже на месте.
		var router = _services.GetRequiredService<NotificationTapRouter>();

		window.Created += (_, _) => router.ScheduleFlush();
		window.Resumed += OnWindowResumed;

		router.ScheduleFlush();

		// Регистрация устройства — по Resumed, а не здесь: к этому моменту Activity ещё не
		// возобновлена, и системный диалог разрешения показать не из чего. Resumed приходит
		// и на первом запуске, и при каждом возврате в приложение.
		return window;
	}

	/// <summary>
	/// Регистрирует устройство, когда сессия восстановлена из хранилища или приложение
	/// вернулось на экран.
	/// </summary>
	/// <remarks>
	/// Регистратор сам проверяет настройку, сессию и разрешение и никогда не бросает
	/// исключений, поэтому обработчик не показывает ошибок и не мешает появлению окна.
	/// </remarks>
	private async void OnWindowResumed(object? sender, EventArgs e) =>
		await _services.GetRequiredService<IPushRegistrar>().EnsureRegisteredAsync();
}
