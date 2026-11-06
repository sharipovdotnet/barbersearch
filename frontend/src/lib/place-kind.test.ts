import { describe, expect, it } from 'vitest'

import {
  defaultIsListed,
  isListedAllowed,
  isPlaceKind,
  PLACE_KIND_HINTS,
  PLACE_KIND_LABELS,
  PLACE_KIND_ORDER,
  placeKindCaption,
  type PlaceKind,
} from '@/lib/place-kind'

/**
 * Тип места — словарь и правила каталога.
 *
 * Значения приходят из API именами, поэтому проверяем и подписи, и порядок
 * радио-группы, и единственное правило домена: `Home`/`Mobile` в каталоге
 * не показываются никогда.
 */

describe('place-kind', () => {
  it('переводит все пять значений на русский', () => {
    expect(PLACE_KIND_LABELS).toStrictEqual({
      Salon: 'Барбершоп',
      RentalStudio: 'Студия',
      SoloStudio: 'Кабинет',
      Home: 'На дому',
      Mobile: 'Выезд',
    })
    expect(PLACE_KIND_HINTS.Mobile).toBe('приезжаете к клиенту')
  })

  it('держит порядок радио-группы от барбершопа к выезду', () => {
    expect(PLACE_KIND_ORDER).toStrictEqual([
      'Salon',
      'RentalStudio',
      'SoloStudio',
      'Home',
      'Mobile',
    ])
  })

  it('разрешает каталог только барбершопу, студии и кабинету', () => {
    const allowed = PLACE_KIND_ORDER.filter(isListedAllowed)

    expect(allowed).toStrictEqual(['Salon', 'RentalStudio', 'SoloStudio'])
    expect(isListedAllowed('Home')).toBe(false)
    expect(isListedAllowed('Mobile')).toBe(false)
  })

  it('включает каталог по умолчанию для барбершопа и студии', () => {
    expect(defaultIsListed('Salon')).toBe(true)
    expect(defaultIsListed('RentalStudio')).toBe(true)
    expect(defaultIsListed('SoloStudio')).toBe(false)
    expect(defaultIsListed('Home')).toBe(false)
    expect(defaultIsListed('Mobile')).toBe(false)
  })

  it('узнаёт только известные значения', () => {
    for (const kind of PLACE_KIND_ORDER) {
      expect(isPlaceKind(kind)).toBe(true)
    }

    expect(isPlaceKind('Penthouse')).toBe(false)
    expect(isPlaceKind(4)).toBe(false)
    expect(isPlaceKind(null)).toBe(false)
  })

  it('подписывает место в карточке: дом и выезд — без названия', () => {
    expect(placeKindCaption('Home', 'Квартира', 'Москва')).toBe(
      'На дому в Москва'
    )
    expect(placeKindCaption('Home', null, null)).toBe('На дому')
    expect(placeKindCaption('Mobile', 'Выезд', 'Москва')).toBe('Выезд по городу')
    expect(placeKindCaption('SoloStudio', 'Кабинет на Ленина', 'Москва')).toBe(
      'Кабинет на Ленина · Москва · Кабинет'
    )
    expect(placeKindCaption('Salon', null, null)).toBe('Барбершоп')

    // Тип сужает union: список ниже не компилируется с чужим значением.
    const kinds: PlaceKind[] = [...PLACE_KIND_ORDER]
    expect(kinds).toHaveLength(5)
  })
})
