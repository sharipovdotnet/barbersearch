import { describe, expect, it } from 'vitest'

import type { ServiceCategory } from '@/lib/barber-profile-labels'
import type { HairServiceOption } from '@/lib/hair-services'
import { groupHairServices } from '@/lib/service-groups'
import type { SpecializationOption } from '@/lib/specializations'

/**
 * Чистая группировка услуг: порядок групп — по категории специализации, внутри
 * категории — порядок API, внутри группы — порядок услуг API, незнакомые
 * специализации — одной хвостовой группой.
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
  it('ставит группы в порядке SERVICE_CATEGORY_ORDER, а не в порядке входа', () => {
    const specializations = [
      specialization('other', 'Бритьё головы', 'Other'),
      specialization('beard', 'Борода', 'Beard'),
      specialization('haircut', 'Барберинг', 'Haircut'),
    ]
    const services = [
      service('other-service', 'Бритьё головы', 'other', 'Other'),
      service('beard-service', 'Оформление бороды', 'beard', 'Beard'),
      service('haircut-service', 'Стрижка', 'haircut', 'Haircut'),
    ]

    const groups = groupHairServices(services, specializations)

    expect(
      groups.map((group) => group.specialization?.id)
    ).toStrictEqual(['haircut', 'beard', 'other'])
  })

  it('внутри категории сохраняет порядок специализаций из API', () => {
    const specializations = [
      specialization('haircut-2', 'Фейд', 'Haircut'),
      specialization('haircut-1', 'Барберинг', 'Haircut'),
    ]
    const services = [
      service('service-2', 'Фейд', 'haircut-2'),
      service('service-1', 'Стрижка', 'haircut-1'),
    ]

    const groups = groupHairServices(services, specializations)

    expect(
      groups.map((group) => group.specialization?.id)
    ).toStrictEqual(['haircut-2', 'haircut-1'])
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
