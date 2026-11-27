import type { BarberProfileService } from '@/lib/catalog'

/**
 * Группа услуг одного мастера для `optgroup`.
 *
 * `services` — ссылки на исходные строки прайса: helper ничего не копирует и не
 * меняет. Активность здесь не проверяется: `isActive` — решение карточки, а не
 * группировки, поэтому скрытые строки отсекает вызывающий код до вызова.
 */
export type BarberServiceGroup = {
  specializationId: string
  specializationName: string
  services: BarberProfileService[]
}

/**
 * Раскладывает прайс барбера по специализациям для `optgroup`.
 *
 * Плоский список не подсказывает, к какой специализации относится строка, а группы
 * читаются сверху вниз. Группы идут по алфавиту названий специализаций (русская
 * локаль), при совпадении имён — по `specializationId`: порядок не зависит от
 * порядка ответа API и одинаков на всех карточках. Внутри группы сохраняется
 * порядок API: прайс уже отсортирован для витрины, и переставлять строки незачем.
 * Пустые группы не возвращаются, поэтому пустой вход даёт пустой массив.
 *
 * Ключ группировки — `specializationId`, а не название: одноимённые, но разные
 * специализации остаются разными группами, иначе `optgroup` склеил бы чужие услуги.
 * Название группы берётся из первой строки специализации в порядке API.
 *
 * Функция чистая: возвращает новые массивы групп, входной массив и его строки не
 * мутируются.
 */
export function groupBarberServices(
  services: readonly BarberProfileService[]
): BarberServiceGroup[] {
  const groups = new Map<string, BarberServiceGroup>()

  // Порядок вставки не важен: ниже группы сортируются по названию специализации.
  for (const service of services) {
    const group = groups.get(service.specializationId)

    if (group) {
      group.services.push(service)
      continue
    }

    groups.set(service.specializationId, {
      specializationId: service.specializationId,
      specializationName: service.specializationName,
      services: [service],
    })
  }

  // Сортируем массив групп, а не вход: он собран здесь же, поэтому мутация своя.
  return [...groups.values()].sort((left, right) => {
    const byName = left.specializationName.localeCompare(
      right.specializationName,
      'ru'
    )

    return byName !== 0
      ? byName
      : left.specializationId.localeCompare(right.specializationId)
  })
}
