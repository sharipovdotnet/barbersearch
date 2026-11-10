namespace BarberSearch.Contracts.Common;

/// <summary>
/// Ответ на операцию создания: идентификатор созданной сущности.
/// </summary>
/// <remarks>
/// Единый ответ для всех 201 Created. Идентификатор генерируется доменом (UUIDv7), поэтому
/// клиент узнаёт его только отсюда — сам он ключ не придумывает.
/// Заголовок <c>Location</c> по умолчанию не выставляется (R-42, 2026-10-25): у большинства
/// ресурсов, создаваемых с 201, нет self-URI, читаемого сразу после создания, — см. XML-док
/// <c>ApiController.ToCreatedResult</c>. Исключение с 2026-11-10 (issue-83) —
/// <c>POST api/barber-profiles</c>: <c>GET api/barber-profiles/{id}</c> читает собственный
/// профиль в любом статусе, поэтому ответ несёт <c>Location</c> через <c>CreatedAtAction</c>.
/// </remarks>
public record CreatedResourceResponse(Guid Id);
