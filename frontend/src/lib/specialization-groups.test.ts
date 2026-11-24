import { describe, expect, it } from 'vitest'

import type { ServiceCategory } from '@/lib/barber-profile-labels'
import { groupSpecializations } from '@/lib/specialization-groups'
import type { SpecializationOption } from '@/lib/specializations'

/**
 * Чистая группировка специализаций: порядок групп — каталожный, внутри группы —
 * порядок API, пустые категории не возвращаются.
 */

function specialization(
  id: string,
  name: string,
  category: ServiceCategory
): SpecializationOption {
  return { id, name, description: null, category }
}

describe('groupSpecializations', () => {
  it('ставит группы в каталожном порядке, а не в порядке API и не по алфавиту', () => {
    // Вход перемешан (Other, Beard, Haircut): и порядок API, и алфавит названий
    // («Афро», «Борода», «Стрижка») дали бы другой результат.
    const specializations = [
      specialization('other', 'Афро', 'Other'),
      specialization('beard', 'Борода', 'Beard'),
      specialization('haircut', 'Стрижка', 'Haircut'),
    ]

    const groups = groupSpecializations(specializations)

    expect(groups.map((group) => group.category)).toStrictEqual([
      'Haircut',
      'Beard',
      'Other',
    ])
  })

  it('сохраняет порядок API внутри группы', () => {
    const specializations = [
      specialization('haircut-2', 'Фейд', 'Haircut'),
      specialization('haircut-1', 'Барберинг', 'Haircut'),
    ]

    const groups = groupSpecializations(specializations)

    // API вернул «Фейд» раньше «Барберинга», хотя по алфавиту наоборот.
    expect(groups).toHaveLength(1)
    expect(
      groups[0]?.specializations.map((row) => row.id)
    ).toStrictEqual(['haircut-2', 'haircut-1'])
  })

  it('пропускает пустые категории', () => {
    const specializations = [
      specialization('haircut', 'Барберинг', 'Haircut'),
      specialization('other', 'Афрокудри', 'Other'),
    ]

    const groups = groupSpecializations(specializations)

    expect(groups.map((group) => group.category)).toStrictEqual([
      'Haircut',
      'Other',
    ])
  })

  it('не мутирует вход и возвращает ссылки на исходные строки', () => {
    const specializations = [
      specialization('other', 'Афрокудри', 'Other'),
      specialization('haircut', 'Барберинг', 'Haircut'),
    ]
    const snapshot = structuredClone(specializations)

    const groups = groupSpecializations(specializations)

    expect(specializations).toStrictEqual(snapshot)
    expect(groups[0]?.specializations[0]).toBe(specializations[1])
    expect(groups[1]?.specializations[0]).toBe(specializations[0])
  })
})
