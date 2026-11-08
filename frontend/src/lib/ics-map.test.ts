import { describe, expect, it } from 'vitest'

import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'
import { mapIcsOccurrences } from '@/lib/ics-map'
import type { IcsOccurrence, IcsParseOptions } from '@/lib/ics-parse'

/**
 * Маппинг вхождений календаря: эвристики текста, время салона и подсказки.
 *
 * `now` зафиксирован (8 ноября 2026, 15:00 UTC), события лежат 12 ноября.
 * Прошедшие и слишком далёкие строки здесь не проверяются: `past`/`tooFar`
 * считает `recomputeImportRows`, а не маппер.
 */

const NOW = Date.parse('2026-11-08T15:00:00Z')

const leninSalon: BarberProfileSalon = {
  id: 'profile-salon-s1',
  salonId: 's1',
  name: 'Салон на Ленина',
  address: 'Ленина 1',
  city: 'Екатеринбург',
  country: 'Russia',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'Salon',
  isPrimary: true,
  workingSchedules: [],
}

const berlinSalon: BarberProfileSalon = {
  id: 'profile-salon-s2',
  salonId: 's2',
  name: 'Берлин',
  address: 'Torstrasse 1',
  city: 'Берлин',
  country: 'Germany',
  timeZoneId: 'Europe/Berlin',
  kind: 'Salon',
  isPrimary: false,
  workingSchedules: [],
}

function service(
  overrides: Partial<BarberProfileService> = {}
): BarberProfileService {
  return {
    barberServiceId: 'service-haircut',
    hairServiceId: 'hair-haircut',
    name: 'Стрижка',
    category: 'Haircut',
    defaultDurationMinutes: 60,
    durationMinutes: 60,
    price: 2000,
    isActive: true,
    ...overrides,
  }
}

const haircut = service()

const beard = service({
  barberServiceId: 'service-beard',
  hairServiceId: 'hair-beard',
  name: 'Стрижка бороды',
  category: 'Beard',
  defaultDurationMinutes: 30,
  durationMinutes: 30,
  price: 1000,
})

const coloring = service({
  barberServiceId: 'service-coloring',
  hairServiceId: 'hair-coloring',
  name: 'Окрашивание',
  category: 'Coloring',
  defaultDurationMinutes: 120,
  durationMinutes: 120,
  price: 5000,
})

/** Услуга-двойник: то же название, другая строка `barber_services`. */
const haircutCopy = service({
  barberServiceId: 'service-haircut-copy',
  hairServiceId: 'hair-haircut-copy',
})

function options(overrides: Partial<IcsParseOptions> = {}): IcsParseOptions {
  return {
    services: [haircut, beard, coloring],
    salons: [leninSalon, berlinSalon],
    defaultSalonId: 's1',
    now: NOW,
    periodDays: 30,
    ...overrides,
  }
}

function occurrence(overrides: Partial<IcsOccurrence> = {}): IcsOccurrence {
  return {
    uid: 'uid-1',
    recurrenceId: null,
    summary: '',
    description: '',
    location: '',
    categories: '',
    startAt: '2026-11-12T05:00:00.000Z',
    endAt: '2026-11-12T06:00:00.000Z',
    durationMinutes: 60,
    sourceTimeZone: 'Asia/Yekaterinburg',
    isRecurring: false,
    status: 'confirmed',
    isFree: false,
    sequence: 0,
    ...overrides,
  }
}

/** Готовая строка: салон в `LOCATION`, телефон в описании, услуга в заголовке. */
function readyEvent(overrides: Partial<IcsOccurrence> = {}): IcsOccurrence {
  return occurrence({
    summary: 'Стрижка Иван Петров',
    description: 'Тел.: +7 999 123-45-67',
    location: 'Салон на Ленина',
    ...overrides,
  })
}

