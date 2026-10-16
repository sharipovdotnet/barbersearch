namespace BarberSearch.Contracts.Common;

/// <summary>
/// Ответ на операцию создания: идентификатор созданной сущности.
/// </summary>
/// <remarks>
/// Единый ответ для всех 201 Created. Идентификатор генерируется доменом (UUIDv7), поэтому
/// клиент узнаёт его только отсюда — сам он ключ не придумывает.
/// </remarks>
public record CreatedResourceResponse(Guid Id);
