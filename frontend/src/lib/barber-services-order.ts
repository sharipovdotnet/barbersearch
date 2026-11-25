import {
  SERVICE_CATEGORY_ORDER,
  type ServiceCategory,
} from '@/lib/barber-profile-labels'
import type { BarberProfileService } from '@/lib/catalog'

/**
 * Порядок услуг в публичном прайсе: категория → специализация → название.
 *
 * Категории идут каталожным порядком `SERVICE_CATEGORY_ORDER` (стрижка, борода,
 * окрашивание, укладка, уход, другое), а не по алфавиту: клиент ищет знакомую
 * услугу в привычном порядке витрины. Внутри категории услуги одной специализации
 * стоят рядом, поэтому бейджи читаются сверху вниз как группы. Специализации и
 * названия сравниваются русской локалью, `barberServiceId` закрывает полное
 * равенство — порядок не зависит от порядка ответа API и не «прыгает» между
 * запросами.
 *
 * Функция чистая: возвращает новый массив, входной не мутирует.
 */
const CATEGORY_RANK = new Map<ServiceCategory, number>(
  SERVICE_CATEGORY_ORDER.map((category, index) => [category, index])
)

export function sortBarberServices(
  services: readonly BarberProfileService[]
): BarberProfileService[] {
  return [...services].sort((left, right) => {
    const byCategory = categoryRank(left.category) - categoryRank(right.category)

    if (byCategory !== 0) {
      return byCategory
    }

    const bySpecialization = left.specializationName.localeCompare(
      right.specializationName,
      'ru'
    )

    if (bySpecialization !== 0) {
      return bySpecialization
    }

    const byName = left.name.localeCompare(right.name, 'ru')

    if (byName !== 0) {
      return byName
    }

    return left.barberServiceId.localeCompare(right.barberServiceId)
  })
}

/** Неизвестная категория в конце списка, а не в середине. */
function categoryRank(category: ServiceCategory): number {
  return CATEGORY_RANK.get(category) ?? SERVICE_CATEGORY_ORDER.length
}
