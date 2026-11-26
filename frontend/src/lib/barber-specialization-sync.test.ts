import { describe, expect, it } from 'vitest'

import { diffBarberSpecializations } from '@/lib/barber-specialization-sync'
import type {
  BarberProfileService,
  BarberProfileSpecialization,
} from '@/lib/catalog'

/**
 * Строка прайса: значимы только `specializationId` и `isActive`, остальное —
 * валидные значения, чтобы фикстура проходила тип `BarberProfileService`.
 */
function service(
  specializationId: string,
  isActive = true,
  overrides: Partial<BarberProfileService> = {}
): BarberProfileService {
  return {
    barberServiceId: `barber-service-${specializationId}-${String(isActive)}`,
    hairServiceId: `hair-service-${specializationId}`,
    name: 'Услуга',
    category: 'Haircut',
    specializationId,
    specializationName: `Специализация ${specializationId}`,
    defaultDurationMinutes: 30,
    price: 1000,
    durationMinutes: 30,
    isActive,
    ...overrides,
  }
}

/** Тег профиля: `specializationId` — id из справочника, `id` — строка связи. */
function tag(specializationId: string): BarberProfileSpecialization {
  return {
    id: `barber-specialization-${specializationId}`,
    specializationId,
    name: `Специализация ${specializationId}`,
    description: null,
  }
}

describe('diffBarberSpecializations', () => {
  it('добавляет тег активной услуге, у которой тега ещё нет', () => {
    expect(diffBarberSpecializations([service('barbering')], [])).toStrictEqual(
      { add: ['barbering'], remove: [] }
    )
  })

  it('ничего не делает, когда у активной услуги тег уже есть', () => {
    expect(
      diffBarberSpecializations([service('barbering')], [tag('barbering')])
    ).toStrictEqual({ add: [], remove: [] })
  })

  it('убирает тег, когда единственная активная услуга скрыта', () => {
    expect(
      diffBarberSpecializations(
        [service('barbering', false)],
        [tag('barbering')]
      )
    ).toStrictEqual({ add: [], remove: ['barbering'] })
  })

  it('оставляет тег, пока у специализации есть вторая активная услуга', () => {
    expect(
      diffBarberSpecializations(
        [service('barbering', false), service('barbering', true)],
        [tag('barbering')]
      )
    ).toStrictEqual({ add: [], remove: [] })
  })

  it('не удаляет тег без единой строки услуги: это ручная настройка', () => {
    expect(
      diffBarberSpecializations(
        [service('barbering', false)],
        [tag('barbering'), tag('massage')]
      )
    ).toStrictEqual({ add: [], remove: ['barbering'] })
  })

  it('не добавляет тег скрытой услуге: скрытая строка тег не держит', () => {
    expect(
      diffBarberSpecializations([service('beard', false)], [])
    ).toStrictEqual({ add: [], remove: [] })
  })

  it('игнорирует пустой specializationId у услуги и у тега', () => {
    expect(
      diffBarberSpecializations(
        [service('', true), service('barbering', true)],
        [tag(''), tag('barbering')]
      )
    ).toStrictEqual({ add: [], remove: [] })
  })

  it('схлопывает повторяющиеся услуги в одно добавление', () => {
    expect(
      diffBarberSpecializations(
        [service('barbering'), service('barbering')],
        []
      )
    ).toStrictEqual({ add: ['barbering'], remove: [] })
  })

  it('не повторяет удаление одного тега, если он пришёл дважды', () => {
    expect(
      diffBarberSpecializations(
        [service('barbering', false)],
        [tag('barbering'), tag('barbering')]
      )
    ).toStrictEqual({ add: [], remove: ['barbering'] })
  })

  it('порядок add — по первому появлению в услугах, remove — по тегам', () => {
    expect(
      diffBarberSpecializations(
        [
          service('beard'),
          service('barbering'),
          service('beard'),
          service('coloring', false),
        ],
        [tag('coloring'), tag('barbering'), tag('massage')]
      )
    ).toStrictEqual({ add: ['beard'], remove: ['coloring'] })
  })

  it('не мутирует входные массивы', () => {
    const services = [service('beard'), service('barbering', false)]
    const specializations = [tag('barbering')]
    const servicesSnapshot = structuredClone(services)
    const specializationsSnapshot = structuredClone(specializations)

    diffBarberSpecializations(services, specializations)

    expect(services).toStrictEqual(servicesSnapshot)
    expect(specializations).toStrictEqual(specializationsSnapshot)
  })
})
