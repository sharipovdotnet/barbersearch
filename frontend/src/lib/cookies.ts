/**
 * Чтение и запись cookie, которые ставит сам клиент.
 *
 * Клиентских cookie две, и обе не являются учётными данными: признак «Запомнить
 * меня» (`barbersearch.remember`, §9.4) и отметка о показе уведомления
 * (`barbersearch.cookie-notice`). Сессионные cookie Сервиса — `HttpOnly`, JavaScript
 * их не видит и не должен.
 */

/** Значение cookie по имени или `null`, если cookie нет. */
export function readCookie(name: string): string | null {
  const prefix = `${name}=`

  for (const part of document.cookie.split(';')) {
    const entry = part.trim()

    if (entry.startsWith(prefix)) {
      return decodeURIComponent(entry.slice(prefix.length))
    }
  }

  return null
}

/**
 * Ставит клиентскую cookie; `maxAgeSeconds = 0` удаляет её.
 *
 * `Secure` добавляется только на https: на http-порту разработки браузер такую
 * cookie отбросил бы (§5.1). `SameSite=Lax` — тот же режим, что у серверных cookie,
 * поэтому клиентская cookie не расширяет область отправки.
 */
export function writeCookie(
  name: string,
  value: string,
  maxAgeSeconds: number
): void {
  const secure = window.location.protocol === 'https:' ? '; Secure' : ''

  document.cookie = `${name}=${value}; Path=/; SameSite=Lax; Max-Age=${maxAgeSeconds}${secure}`
}
