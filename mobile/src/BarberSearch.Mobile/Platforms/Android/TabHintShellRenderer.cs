using Android.OS;
using Android.Views;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using AView = Android.Views.View;

namespace BarberSearch.Mobile.Navigation;

/// <summary>
/// Android-рендерер Shell, который заменяет подсказку вкладки на её полное название.
/// </summary>
/// <remarks>
/// <para>
/// В .NET MAUI 10 Shell на Android собран на совместимом <see cref="ShellRenderer"/>, поэтому
/// точка входа — фабрика вкладок. Материальная полоса берёт текст подсказки из заголовка пункта
/// меню, и долгое нажатие повторяло бы короткую подпись; здесь текст подсказки задаётся отдельно.
/// </para>
/// <para>
/// Текст живёт в <see cref="TabHints"/> и не зависит от платформы: когда Shell переедет на
/// обработчики, поменяется только место вызова.
/// </para>
/// </remarks>
internal sealed class TabHintShellRenderer : ShellRenderer
{
    /// <inheritdoc />
    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem) =>
        new TabHintShellItemRenderer(this);
}

/// <summary>
/// Полоса вкладок, у которой долгое нажатие называет раздел, а не повторяет подпись.
/// </summary>
internal sealed class TabHintShellItemRenderer : ShellItemRenderer
{
    /// <summary>
    /// API 26 (<c>Build.VERSION_CODES.O</c>) — с него у <c>View</c> появился tooltip.
    /// </summary>
    private const int TooltipApiLevel = 26;

    /// <summary>
    /// Создаёт рендерер вкладок.
    /// </summary>
    /// <param name="shellContext">Контекст Shell, который передаёт базовый рендерер.</param>
    public TabHintShellItemRenderer(IShellContext shellContext)
        : base(shellContext)
    {
    }

    /// <inheritdoc />
    public override AView OnCreateView(
        LayoutInflater inflater,
        ViewGroup container,
        Bundle savedInstanceState)
    {
        var view = base.OnCreateView(inflater, container, savedInstanceState);

        if (FindBottomNavigationView(view) is not { } bottomView)
        {
            return view;
        }

        ApplyTooltips(bottomView);

        // Пункты меню создаются раньше пунктов полосы, поэтому первый проход задаёт текст
        // до материальной инициализации, а LayoutChange повторяет его на готовых пунктах.
        bottomView.LayoutChange += (_, _) => ApplyTooltips(bottomView);

        return view;
    }

    /// <summary>
    /// Переносит подсказки на пункты меню и на уже созданные пункты полосы.
    /// </summary>
    /// <param name="bottomView">Полоса вкладок текущего Shell.</param>
    private void ApplyTooltips(BottomNavigationView bottomView)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(TooltipApiLevel))
        {
            return;
        }

        var sections = ShellItem.Items;
        using var menu = bottomView.Menu;

        var itemViews = bottomView.GetChildAt(0) as ViewGroup;

        for (var index = 0; index < sections.Count && index < menu.Size(); index++)
        {
            var hint = TabHints.For(sections[index].Items.FirstOrDefault()?.Route);

            if (hint.Length == 0)
            {
                continue;
            }

            using var menuItem = menu.GetItem(index);

            if (menuItem is null)
            {
                continue;
            }

            menuItem.SetTooltipText(hint);

            if (itemViews is not null &&
                index < itemViews.ChildCount &&
                itemViews.GetChildAt(index) is { } itemView)
            {
                itemView.TooltipText = hint;
            }
        }
    }

    /// <summary>
    /// Ищет полосу вкладок в дереве, которое построил базовый рендерер.
    /// </summary>
    /// <param name="view">Корень вкладки.</param>
    /// <returns>Полоса вкладок или <c>null</c>, если это не вкладка.</returns>
    private static BottomNavigationView? FindBottomNavigationView(AView view)
    {
        if (view is BottomNavigationView bottomView)
        {
            return bottomView;
        }

        if (view is not ViewGroup group)
        {
            return null;
        }

        for (var index = 0; index < group.ChildCount; index++)
        {
            if (group.GetChildAt(index) is { } child &&
                FindBottomNavigationView(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
