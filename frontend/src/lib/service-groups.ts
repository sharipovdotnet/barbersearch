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

/** Необязательное сужение групп до одной специализации, выбранной в фильтре. */
export type GroupHairServicesOptions = {
  /**
   * Если задан и не пуст, в результат попадают только услуги этой специализации:
   * селект «Услуга» показывает ровно услуги выбранной специализации. Пустая
   * строка — «фильтра нет», как и у остальных полей формы.
   */
  specializationId?: string
}

/**
 * Раскладывает плоский справочник услуг по специализациям.
 *
 * Порядок групп — по алфавиту названий специализаций (русская локаль), при
 * совпадении имён — по `id`: так порядок не зависит ни от категории, ни от
 * порядка ответа API, и селект читается сверху вниз одинаково на всех экранах.
 * Пустые группы не возвращаются.
 *
 * Услуги, чья специализация не пришла в публичном списке (например, её выключили
 * между двумя запросами), не теряются: они собираются в одну последнюю группу с
 * `specialization: null` в порядке входа. Внутри известной группы сохраняется
 * порядок API. Функция чистая: входные массивы и их элементы не мутируются.
 *
 * С `options.specializationId` остаются только услуги этой специализации: чужие
 * и «прочие» строки в выбор не попадают, поэтому хвостовой группы с
 * `specialization: null` в этом режиме нет. Если услуг у специализации нет,
 * возвращается пустой массив — пустая группа не нужна.
 */
export function groupHairServices(
  services: readonly HairServiceOption[],
  specializations: readonly SpecializationOption[],
  options: GroupHairServicesOptions = {}
): HairServiceGroup[] {
  const { specializationId } = options
  // Пустая строка — «без фильтра»: так форма представляет невыбранное поле.
  // Фильтр по специализации не копирует строки: дальше работают ссылки на них.
  const isFiltered =
    specializationId !== undefined && specializationId !== ''
  const visible = isFiltered
    ? services.filter(
        (service) => service.specializationId === specializationId
      )
    : services

  const knownIds = new Set(specializations.map((row) => row.id))
  const bySpecialization = new Map<string, HairServiceOption[]>()
  const unknown: HairServiceOption[] = []

  for (const service of visible) {
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

  // Сортируем копию: входной массив специализаций принадлежит вызывающему коду.
  const sortedSpecializations = [...specializations].sort((left, right) => {
    const byName = left.name.localeCompare(right.name, 'ru')

    return byName !== 0 ? byName : left.id.localeCompare(right.id)
  })

  const groups: HairServiceGroup[] = []

  for (const specialization of sortedSpecializations) {
    const group = bySpecialization.get(specialization.id)

    if (group) {
      groups.push({ specialization, services: group })
    }
  }

  // В режиме выбранной специализации хвостовой группы нет: её id пришёл из
  // этого же справочника, поэтому «прочих» строк быть не может.
  if (unknown.length > 0 && !isFiltered) {
    groups.push({ specialization: null, services: unknown })
  }

  return groups
}
