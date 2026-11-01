namespace BarberSearch.Web.Auth;

/// <summary>
/// Исключает действие (или контроллер) из глобальной проверки antiforgery для cookie-мутаций.
/// </summary>
/// <remarks>
/// Маркер ставится только там, где у запроса нет ambient-идентичности либо защита уже
/// реализована внутри действия. Причина обязательна в комментарии рядом с атрибутом:
/// исключение без причины — это дыра, а не решение.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipCookieAntiforgeryAttribute : Attribute
{
}
