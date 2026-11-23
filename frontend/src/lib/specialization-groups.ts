import {
  SERVICE_CATEGORY_ORDER,
  type ServiceCategory,
} from '@/lib/barber-profile-labels'
import type { SpecializationOption } from '@/lib/specializations'

/**
 * Группа специализаций одной категории для `optgroup`.
 *
 * `options` — ссылки на исходные строки: helper ничего не копирует и не меняет,
 * поэтому вызывающий код может сравнивать элементы по идентичности.
 */
export type SpecializationGroup = {
  category: ServiceCategory
  options: SpecializationOption[]
}

/**
 * Раскладывает плоский справочник по шести категориям.
 *
 * Порядок групп задаёт `SERVICE_CATEGORY_ORDER`, а не порядок входа; пустые группы
 * не возвращаются. Внутри группы сохраняется порядок API (по имени), поэтому
 * селект не переставляет строки относительно сервера. Функция чистая: входной
 * массив и его элементы не мутируются.
 */
export function groupSpecializations(
  options: readonly SpecializationOption[]
): SpecializationGroup[] {
  const byCategory = new Map<ServiceCategory, SpecializationOption[]>()

  for (const option of options) {
    const group = byCategory.get(option.category)

    if (group) {
      group.push(option)
    } else {
      byCategory.set(option.category, [option])
    }
  }

  return SERVICE_CATEGORY_ORDER.flatMap((category) => {
    const group = byCategory.get(category)

    return group ? [{ category, options: group }] : []
  })
}
