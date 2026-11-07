import { describe, expect, it } from 'vitest'

import {
  IMPORT_MAX_ROWS,
  parseImportText,
  type ImportDraft,
  type ImportParseOptions,
} from '@/lib/import-parse'
import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'

/**
 * Разбор вставленного списка — табличные случаи.
 *
 * Дата «сегодня» зафиксирована: 7 ноября 2026 — суббота, теперь 12:00 по
 * Екатеринбургу. Так «сб» — это сегодня, «пн» — ближайший понедельник,
 * а «время в прошлом» и «ровно +5 минут» попадают в один и тот же день.
 */

const TODAY = '2026-11-07'
const NOW = Date.parse('2026-11-07T12:00:00+05:00')

const YEKATERINBURG = 'Asia/Yekaterinburg'

function service(
  name: string,
  overrides: Partial<BarberProfileService> = {}
): BarberProfileService {
  return {
    barberServiceId: `service-${name}`,
    hairServiceId: `hair-${name}`,
    name,
    category: 'Haircut',
    defaultDurationMinutes: 60,
    durationMinutes: 60,
    price: 2000,
    isActive: true,
    ...overrides,
  }
}

function salon(
  salonId: string,
  name: string,
  city: string,
  timeZoneId: string,
  isPrimary = false
): BarberProfileSalon {
  return {
    id: `profile-salon-${salonId}`,
    salonId,
    name,
    address: 'ул. Ленина, 1',
    city,
    country: 'Russia',
    timeZoneId,
    kind: 'Salon',
    isPrimary,
    workingSchedules: [],
  }
}

const haircut = service('Стрижка')
const beard = service('Борода', { category: 'Beard', durationMinutes: 30, price: 1200 })
const massage = service('Массаж', { isActive: false })

const leninSalon = salon('salon-lenin', 'Салон на Ленина', 'Екатеринбург', YEKATERINBURG, true)
const northSalon = salon('salon-north', 'Студия Север', 'Москва', 'Europe/Moscow')

const options: ImportParseOptions = {
  services: [haircut, beard, massage],
  salons: [leninSalon, northSalon],
  today: TODAY,
  now: NOW,
  defaultSalonId: leninSalon.salonId,
}

function parse(text: string, overrides: Partial<ImportParseOptions> = {}) {
  return parseImportText(text, { ...options, ...overrides })
}

function onlyRow(text: string, overrides: Partial<ImportParseOptions> = {}): ImportDraft {
  const result = parse(text, overrides)

  expect(result.failure).toBeNull()
  expect(result.rows).toHaveLength(1)

  return result.rows[0]
}

