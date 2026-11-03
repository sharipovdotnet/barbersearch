namespace BarberSearch.Application.Privacy;

/// <summary>
/// Действующая редакция Политики обработки персональных данных.
/// </summary>
/// <remarks>
/// Версия — дата редакции документа. Она совпадает с <c>PRIVACY_POLICY_VERSION</c> на клиенте
/// и с <c>PRIVACY_POLICY_DATE</c> там же: компилятор разные языки не сверит, поэтому каждое
/// расхождение ловят тесты обеих сторон, а живой клиент при расхождении получает
/// <c>auth.register.consentVersion</c>.
/// <para>
/// Любая правка текста Политики поднимает эту константу, дату на клиенте и дату редакции
/// в документе — иначе уже выданные согласия начинают относиться к другой редакции.
/// </para>
/// </remarks>
public static class PrivacyPolicy
{
    /// <summary>
    /// Редакция Политики в формате <c>YYYY-MM-DD</c>.
    /// </summary>
    public const string Version = "2026-11-03";
}
