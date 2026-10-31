/**
 * Подсказка «Запомнить меня».
 *
 * Браузер не отдаёт JS признак «сессия была»: refresh-cookie HttpOnly. Звать refresh на
 * каждом анонимном заходе нельзя — лимитер auth 10/мин на IP и лишние `401` в логах.
 * Поэтому клиент ставит свою cookie-подсказку: это не credential, доступа она не даёт и PII
 * не содержит. Подделка приводит лишь к одной неудачной попытке refresh.
 */

const REMEMBER_COOKIE = 'barbersearch.remember'

/** 14 дней — столько же живёт refresh-токен на сервере (`RefreshTokenDays`). */
const REMEMBER_MAX_AGE_SECONDS = 1_209_600

/** Была ли сессия запомнена при последнем входе. */
export function hasRememberHint(): boolean {
  return readCookie(REMEMBER_COOKIE) === '1'
}

/** Отметить сессию как запомненную: вход с «Запомнить меня» и успешное продление. */
export function markRememberedSession(): void {
  writeCookie('1', REMEMBER_MAX_AGE_SECONDS)
}

/** Снять подсказку: выход, вход без «Запомнить меня», терминальный сбой продления. */
export function clearRememberHint(): void {
  writeCookie('', 0)
}

function readCookie(name: string): string | null {
  const prefix = `${name}=`

  for (const part of document.cookie.split(';')) {
    const entry = part.trim()
    if (entry.startsWith(prefix)) {
      return decodeURIComponent(entry.slice(prefix.length))
    }
  }

  return null
}

// Без HttpOnly: cookie читает клиент. Secure — только на https, иначе dev-порт её не сохранит.
function writeCookie(value: string, maxAgeSeconds: number): void {
  const secure = window.location.protocol === 'https:' ? '; Secure' : ''

  document.cookie = `${REMEMBER_COOKIE}=${value}; Path=/; SameSite=Lax; Max-Age=${maxAgeSeconds}${secure}`
}