describe('parseImportText: одна строка', () => {
  it('разбирает «10:00 Иван Петров +7 999 123-45-67 стрижка» полностью', () => {
    const row = onlyRow(
      '12 ноября\n10:00 Иван Петров +7 999 123-45-67 стрижка'
    )

    expect(row).toMatchObject({
      date: '2026-11-12',
      time: '10:00',
      firstName: 'Иван',
      lastName: 'Петров',
      phone: '+79991234567',
      salonId: leninSalon.salonId,
      serviceId: haircut.barberServiceId,
      startAt: '2026-11-12T05:00:00.000Z',
      endAt: '2026-11-12T06:00:00.000Z',
      issues: [],
      line: 2,
    })
  })

  it('не зависит от порядка частей: «Иван 14:00 стрижка»', () => {
    const row = onlyRow('12 ноября\nИван 14:00 стрижка +79990001122')

    expect(row).toMatchObject({
      date: '2026-11-12',
      time: '14:00',
      firstName: 'Иван',
      lastName: null,
      startAt: '2026-11-12T09:00:00.000Z',
      issues: [],
    })
  })

  it('понимает время через дефис: «14-00»', () => {
    const row = onlyRow('12 ноября\n14-00 Иван стрижка')

    expect(row.time).toBe('14:00')
    expect(row.startAt).toBe('2026-11-12T09:00:00.000Z')
  })

  it('«14.05» — дата 14 мая, а не время', () => {
    const row = onlyRow('11:00 Иван 14.05 стрижка')

    expect(row.date).toBe('2027-05-14')
    expect(row.time).toBe('11:00')
  })

  it('строка «12 ноября» задаёт дату следующим и записи не создаёт', () => {
    const result = parse('12 ноября\n10:00 Иван стрижка\n11:00 Пётр борода')

    expect(result.failure).toBeNull()
    expect(result.rows).toHaveLength(2)
    expect(result.rows.map((row) => row.date)).toStrictEqual([
      '2026-11-12',
      '2026-11-12',
    ])
  })

  it('разбирает строку с пайпами: «12.11.2026 | 10:00 | Пётр | борода»', () => {
    const row = onlyRow('12.11.2026 | 10:00 | Пётр | борода')

    expect(row).toMatchObject({
      date: '2026-11-12',
      time: '10:00',
      firstName: 'Пётр',
      lastName: null,
      serviceId: beard.barberServiceId,
      salonId: leninSalon.salonId,
    })

    // Телефона в строке нет — так и говорим, а не выдумываем номер.
    expect(row.issues).toStrictEqual(['noPhone'])
  })

  it('«сб 12:30 Аня» — ближайшая суббота, для сегодня это сегодня', () => {
    const row = onlyRow('сб 12:30 Аня стрижка')

    expect(row.date).toBe('2026-11-07')
    expect(row.time).toBe('12:30')
  })

  it('день недели и явная дата: дата важнее, расхождение не ошибка', () => {
    const row = onlyRow('пн 12.11.2026 10:00 Аня стрижка +79990001122')

    expect(row.date).toBe('2026-11-12')
    expect(row.issues).toStrictEqual([])
  })

  it.each([
    ['8 (999) 123-45-67', '+79991234567'],
    ['+7 999 123-45-67', '+79991234567'],
    ['79991234567', '+79991234567'],
    ['9991234567', '+79991234567'],
    ['+7 999 123 45 67', '+79991234567'],
  ])('нормализует телефон «%s» → %s', (phone, expected) => {
    const row = onlyRow(`12 ноября 10:00 Иван стрижка ${phone}`)

    expect(row.phone).toBe(expected)
    expect(row.time).toBe('10:00')
    expect(row.issues).toStrictEqual([])
  })

  it('обрезанный телефон — «похож на неполный», а не имя', () => {
    const row = onlyRow('12 ноября 10:00 Иван стрижка +7 999 123')

    expect(row.phone).toBe('+7 999 123')
    expect(row.issues).toContain('badPhone')
  })

  it('два похожих сервиса — услуга неоднозначна и оба кандидата', () => {
    const second = service('Стрижка!')

    const row = onlyRow('12 ноября 10:00 Аня стрижка', {
      services: [haircut, second],
    })

    expect(row.serviceId).toBeNull()
    expect(row.issues).toContain('ambiguousService')
    expect(row.serviceCandidates).toStrictEqual([
      haircut.barberServiceId,
      second.barberServiceId,
    ])
  })

  it('«Катя» без услуги — noService, остальное разобрано', () => {
    const row = onlyRow('12 ноября 10:00 Катя +79990001122')

    expect(row).toMatchObject({
      date: '2026-11-12',
      time: '10:00',
      firstName: 'Катя',
      phone: '+79990001122',
      serviceId: null,
      startAt: null,
    })

    expect(row.issues).toStrictEqual(['noService'])
  })

  it('выключенная услуга в кандидаты не попадает', () => {
    const row = onlyRow('12 ноября 10:00 Иван массаж +79990001122')

    expect(row.serviceId).toBeNull()
    expect(row.issues).toStrictEqual(['noService'])
  })

  it('салон по городу: «Москва» переключает пояс и время', () => {
    const row = onlyRow('12 ноября 10:00 Иван стрижка Москва +79990001122')

    expect(row.salonId).toBe(northSalon.salonId)
    expect(row.startAt).toBe('2026-11-12T07:00:00.000Z')
    expect(row.issues).toStrictEqual([])
  })

  it('без даты и без контекста — noDate', () => {
    const row = onlyRow('10:00 Иван стрижка +79990001122')

    expect(row.date).toBeNull()
    expect(row.issues).toStrictEqual(['noDate'])
  })

  it('без времени — noTime', () => {
    const row = onlyRow('12 ноября Иван стрижка +79990001122')

    expect(row.time).toBeNull()
    expect(row.issues).toStrictEqual(['noTime'])
  })

  it('«25:00» — не время: badTime, а не noTime', () => {
    const row = onlyRow('12 ноября 25:00 Иван стрижка +79990001122')

    expect(row.time).toBeNull()
    expect(row.issues).toStrictEqual(['badTime'])
  })

  it('время в прошлом — past', () => {
    const row = onlyRow('12 ноября 10:00 Иван стрижка +79990001122', {
      today: '2026-11-07',
      now: Date.parse('2026-11-12T12:00:00+05:00'),
    })

    expect(row.issues).toStrictEqual(['past'])
  })

  it('ровно через 5 минут — ещё не прошлое', () => {
    const row = onlyRow('сб 12:05 Иван стрижка +79990001122')

    expect(row.startAt).toBe('2026-11-07T07:05:00.000Z')
    expect(row.issues).toStrictEqual([])
  })

  it('прошедшая дата без года — следующий год', () => {
    const row = onlyRow('14.05 10:00 Иван стрижка')

    expect(row.date).toBe('2027-05-14')
  })

  it('29.02.2026 не существует — badDate', () => {
    const row = onlyRow('29.02.2026 10:00 Иван стрижка +79990001122')

    expect(row.date).toBeNull()
    expect(row.issues).toStrictEqual(['badDate'])
  })

  it('дыра перехода на летнее время — badTime', () => {
    const berlin = salon('salon-berlin', 'Берлин', 'Берлин', 'Europe/Berlin')

    const row = onlyRow('29.03.2026 02:30 Иван стрижка +79990001122', {
      salons: [berlin],
      defaultSalonId: berlin.salonId,
    })

    expect(row.startAt).toBeNull()
    expect(row.issues).toStrictEqual(['badTime'])
  })
})