describe('mapIcsOccurrences: услуга и имя', () => {
  it('вырезает услугу из заголовка и оставляет имя', () => {
    const [draft] = mapIcsOccurrences(
      [occurrence({ summary: 'Стрижка — Иван Петров', description: 'Тел.: +7 999 123-45-67' })],
      options()
    )

    expect(draft.serviceId).toBe('service-haircut')
    expect(draft.firstName).toBe('Иван')
    expect(draft.lastName).toBe('Петров')
    expect(draft.issues).toStrictEqual([])
  })

  it('находит телефон в заголовке и нормализует его до +7', () => {
    const [draft] = mapIcsOccurrences(
      [occurrence({ summary: 'Иван Петров +7 999 123-45-67' })],
      options()
    )

    expect(draft.firstName).toBe('Иван')
    expect(draft.lastName).toBe('Петров')
    expect(draft.phone).toBe('+79991234567')
    expect(draft.issues).toStrictEqual(['noService'])
  })

  it('убирает дату и время из имени', () => {
    const [draft] = mapIcsOccurrences(
      [occurrence({ summary: 'Иван 12.11 10:00 стрижка' })],
      options()
    )

    expect(draft.firstName).toBe('Иван')
    expect(draft.lastName).toBeNull()
    expect(draft.serviceId).toBe('service-haircut')
  })

  it('ищет услугу в CATEGORIES, когда в заголовке её нет', () => {
    const [draft] = mapIcsOccurrences(
      [
        occurrence({
          summary: 'Иван Петров',
          categories: 'Стрижка',
          description: 'Тел.: +7 999 123-45-67',
          location: 'Салон на Ленина',
        }),
      ],
      options()
    )

    expect(draft.serviceId).toBe('service-haircut')
    expect(draft.hints).toStrictEqual(['phoneFromDescription'])
  })

  it('неоднозначная услуга даёт кандидатов и ошибку', () => {
    const [draft] = mapIcsOccurrences(
      [occurrence({ summary: 'Стрижка Иван Петров', description: 'Тел.: +7 999 123-45-67' })],
      options({ services: [haircut, haircutCopy] })
    )

    expect(draft.serviceId).toBeNull()
    expect(draft.serviceCandidates).toStrictEqual([
      'service-haircut',
      'service-haircut-copy',
    ])
    expect(draft.issues).toContain('ambiguousService')
  })
})

describe('mapIcsOccurrences: телефон', () => {
  it('берёт телефон из DESCRIPTION и подсказывает проверить', () => {
    const [draft] = mapIcsOccurrences(
      [occurrence({ summary: 'Иван Петров', description: 'Тел.: +7 999 123-45-67' })],
      options()
    )

    expect(draft.phone).toBe('+79991234567')
    expect(draft.hints).toContain('phoneFromDescription')
  })

  it('понимает HTML в DESCRIPTION: теги не мешают телефону', () => {
    const [draft] = mapIcsOccurrences(
      [
        occurrence({
          summary: 'Иван Петров',
          description: '<p>Тел.: +7 999 123-45-67</p>',
        }),
      ],
      options()
    )

    expect(draft.phone).toBe('+79991234567')
    expect(draft.hints).toContain('phoneFromDescription')
    expect(draft.raw).toBe('Иван Петров')
  })

  it('обрезанный номер не становится телефоном: только badPhone', () => {
    const longRaw = `7${'9'.repeat(39)}`
    const [draft] = mapIcsOccurrences(
      [occurrence({ summary: 'Иван Петров', description: `Тел.: ${longRaw}` })],
      options()
    )

    expect(draft.phone).toBeNull()
    expect(draft.issues).toContain('badPhone')
    // Нормализованный телефон всегда `+7` и десять цифр, поэтому обрезать в
    // черновике нечего; `phoneTruncated` остаётся защитой на будущее.
    expect(draft.hints).not.toContain('phoneTruncated')
  })
})

describe('mapIcsOccurrences: салон', () => {
  it('находит салон по названию в LOCATION', () => {
    const [draft] = mapIcsOccurrences([readyEvent()], options())

    expect(draft.salonId).toBe('s1')
    expect(draft.hints).not.toContain('salonFallback')
  })

  it('игнорирует встречу в LOCATION и берёт основной салон', () => {
    const [draft] = mapIcsOccurrences(
      [readyEvent({ location: 'Google Meet' })],
      options()
    )

    expect(draft.salonId).toBe('s1')
    expect(draft.hints).toContain('salonFallback')
  })

  it('находит салон по адресу в LOCATION', () => {
    const [draft] = mapIcsOccurrences(
      [readyEvent({ location: 'Ленина 1' })],
      options()
    )

    expect(draft.salonId).toBe('s1')
  })

  it('не добавляет подсказку о салоне, когда салон один', () => {
    const [draft] = mapIcsOccurrences(
      [readyEvent({ location: '' })],
      options({ salons: [leninSalon] })
    )

    expect(draft.salonId).toBe('s1')
    expect(draft.hints).not.toContain('salonFallback')
  })
})

