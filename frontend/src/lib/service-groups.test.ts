import { describe, expect, it } from 'vitest'

import type { ServiceCategory } from '@/lib/barber-profile-labels'
import type { HairServiceOption } from '@/lib/hair-services'
import { groupHairServices } from '@/lib/service-groups'
import type { SpecializationOption } from '@/lib/specializations'

/**
 * Чистая группировка услуг: порядок групп — по алфавиту названий специализаций,
 * внутри группы — порядок услуг API, незнакомые специализации — одной хвостовой
 * группой.
 */

function specialization(
  id: string,
  name: string,
  category: ServiceCategory
): SpecializationOption {
  return { id, name, description: null, category }
}

function service(
  id: string,
  name: string,
  specializationId: string,
  category: ServiceCategory = 'Haircut'
): HairServiceOption {
  return {
    id,
    name,
    category,
    defaultDurationMinutes: 30,
    specializationId,
  }
}

describe('groupHairServices', () => {
  it('ставит группы по алфавиту названий, а не по категории специализации', () => {
    // Входной порядок совпадает со старым порядком категорий (Haircut, Beard,
    // Other), поэтому алфавитный результат («Афро», «Борода», «Стрижка») от него
    // отличается и ловит возврат к категорийной сортировке.
    const specializations = [
      specialization('haircut', 'Стрижка', 'Haircut'),
      specialization('beard', 'Борода', 'Beard'),
      specialization('other', 'Афро', 'Other'),
    ]
    const services = [
      service('haircut-service', 'Классическая стрижка', 'haircut', 'Haircut'),
      service('beard-service', 'Оформление бороды', 'beard', 'Beard'),
      service('other-service', 'Афрокудри', 'other', 'Other'),
    ]

    const groups = groupHairServices(services, specializations)

    expect(
      groups.map((group) => group.specialization?.id)
    ).toStrictEqual(['other', 'beard', 'haircut'])
  })

  it('сортирует специализации по названию, а не по порядку API', () => {
    const specializations = [
      specialization('haircut-2', 'Фейд', 'Haircut'),
      specialization('haircut-1', 'Барберинг', 'Haircut'),
    ]
    const services = [
      service('service-2', 'Фейд', 'haircut-2'),
      service('service-1', 'Стрижка', 'haircut-1'),
    ]

    const groups = groupHairServices(services, specializations)

    // API вернул «Фейд» раньше «Барберинга», но алфавит сильнее порядка ответа.
    expect(
      groups.map((group) => group.specialization?.id)
    ).toStrictEqual(['haircut-1', 'haircut-2'])
  })

  it('сохраняет порядок услуг внутри группы и пропускает пустые группы', () => {
    const specializations = [
      specialization('haircut-1', 'Барберинг', 'Haircut'),
      specialization('empty', 'Укладка', 'Styling'),
    ]
    const services = [
      service('fade', 'Фейд', 'haircut-1'),
      service('classic', 'Классическая стрижка', 'haircut-1'),
    ]

    const groups = groupHairServices(services, specializations)

    expect(groups).toHaveLength(1)
    expect(groups[0]?.services.map((row) => row.id)).toStrictEqual([
      'fade',
      'classic',
    ])
  })

  it('собирает услуги без известной специализации в последнюю группу', () => {
    const specializations = [
      specialization('haircut-1', 'Барберинг', 'Haircut'),
    ]
    const services = [
      service('unknown-1', 'Бритьё головы', 'deleted-1', 'Other'),
      service('known', 'Стрижка', 'haircut-1'),
      service('unknown-2', 'Камуфляж седины', 'deleted-2', 'Coloring'),
    ]

    const groups = groupHairServices(services, specializations)

    expect(
      groups.map((group) => group.specialization?.id ?? null)
    ).toStrictEqual(['haircut-1', null])
    expect(groups[1]?.services.map((row) => row.id)).toStrictEqual([
      'unknown-1',
      'unknown-2',
    ])
  })

  it('не мутирует входы и возвращает ссылки на исходные строки', () => {
    const specializations = [
      specialization('haircut-1', 'Барберинг', 'Haircut'),
    ]
    const services = [service('classic', 'Стрижка', 'haircut-1')]
    const specializationsSnapshot = structuredClone(specializations)
    const servicesSnapshot = structuredClone(services)

    const groups = groupHairServices(services, specializations)

    expect(specializations).toStrictEqual(specializationsSnapshot)
    expect(services).toStrictEqual(servicesSnapshot)
    expect(groups[0]?.specialization).toBe(specializations[0])
    expect(groups[0]?.services[0]).toBe(services[0])
  })
})

/**
 * Фильтр по специализации — это тот же справочник, сужённый до одной группы:
 * услуги других специализаций и «прочие» строки в селект не попадают.
 */
describe('groupHairServices, выбранная специализация', () => {
  const specializations = [
    specialization('haircut', 'Барберинг', 'Haircut'),
    specialization('beard', 'Борода', 'Beard'),
    specialization('empty', 'Укладка', 'Styling'),
  ]
  const services = [
    service('fade', 'Фейд', 'haircut'),
    service('beard-service', 'Оформление бороды', 'beard', 'Beard'),
    service('classic', 'Классическая стрижка', 'haircut'),
    // Строка без известной специализации: без фильтра она попадает в хвост.
    service('unknown', 'Бритьё головы', 'deleted', 'Other'),
  ]

  it('оставляет только группу выбранной специализации с её услугами', () => {
    const groups = groupHairServices(services, specializations, {
      specializationId: 'beard',
    })

    expect(groups.map((group) => group.specialization?.id)).toStrictEqual([
      'beard',
    ])
    expect(groups[0]?.services.map((row) => row.id)).toStrictEqual([
      'beard-service',
    ])
  })

  it('не показывает «Прочие услуги»: чужие строки в выбор не попадают', () => {
    const groups = groupHairServices(services, specializations, {
      specializationId: 'haircut',
    })

    expect(
      groups.map((group) => group.specialization?.id ?? null)
    ).toStrictEqual(['haircut'])
    // Внутри группы сохраняется порядок API, а не порядок specializations.
    expect(groups[0]?.services.map((row) => row.id)).toStrictEqual([
      'fade',
      'classic',
    ])
  })

  it('возвращает пустой список, когда у специализации нет услуг', () => {
    expect(
      groupHairServices(services, specializations, {
        specializationId: 'empty',
      })
    ).toStrictEqual([])
  })

  it('пустая строка — «фильтра нет»: форма так представляет невыбранное поле', () => {
    const groups = groupHairServices(services, specializations, {
      specializationId: '',
    })

    expect(
      groups.map((group) => group.specialization?.id ?? null)
    ).toStrictEqual(['haircut', 'beard', null])
  })

  it('не мутирует входы и ссылается на исходные строки', () => {
    const servicesSnapshot = structuredClone(services)
    const specializationsSnapshot = structuredClone(specializations)

    const groups = groupHairServices(services, specializations, {
      specializationId: 'beard',
    })

    expect(services).toStrictEqual(servicesSnapshot)
    expect(specializations).toStrictEqual(specializationsSnapshot)
    expect(groups[0]?.specialization).toBe(specializations[1])
    expect(groups[0]?.services[0]).toBe(services[1])
  })
})