describe('parseImportText: пачка', () => {
  it('шапка таблицы и пустые строки пропускаются молча', () => {
    const result = parse(
      'Дата | Время | Клиент\n\n12 ноября\n10:00 Иван стрижка +79990001122\n'
    )

    expect(result.rows).toHaveLength(1)
    expect(result.rows[0].firstName).toBe('Иван')
  })

  it('дубль в пачке — у второй строки duplicate, первая остаётся ready', () => {
    const result = parse(
      '12 ноября\n10:00 Иван стрижка +79990001122\n10:00 Пётр стрижка +79990003344\n11:00 Аня стрижка +79990005566'
    )

    expect(result.rows.map((row) => row.issues)).toStrictEqual([
      [],
      ['duplicate'],
      [],
    ])
  })

  it('тот же телефон в разное время — не дубль', () => {
    const result = parse(
      '12 ноября\n10:00 Иван стрижка +79990001122\n11:00 Мария борода +79990001122'
    )

    expect(result.rows[0].issues).toStrictEqual([])
    expect(result.rows[1].issues).toStrictEqual([])
  })

  it('то же время в другом салоне — не дубль', () => {
    const result = parse(
      '12 ноября\n10:00 Иван стрижка +79990001122\n10:00 Пётр стрижка Москва +79990003344'
    )

    expect(result.rows[0].issues).toStrictEqual([])
    expect(result.rows[1].issues).toStrictEqual([])
  })

  it('пример из Google Docs даёт столько же готовых строк, сколько записей', () => {
    const colouring = service('Окрашивание', { category: 'Coloring', durationMinutes: 120 })

    const result = parse(
      '12 ноября\n10:00 Иван Петров +7 999 123-45-67 стрижка\n11:30 Аня, окрашивание, +79990001122\n14:00 Пётр | борода',
      { services: [haircut, beard, colouring] }
    )

    expect(result.failure).toBeNull()
    expect(result.rows).toHaveLength(3)
    expect(result.rows.map((row) => row.issues)).toStrictEqual([
      [],
      [],
      ['noPhone'],
    ])
    expect(result.rows.map((row) => row.firstName)).toStrictEqual([
      'Иван',
      'Аня',
      'Пётр',
    ])
    expect(result.rows.map((row) => row.serviceId)).toStrictEqual([
      haircut.barberServiceId,
      colouring.barberServiceId,
      beard.barberServiceId,
    ])
  })

  it('пустой текст — empty', () => {
    expect(parse('   \n\n').failure).toBe('empty')
  })

  it(`${IMPORT_MAX_ROWS + 1}-я строка — лимит, разбора нет`, () => {
    const lines = ['12 ноября']

    for (let index = 0; index < IMPORT_MAX_ROWS; index += 1) {
      lines.push(`10:00 Клиент${index} стрижка`)
    }

    const result = parse(lines.join('\n'))

    expect(result.failure).toBe('tooManyRows')
    expect(result.rows).toStrictEqual([])
  })

  it('ровно 50 строк разбираются', () => {
    const lines = ['12 ноября']

    for (let index = 0; index < IMPORT_MAX_ROWS - 1; index += 1) {
      lines.push(`10:${String(index).padStart(2, '0')} Клиент${index} стрижка`)
    }

    expect(parse(lines.join('\n')).rows).toHaveLength(IMPORT_MAX_ROWS - 1)
  })
})
