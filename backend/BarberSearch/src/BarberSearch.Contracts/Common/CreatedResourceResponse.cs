namespace BarberSearch.Contracts.Common;

/// <summary>
/// Ответ на операцию создания: идентификатор созданной сущности.
/// </summary>
/// <remarks>
/// Единый ответ для всех 201 Created. Идентификатор генерируется доменом (UUIDv7), поэтому
/// клиент узнаёт его только отсюда — сам он ключ не придумывает.
/// Заголовок <c>Location</c> не выставляется сознательно (R-42, 2026-10-25): ни у одного
/// ресурса, создаваемого с 201, нет self-URI, читаемого сразу после создания, — см. XML-док
/// <c>ApiController.ToCreatedResult</c>. Пересмотреть при появлении GET по id.
/// </remarks>
public record CreatedResourceResponse(Guid Id);
