import {
  SERVICE_CATEGORY_ORDER,
  type ServiceCategory,
} from '@/lib/barber-profile-labels'
import type { SpecializationOption } from '@/lib/specializations'

/** Группа специализаций одной категории для `optgroup`. */
export type SpecializationGroup = {
  category: ServiceCategory
  specializations: SpecializationOption[]
}

/**
 * Раскладывает плоский справочник специализаций по категориям.
 *
 * Порядок групп — каталожный (`SERVICE_CATEGORY_ORDER`), а не алфавитный: так
 * селект читается сверху вниз одинаково в фильтре и в каталоге админки.
 * Внутри группы сохраняется порядок API (сервер отдаёт по имени). Пустые
 * категории не возвращаются: заголовок без строк — мёртвый пункт списка.
 *
 * Fallback-группы нет и не нужно: категория — закрытый enum, который
 * `isSpecializationOption` уже строго валидирует, поэтому неизвестная категория
 * отклоняет весь ответ, а не отдельную строку. Функция чистая: входной массив
 * и его элементы не мутируются, в группах — ссылки на исходные строки.
 */
export function groupSpecializations(
  specializations: readonly SpecializationOption[]
): SpecializationGroup[] {
  const byCategory = new Map<ServiceCategory, SpecializationOption[]>()

  for (const specialization of specializations) {
    const group = byCategory.get(specialization.category)

    if (group) {
      group.push(specialization)
    } else {
      byCategory.set(specialization.category, [specialization])
    }
  }

  const groups: SpecializationGroup[] = []

  for (const category of SERVICE_CATEGORY_ORDER) {
    const group = byCategory.get(category)

    if (group) {
      groups.push({ category, specializations: group })
    }
  }

  return groups
}