describe('mapIcsOccurrences: ошибки и включение', () => {
  it('без телефона, услуги и имени ставит три ошибки', () => {
    const [draft] = mapIcsOccurrences([occurrence()], options())

    expect(draft.issues).toStrictEqual(['noPhone', 'noName', 'noService'])
    expect(draft.firstName).toBeNull()
    expect(draft.defaultIncluded).toBe(false)
    expect(draft.hints).toContain('noContact')
  })

  it('личное событие без телефона и услуги не выбрано по умолчанию', () => {
    const [draft] = mapIcsOccurrences(
      [occurrence({ summary: 'Обед' })],
      options()
    )

    expect(draft.hints).toContain('personal')
    expect(draft.defaultIncluded).toBe(false)
  })

  it('личное событие с телефоном остаётся выбранным', () => {
    const [draft] = mapIcsOccurrences(
      [
        occurrence({
          summary: 'Обед',
          description: 'Тел.: +7 999 123-45-67',
        }),
      ],
      options()
    )

    expect(draft.hints).toContain('personal')
    expect(draft.hints).not.toContain('noContact')
    expect(draft.defaultIncluded).toBe(true)
  })

  it('«свободно» исключает строку, но не удаляет её', () => {
    const drafts = mapIcsOccurrences(
      [readyEvent({ isFree: true })],
      options()
    )

    expect(drafts).toHaveLength(1)
    expect(drafts[0].hints).toContain('free')
    expect(drafts[0].defaultIncluded).toBe(false)
  })

  it('предварительная запись остаётся выбранной', () => {
    const [draft] = mapIcsOccurrences(
      [readyEvent({ status: 'tentative' })],
      options()
    )

    expect(draft.hints).toContain('tentative')
    expect(draft.defaultIncluded).toBe(true)
  })
})

describe('mapIcsOccurrences: длительность', () => {
  it('сохраняет конец события при расхождении с услугой', () => {
    const [draft] = mapIcsOccurrences(
      [
        readyEvent({
          endAt: '2026-11-12T05:45:00.000Z',
          durationMinutes: 45,
        }),
      ],
      options()
    )

    expect(draft.eventDurationMinutes).toBe(45)
    expect(draft.endAt).toBe('2026-11-12T05:45:00.000Z')
    expect(draft.hints).toContain('durationMismatch')
  })

  it('считает конец из длительности события, если конца нет', () => {
    const [draft] = mapIcsOccurrences(
      [readyEvent({ endAt: null, durationMinutes: 45 })],
      options()
    )

    expect(draft.endAt).toBe('2026-11-12T05:45:00.000Z')
    expect(draft.hints).toContain('durationMismatch')
  })

  it('падает на длительность услуги, если нет ни конца, ни длительности', () => {
    const [draft] = mapIcsOccurrences(
      [readyEvent({ endAt: null, durationMinutes: null })],
      options()
    )

    expect(draft.endAt).toBe('2026-11-12T06:00:00.000Z')
    expect(draft.hints).not.toContain('durationMismatch')
  })
})

describe('mapIcsOccurrences: лимиты длины', () => {
  it('обрезает длинное имя и подсказывает', () => {
    const [draft] = mapIcsOccurrences(
      [
        occurrence({
          summary: `${'А'.repeat(120)} Петров`,
          description: 'Тел.: +7 999 123-45-67',
          location: 'Салон на Ленина',
        }),
      ],
      options()
    )

    expect(draft.firstName).toHaveLength(100)
    expect(draft.lastName).toBe('Петров')
    expect(draft.hints).toContain('nameTruncated')
  })
})

