/**
 * Тип места приёма: чем именно является место, куда барбер приглашает клиента.
 *
 * `Salon` — обычный барбершоп, `RentalStudio` — студия с арендой кресел,
 * `SoloStudio` — личный кабинет одного мастера, `Home` — приём на дому,
 * `Mobile` — выезд к клиенту. Тип приходит из API именем (`Salon`, `Home`, …),
 * поэтому здесь union, а не `enum`: `erasableSyntaxOnly` запрещает `enum` (§4).
 */

import { t } from '@/i18n'

export type PlaceKind =
  | 'Salon'
  | 'RentalStudio'
  | 'SoloStudio'
  | 'Home'
  | 'Mobile'

/** Русские подписи типа места: в API значения остаются английскими. */
export const PLACE_KIND_LABELS: Record<PlaceKind, string> = {
  Salon: 'Барбершоп',
  RentalStudio: 'Студия',
  SoloStudio: 'Кабинет',
  Home: 'На дому',
  Mobile: 'Выезд',
}

/** Подсказки радио-группы — по одной строке на вариант (§5.2). */
export const PLACE_KIND_HINTS: Record<PlaceKind, string> = {
  Salon: 'обычный салон со штатом',
  RentalStudio: 'сдаёт кресла другим барберам',
  SoloStudio: 'принимаете только вы',
  Home: 'адрес виден в карточке',
  Mobile: 'приезжаете к клиенту',
}

/** Порядок радио-группы: от общего барбершопа к частному выезду. */
export const PLACE_KIND_ORDER: readonly PlaceKind[] = [
  'Salon',
  'RentalStudio',
  'SoloStudio',
  'Home',
  'Mobile',
]

/** Проверка значения из ответа API: неизвестный `kind` — битый контракт (§9.1). */
export function isPlaceKind(value: unknown): value is PlaceKind {
  return (
    typeof value === 'string' &&
    PLACE_KIND_ORDER.includes(value as PlaceKind)
  )
}

/**
 * Место вообще может быть показано в каталоге.
 *
 * `Home` и `Mobile` скрыты всегда: адрес на дому и выезд не публикуются в каталоге,
 * а скрытое от каталога место не проходит модерацию. `SoloStudio` бывает и
 * публичным, и скрытым — это решает барбер флагом `isListed`.
 */
export function isListedAllowed(kind: PlaceKind): boolean {
  return kind !== 'Home' && kind !== 'Mobile'
}

/**
 * Значение «Показывать в каталоге» по умолчанию для нового места.
 *
 * Барбершоп и студия без каталога не имеют смысла (и домен это запрещает),
 * кабинет по умолчанию скрыт, потому что кабинет чаще личный.
 */
export function defaultIsListed(kind: PlaceKind): boolean {
  return kind === 'Salon' || kind === 'RentalStudio'
}

/**
 * Подпись места в карточке барбера и в шапке профиля.
 *
 * Для `Home`/`Mobile` названия места у клиента нет — показываем понятную фразу
 * с городом; для остальных — название, город и тип.
 */
export function placeKindCaption(
  kind: PlaceKind,
  salonName: string | null,
  city: string | null
): string {
  if (kind === 'Home') {
    return city ? t('На дому в {city}', { city }) : t('На дому')
  }

  if (kind === 'Mobile') {
    return t('Выезд по городу')
  }

  const place = [salonName, city].filter(Boolean).join(' · ')

  return place
    ? `${place} · ${t(PLACE_KIND_LABELS[kind])}`
    : t(PLACE_KIND_LABELS[kind])
}
