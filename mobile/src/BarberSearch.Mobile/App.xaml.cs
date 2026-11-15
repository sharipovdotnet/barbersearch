using BarberSearch.Mobile.Core.Localization;
using BarberSearch.Mobile.Theming;
using Microsoft.Maui.ApplicationModel;

namespace BarberSearch.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _services;
	private readonly ThemeService _themes;

	public App(IServiceProvider services, ILocalization localization, ThemeService themes)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(localization);
		ArgumentNullException.ThrowIfNull(themes);

		// Язык читается здесь, а не в MauiProgram: к этому моменту платформенные настройки
		// уже доступны. До создания Shell остаётся выставить модульный Loc, которым
		// пользуются XAML-разметка и статические форматтеры.
		localization.Initialize();
		Loc.Current = localization;

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
		return new Window(_services.GetRequiredService<AppShell>());
	}
}
