import { SERVICE_CATEGORY_ORDER } from '@/lib/barber-profile-labels'
import type { HairServiceOption } from '@/lib/hair-services'
import type { SpecializationOption } from '@/lib/specializations'

/**
 * Группа услуг одной специализации для `optgroup`.
 *
 * `specialization: null` — услуги, чьей специализации нет в публичном списке:
 * такие строки нельзя потерять, поэтому они идут одной группой «Прочие услуги».
 * `services` — ссылки на исходные строки: helper ничего не копирует и не меняет.
 */
export type HairServiceGroup = {
  specialization: SpecializationOption | null
  services: HairServiceOption[]
}

/**
 * Раскладывает плоский справочник услуг по специализациям.
 *
 * Порядок групп задаёт `SERVICE_CATEGORY_ORDER`: сначала категория специализации
 * (от стрижки к «Другому»), затем порядок API внутри категории (по имени). Так
 * селект читается сверху вниз одинаково на всех экранах, независимо от порядка
 * ответа. Пустые группы не возвращаются.
 *
 * Услуги, чья специализация не пришла в публичном списке (например, её выключили
 * между двумя запросами), не теряются: они собираются в одну последнюю группу с
 * `specialization: null` в порядке входа. Внутри известной группы сохраняется
 * порядок API. Функция чистая: входные массивы и их элементы не мутируются.
 */
export function groupHairServices(
  services: readonly HairServiceOption[],
  specializations: readonly SpecializationOption[]
): HairServiceGroup[] {
  const knownIds = new Set(specializations.map((row) => row.id))
  const bySpecialization = new Map<string, HairServiceOption[]>()
  const unknown: HairServiceOption[] = []

  for (const service of services) {
    if (!knownIds.has(service.specializationId)) {
      unknown.push(service)
      continue
    }

    const group = bySpecialization.get(service.specializationId)

    if (group) {
      group.push(service)
    } else {
      bySpecialization.set(service.specializationId, [service])
    }
  }

  const groups: HairServiceGroup[] = []

  for (const category of SERVICE_CATEGORY_ORDER) {
    for (const specialization of specializations) {
      if (specialization.category !== category) {
        continue
      }

      const group = bySpecialization.get(specialization.id)

      if (group) {
        groups.push({ specialization, services: group })
      }
    }
  }

  if (unknown.length > 0) {
    groups.push({ specialization: null, services: unknown })
  }

  return groups
}
