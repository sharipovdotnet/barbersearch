/**
 * Правовые данные Сервиса: адрес Политики, реквизиты Оператора и перечень cookie.
 *
 * Политика обработки персональных данных — документ Оператора (часть 2 статьи 18.1
 * 152-ФЗ), а не текст интерфейса. Поэтому её данные собраны в одном месте, а перечень
 * cookie в Политике и перечень, который реально ставит Сервис, — один и тот же массив:
 * разойтись они не могут.
 *
 * Реквизиты в квадратных скобках заполняются при регистрации Оператора, до публикации
 * домена; дата редакции меняется вместе с любым изменением текста Политики.
 */

import { navigate } from '@/lib/router'

/** Адрес Политики: на него ссылаются подвал, форма регистрации и уведомление о cookie. */
export const PRIVACY_PATH = '/privacy'

/** Якорь раздела о cookie внутри Политики. */
export const COOKIES_SECTION_ID = 'cookies'

/** Дата редакции Политики: ISO — для `<time>`, подпись форматируется на странице. */
export const PRIVACY_POLICY_DATE = '2026-11-02'

/**
 * Реквизиты Оператора.
 *
 * Значения в квадратных скобках — поля, которые заполняет владелец Сервиса: закон
 * требует указать в Политике наименование, адрес и контакт для обращений, а придумать
 * их за Оператора нельзя.
 */
export const LEGAL_ENTITY = {
  name: '[наименование Оператора]',
  inn: '[ИНН]',
  ogrn: '[ОГРН / ОГРНИП]',
  address: '[адрес местонахождения]',
  email: '[email для обращений субъектов персональных данных]',
  phone: '[телефон]',
} as const

export const LEGAL_ENTITY_FIELDS = [
  { label: 'Наименование', value: LEGAL_ENTITY.name },
  { label: 'ИНН', value: LEGAL_ENTITY.inn },
  { label: 'ОГРН / ОГРНИП', value: LEGAL_ENTITY.ogrn },
  { label: 'Адрес', value: LEGAL_ENTITY.address },
  { label: 'Email', value: LEGAL_ENTITY.email },
  { label: 'Телефон', value: LEGAL_ENTITY.phone },
] as const

/** Одна запись перечня cookie: имя, назначение, срок жизни и атрибуты. */
export type CookieEntry = {
  name: string
  purpose: string
  term: string
  flags: string
}

/**
 * Полный перечень cookie Сервиса.
 *
 * Аналитических, рекламных, маркетинговых и сторонних cookie нет; появление любой
 * новой строки здесь означает, что Политика и уведомление должны быть обновлены.
 */
export const COOKIE_REGISTRY: readonly CookieEntry[] = [
  {
    name: 'barbersearch.auth (Development) / __Host-barbersearch',
    purpose:
      'Сессия вошедшего пользователя. Без неё личный кабинет и записи недоступны.',
    term: '8 часов с момента входа; при входе без «Запомнить меня» — до закрытия браузера.',
    flags: 'HttpOnly, Secure, SameSite=Lax, Path=/',
  },
  {
    name: 'barbersearch.refresh (Development) / __Host-barbersearch-refresh',
    purpose:
      'Продление сессии без повторного ввода пароля: refresh-токен, из которого Сервис выпускает новую сессионную cookie.',
    term: '14 дней.',
    flags: 'HttpOnly, Secure, SameSite=Lax, Path=/',
  },
  {
    name: '.AspNetCore.Antiforgery.*',
    purpose:
      'Защита изменяющих запросов от подделки (CSRF): пара к заголовку X-CSRF-TOKEN, который клиент получает перед каждым таким запросом.',
    term: 'До закрытия браузера.',
    flags: 'HttpOnly, SameSite=Lax, Path=/',
  },
  {
    name: 'barbersearch.remember',
    purpose:
      'Признак «Запомнить меня» (значение 1). Позволяет при загрузке страницы понять, нужно ли восстанавливать сессию. Не содержит идентификатора пользователя и не даёт доступа к аккаунту.',
    term: '14 дней.',
    flags: 'Не HttpOnly (читается клиентом), Secure на https, SameSite=Lax, Path=/',
  },
  {
    name: 'barbersearch.cookie-notice',
    purpose:
      'Отметка о том, что настоящее уведомление показано и закрыто. Без неё плашка появлялась бы при каждом переходе.',
    term: '1 год.',
    flags: 'Не HttpOnly (читается клиентом), Secure на https, SameSite=Lax, Path=/',
  },
]

/** Ровно `/privacy`; вложенные адреса остаются витрине, как и прочий неизвестный путь. */
export function isPrivacyPath(pathname: string): boolean {
  return pathname === PRIVACY_PATH
}

/**
 * Переход к разделу о cookie из подвала и уведомления.
 *
 * `navigate` меняет адрес, но не прокручивает страницу: если Политика уже открыта,
 * раздел приходится прокручивать вручную, иначе клик по ссылке ничего не делает.
 * Когда страница ещё не смонтирована (переход с витрины), элемента нет — прокрутку
 * выполнит `PrivacyPage` по якорю из адреса.
 */
export function openCookiesSection(): void {
  navigate(`${PRIVACY_PATH}#${COOKIES_SECTION_ID}`)
  document.getElementById(COOKIES_SECTION_ID)?.scrollIntoView({ block: 'start' })
}
