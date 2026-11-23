import { describe, expect, it } from 'vitest'

import type { ServiceCategory } from '@/lib/barber-profile-labels'
import { groupSpecializations } from '@/lib/specialization-groups'
import type { SpecializationOption } from '@/lib/specializations'

/**
 * Чистая группировка справочника: порядок групп — витринный, порядок строк внутри
 * группы — серверный, пустые категории не возвращаются, вход не меняется.
 */

function option(
  id: string,
  name: string,
  category: ServiceCategory
): SpecializationOption {
  return { id, name, description: null, category }
}

const OPTIONS: SpecializationOption[] = [
  option('styling-2', 'Укладка феном', 'Styling'),
  option('haircut-2', 'Фейд', 'Haircut'),
  option('other-1', 'Бритьё головы', 'Other'),
  option('haircut-1', 'Классическая стрижка', 'Haircut'),
  option('beard-1', 'Оформление бороды', 'Beard'),
  option('coloring-1', 'Окрашивание', 'Coloring'),
  option('hair-treatment-1', 'Уход за волосами', 'HairTreatment'),
  option('styling-1', 'Укладка', 'Styling'),
]

describe('groupSpecializations', () => {
  it('возвращает шесть групп в порядке витрины', () => {
    const groups = groupSpecializations(OPTIONS)

    expect(groups.map((group) => group.category)).toStrictEqual([
      'Haircut',
      'Beard',
      'Coloring',
      'Styling',
      'HairTreatment',
      'Other',
    ])
  })

  it('пропускает пустые группы', () => {
    const groups = groupSpecializations([
      option('haircut-1', 'Классическая стрижка', 'Haircut'),
      option('other-1', 'Бритьё головы', 'Other'),
    ])

    expect(groups.map((group) => group.category)).toStrictEqual([
      'Haircut',
      'Other',
    ])
  })

  it('сохраняет порядок строк внутри группы', () => {
    const groups = groupSpecializations(OPTIONS)
    const byCategory = new Map(
      groups.map((group) => [group.category, group.options])
    )

    expect(byCategory.get('Haircut')?.map((row) => row.name)).toStrictEqual([
      'Фейд',
      'Классическая стрижка',
    ])
    expect(byCategory.get('Styling')?.map((row) => row.name)).toStrictEqual([
      'Укладка феном',
      'Укладка',
    ])
  })

  it('не мутирует входной массив', () => {
    const snapshot = structuredClone(OPTIONS)

    groupSpecializations(OPTIONS)

    expect(OPTIONS).toStrictEqual(snapshot)
  })
})
