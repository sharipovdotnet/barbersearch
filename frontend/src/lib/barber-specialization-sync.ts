import type {
  BarberProfileService,
  BarberProfileSpecialization,
} from '@/lib/catalog'

/**
 * Разница между тегами профиля и его услугами.
 *
 * Специализация выводится из активных услуг: она живёт, пока у неё есть хотя бы
 * одна активная строка. Скрытая услуга остаётся в прайсе ради истории записей,
 * но клиентам её не видно, поэтому тег она не держит. Тег, у которого нет ни
 * одной строки услуги, — ручная настройка из блока «Специализации»: его не
 * трогают ни веб, ни мобильный клиент.
 *
 * `add` идёт в порядке первого появления специализации в услугах, `remove` — в
 * порядке тегов профиля. Функция чистая: входные массивы не мутирует.
 */
export type BarberSpecializationDiff = {
  add: string[]
  remove: string[]
}

export function diffBarberSpecializations(
  services: readonly BarberProfileService[],
  specializations: readonly BarberProfileSpecialization[]
): BarberSpecializationDiff {
  const active = new Set<string>()
  const serviceBacked = new Set<string>()
  const activeOrder: string[] = []

  for (const service of services) {
    const id = service.specializationId
    if (!id) continue

    serviceBacked.add(id)

    if (service.isActive && !active.has(id)) {
      active.add(id)
      activeOrder.push(id)
    }
  }

  const attached = new Set(
    specializations
      .map((item) => item.specializationId)
      .filter((id): id is string => id !== '')
  )

  const add = activeOrder.filter((id) => !attached.has(id))

  const seen = new Set<string>()
  const remove: string[] = []

  for (const item of specializations) {
    const id = item.specializationId
    if (!id || seen.has(id)) continue
    seen.add(id)

    // Тег без услуг не попадает в serviceBacked — это ручная настройка, её не трогаем.
    if (serviceBacked.has(id) && !active.has(id)) {
      remove.push(id)
    }
  }

  return { add, remove }
}
