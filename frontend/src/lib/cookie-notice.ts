/**
 * Отметка о том, что уведомление о cookie показано.
 *
 * Уведомление информационное: Сервис ставит только строго необходимые cookie, и
 * согласия они не требуют (раздел 6 Политики). Поэтому запоминается не «согласие», а
 * факт показа — иначе плашка появлялась бы на каждой странице и мешала работе.
 */

import { readCookie, writeCookie } from '@/lib/cookies'

const NOTICE_COOKIE = 'barbersearch.cookie-notice'

/** Год: уведомление может измениться, но не чаще раза в год. */
const NOTICE_MAX_AGE_SECONDS = 31_536_000

/** Показывать ли уведомление: `false`, если пользователь уже его закрыл. */
export function shouldShowCookieNotice(): boolean {
  return readCookie(NOTICE_COOKIE) !== '1'
}

/** Запомнить, что уведомление закрыто. */
export function acknowledgeCookieNotice(): void {
  writeCookie(NOTICE_COOKIE, '1', NOTICE_MAX_AGE_SECONDS)
}
