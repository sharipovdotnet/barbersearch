import { describe, expect, it } from 'vitest'

import { groupBarberServices } from '@/lib/barber-service-groups'
import type { BarberProfileService } from '@/lib/catalog'

/**
 * Чистая группировка прайса барбера: группы — по алфавиту названий
 * специализаций, внутри группы — порядок API. Строки helper не копирует и не
 * меняет, активность не проверяет — это делает карточка.
 */

function service(
  name: string,
  specializationId: string,
  specializationName: string,
  overrides: Partial<BarberProfileService> = {}
): BarberProfileService {
  return {
    barberServiceId: `service-${specializationId}-${name}`,
    hairServiceId: `hair-${specializationId}-${name}`,
    name,
    category: 'Haircut',
    specializationId,
    specializationName,
    defaultDurationMinutes: 30,
    price: 1000,
    durationMinutes: 30,
    isActive: true,
    ...overrides,
  }
}

function groupIds(groups: ReturnType<typeof groupBarberServices>): string[] {
  return groups.map((group) => group.specializationId)
}

describe('groupBarberServices', () => {
  it('ставит группы по алфавиту названий, а не по порядку API', () => {
    // Порядок API — Стрижка, Борода, Афро: алфавитный результат от него отличается
    // и ловит возврат к сортировке по порядку ответа.
    const services = [
      service('Стрижка', 'specialization-haircut', 'Стрижка'),
      service('Оформление бороды', 'specialization-beard', 'Борода'),
      service('Афрокудри', 'specialization-afro', 'Афро'),
    ]

    const groups = groupBarberServices(services)

    expect(groups.map((group) => group.specializationName)).toStrictEqual([
      'Афро',
      'Борода',
      'Стрижка',
    ])
    expect(groupIds(groups)).toStrictEqual([
      'specialization-afro',
      'specialization-beard',
      'specialization-haircut',
    ])
  })

  it('при совпадении названий сортирует группы по specializationId', () => {
    const services = [
      service('Фейд', 'specialization-b', 'Барберинг'),
      service('Стрижка', 'specialization-a', 'Барберинг'),
    ]

    const groups = groupBarberServices(services)

    expect(groupIds(groups)).toStrictEqual([
      'specialization-a',
      'specialization-b',
    ])
  })

  it('оставляет одноимённые специализации разными группами', () => {
    // Название группы не ключ: две разные специализации могут называться
    // одинаково, и склеивать их услуги в один `optgroup` нельзя.
    const services = [
      service('Фейд', 'specialization-1', 'Барберинг'),
      service('Стрижка', 'specialization-2', 'Барберинг'),
    ]

    const groups = groupBarberServices(services)

    expect(groups).toHaveLength(2)
    expect(groupIds(groups)).toStrictEqual([
      'specialization-1',
      'specialization-2',
    ])
    expect(
      groups.map((group) => group.services.map((row) => row.name))
    ).toStrictEqual([['Фейд'], ['Стрижка']])
  })

  it('собирает услуги одной специализации в одну группу', () => {
    const services = [
      service('Фейд', 'specialization-haircut', 'Барберинг'),
      service('Оформление бороды', 'specialization-beard', 'Борода'),
      service('Стрижка', 'specialization-haircut', 'Барберинг'),
    ]

    const groups = groupBarberServices(services)

    expect(groups).toHaveLength(2)
    expect(groups[0]?.specializationId).toBe('specialization-haircut')
    expect(groups[0]?.services).toHaveLength(2)
    expect(groupIds(groups)).toStrictEqual([
      'specialization-haircut',
      'specialization-beard',
    ])
  })

  it('сохраняет порядок услуг внутри группы', () => {
    // «Фейд» идёт раньше «Стрижки» в ответе API, хотя по алфавиту наоборот:
    // helper не пересортировывает строки внутри группы.
    const services = [
      service('Фейд', 'specialization-haircut', 'Барберинг'),
      service('Стрижка', 'specialization-haircut', 'Барберинг'),
      service('Оформление бороды', 'specialization-beard', 'Борода'),
    ]

    const groups = groupBarberServices(services)
    const haircutGroup = groups.find(
      (group) => group.specializationId === 'specialization-haircut'
    )

    expect(haircutGroup?.services.map((row) => row.name)).toStrictEqual([
      'Фейд',
      'Стрижка',
    ])
  })

  it('возвращает пустой массив для пустого входа', () => {
    expect(groupBarberServices([])).toStrictEqual([])
  })

  it('не мутирует вход и ссылается на исходные строки', () => {
    // Замороженный массив: сортировка на месте упала бы в strict mode.
    const services = Object.freeze([
      service('Оформление бороды', 'specialization-beard', 'Борода'),
      service('Фейд', 'specialization-haircut', 'Барберинг'),
      service('Стрижка', 'specialization-haircut', 'Барберинг'),
    ])
    const snapshot = structuredClone(services)

    const groups = groupBarberServices(services)

    expect(groupIds(groups)).toStrictEqual([
      'specialization-haircut',
      'specialization-beard',
    ])
    expect(services).toStrictEqual(snapshot)
    // Группы держат ссылки на те же строки, а не копии.
    expect(groups[0]?.services[0]).toBe(services[1])
    expect(groups[0]?.services[1]).toBe(services[2])
  })
})
