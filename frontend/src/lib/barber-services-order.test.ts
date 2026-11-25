import { describe, expect, it } from 'vitest'

import { sortBarberServices } from '@/lib/barber-services-order'
import type { ServiceCategory } from '@/lib/barber-profile-labels'
import type { BarberProfileService } from '@/lib/catalog'

/**
 * Базовый сервис: конкретную услугу задаёт `name`, остальное — валидные
 * значения, чтобы фикстура проходила тип `BarberProfileService`.
 */
function service(
  name: string,
  overrides: Partial<BarberProfileService> = {}
): BarberProfileService {
  return {
    barberServiceId: `service-${name}`,
    hairServiceId: `hair-${name}`,
    name,
    category: 'Haircut',
    specializationId: 'specialization-barbering',
    specializationName: 'Барберинг',
    defaultDurationMinutes: 30,
    price: 1000,
    durationMinutes: 30,
    isActive: true,
    ...overrides,
  }
}

function order(services: readonly BarberProfileService[]): string[] {
  return sortBarberServices(services).map((item) => item.name)
}

function category(
  category: ServiceCategory,
  name: string
): BarberProfileService {
  return service(name, {
    category,
    specializationId: `specialization-${category}`,
    specializationName: name,
  })
}

describe('sortBarberServices', () => {
  it('ставит категории в каталожном порядке, а не по алфавиту', () => {
    const services = [
      category('Other', 'Другое'),
      category('HairTreatment', 'Уход'),
      category('Styling', 'Укладка'),
      category('Coloring', 'Окрашивание'),
      category('Beard', 'Борода'),
      category('Haircut', 'Стрижка'),
    ]

    expect(order(services)).toStrictEqual([
      'Стрижка',
      'Борода',
      'Окрашивание',
      'Укладка',
      'Уход',
      'Другое',
    ])
  })

  it('внутри категории сортирует по специализации, затем по названию', () => {
    const services = [
      service('Яблоко', { specializationName: 'Стилист' }),
      service('Вишня', { specializationName: 'Барберинг' }),
      service('Арбуз', { specializationName: 'Барберинг' }),
      service('Банан', { specializationName: 'Барберинг' }),
    ]

    expect(order(services)).toStrictEqual(['Арбуз', 'Банан', 'Вишня', 'Яблоко'])
  })

  it('при полном равенстве сортирует по barberServiceId', () => {
    const services = [
      service('Фейд', { barberServiceId: 'service-c' }),
      service('Фейд', { barberServiceId: 'service-a' }),
      service('Фейд', { barberServiceId: 'service-b' }),
    ]

    expect(
      sortBarberServices(services).map((item) => item.barberServiceId)
    ).toStrictEqual(['service-a', 'service-b', 'service-c'])
  })

  it('не мутирует входной массив и возвращает новый', () => {
    const services = [
      category('Other', 'Другое'),
      category('Haircut', 'Стрижка'),
    ]
    const original = [...services]

    const sorted = sortBarberServices(services)

    expect(services).toStrictEqual(original)
    expect(sorted).not.toBe(services)
    expect(services.map((item) => item.name)).toStrictEqual([
      'Другое',
      'Стрижка',
    ])
  })
})