describe('mapIcsOccurrences: часовой пояс', () => {
  const berlinEvent = readyEvent({
    startAt: '2026-11-12T09:00:00.000Z',
    endAt: '2026-11-12T10:00:00.000Z',
    sourceTimeZone: 'Europe/Berlin',
  })

  it('пересчитывает берлинское событие в пояс салона', () => {
    const [draft] = mapIcsOccurrences([berlinEvent], options())

    expect(draft.useEventTime).toBe(true)
    expect(draft.date).toBe('2026-11-12')
    expect(draft.time).toBe('14:00')
    expect(draft.hints).toStrictEqual([
      'timeZoneConverted',
      'phoneFromDescription',
    ])
  })

  it('не подсказывает про пересчёт, когда пояс совпадает с салоном', () => {
    const [draft] = mapIcsOccurrences(
      [{ ...berlinEvent, sourceTimeZone: 'Asia/Yekaterinburg' }],
      options()
    )

    expect(draft.time).toBe('14:00')
    expect(draft.hints).not.toContain('timeZoneConverted')
  })

  it('floating-событие сохраняет настенное время и не считает момент', () => {
    const [draft] = mapIcsOccurrences(
      [
        {
          ...berlinEvent,
          startAt: '2026-11-12T05:00:00.000Z',
          endAt: '2026-11-12T06:00:00.000Z',
          sourceTimeZone: null,
        },
      ],
      options()
    )

    expect(draft.useEventTime).toBe(false)
    expect(draft.date).toBe('2026-11-12')
    expect(draft.time).toBe('10:00')
    expect(draft.hints).not.toContain('timeZoneConverted')
  })

  it('показывает Берлин в поясе берлинского салона', () => {
    const [draft] = mapIcsOccurrences(
      [
        readyEvent({
          startAt: '2026-11-12T09:00:00.000Z',
          endAt: '2026-11-12T10:00:00.000Z',
          location: 'Берлин',
          sourceTimeZone: 'Europe/Berlin',
        }),
      ],
      options()
    )

    expect(draft.salonId).toBe('s2')
    expect(draft.time).toBe('10:00')
    expect(draft.hints).not.toContain('timeZoneConverted')
  })
})

describe('mapIcsOccurrences: поля строки', () => {
  it('нумерует строки по порядку и копирует идентичность события', () => {
    const drafts = mapIcsOccurrences(
      [
        readyEvent({ uid: 'uid-a', startAt: '2026-11-12T05:00:00.000Z' }),
        readyEvent({
          uid: 'uid-b',
          startAt: '2026-11-13T05:00:00.000Z',
          endAt: '2026-11-13T06:00:00.000Z',
          recurrenceId: '2026-11-13T05:00:00.000Z',
          isRecurring: true,
        }),
        readyEvent({ uid: 'uid-c', startAt: '2026-11-14T05:00:00.000Z' }),
      ],
      options()
    )

    expect(drafts.map((draft) => draft.line)).toStrictEqual([1, 2, 3])
    expect(drafts.map((draft) => draft.externalUid)).toStrictEqual([
      'uid-a',
      'uid-b',
      'uid-c',
    ])
    expect(drafts[1].recurrenceId).toBe('2026-11-13T05:00:00.000Z')
    expect(drafts[0].recurrenceId).toBeNull()
    expect(drafts[0].source).toBe('ics')
  })

  it('не хранит HTML в подписи, имени и телефоне', () => {
    const [draft] = mapIcsOccurrences(
      [
        occurrence({
          summary: '<b>Стрижка</b> — Иван Петров',
          description: '<p>Тел.: +7 999 123-45-67</p>',
          location: 'Салон на Ленина',
        }),
      ],
      options()
    )

    expect(draft.eventSummary).toBe('Стрижка — Иван Петров')
    expect(draft.raw).toBe('Стрижка — Иван Петров')
    expect(draft.firstName).toBe('Иван')
    expect(draft.lastName).toBe('Петров')

    for (const value of [draft.raw, draft.eventSummary, draft.firstName, draft.lastName, draft.phone]) {
      expect(value ?? '').not.toContain('<')
    }
  })

  it('не хранит цену в черновике: её читает UI из услуги', () => {
    const [draft] = mapIcsOccurrences([readyEvent()], options())
    const service = options().services.find(
      (item) => item.barberServiceId === draft.serviceId
    )

    expect(Object.keys(draft)).not.toContain('price')
    expect(service?.price).toBe(2000)
  })

  it('оставляет поле заметки клиента вне черновика', () => {
    const [draft] = mapIcsOccurrences(
      [occurrence({ summary: 'Иван Петров', description: 'Тел.: +7 999 123-45-67' })],
      options()
    )

    expect(Object.keys(draft)).not.toContain('customerNote')
    expect(Object.keys(draft)).not.toContain('customerId')
  })
})
