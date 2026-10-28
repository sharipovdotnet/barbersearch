/**
 * Кнопка «В календарь» без бэкенда: ICS-файл собирается на клиенте.
 *
 * Время пишется в UTC (`Z`), поэтому событие одинаково верно открывается в любом
 * часовом поясе — приложение уже показало его в поясе салона.
 */

export type CalendarEvent = {
  id: string
  title: string
  description: string
  location: string
  startAt: string
  endAt: string
}

function toUtcStamp(iso: string): string {
  const date = new Date(iso)
  const pad = (value: number) => String(value).padStart(2, '0')

  return [
    date.getUTCFullYear(),
    pad(date.getUTCMonth() + 1),
    pad(date.getUTCDate()),
    'T',
    pad(date.getUTCHours()),
    pad(date.getUTCMinutes()),
    pad(date.getUTCSeconds()),
    'Z',
  ].join('')
}

/** RFC 5545: обратный слэш экранируется первым, иначе съест следующие правила. */
function escapeText(value: string): string {
  return value
    .replace(/\\/g, '\\\\')
    .replace(/;/g, '\\;')
    .replace(/,/g, '\\,')
    .replace(/\r?\n/g, '\\n')
}

/** Строки ICS обязаны быть не длиннее 75 октетов и переносятся продолжением. */
function foldLine(line: string): string {
  const encoder = new TextEncoder()

  if (encoder.encode(line).length <= 75) {
    return line
  }

  const parts: string[] = []
  let current = ''
  let bytes = 0

  for (const character of line) {
    const size = encoder.encode(character).length
    // У строки-продолжения первый октет занят пробелом, поэтому полезных — 74.
    const limit = parts.length === 0 ? 75 : 74

    if (bytes + size > limit) {
      parts.push(current)
      current = character
      bytes = size
    } else {
      current += character
      bytes += size
    }
  }

  parts.push(current)

  return parts
    .map((part, index) => (index === 0 ? part : ` ${part}`))
    .join('\r\n')
}

export function buildCalendarFile(event: CalendarEvent): string {
  const lines = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//BarberSearch//Booking//RU',
    'CALSCALE:GREGORIAN',
    'METHOD:PUBLISH',
    'BEGIN:VEVENT',
    `UID:${event.id}@barbersearch`,
    `DTSTAMP:${toUtcStamp(new Date().toISOString())}`,
    `DTSTART:${toUtcStamp(event.startAt)}`,
    `DTEND:${toUtcStamp(event.endAt)}`,
    `SUMMARY:${escapeText(event.title)}`,
    `LOCATION:${escapeText(event.location)}`,
    `DESCRIPTION:${escapeText(event.description)}`,
    'END:VEVENT',
    'END:VCALENDAR',
  ]

  return `${lines.map(foldLine).join('\r\n')}\r\n`
}

export function downloadBookingCalendar(event: CalendarEvent) {
  const file = new Blob([buildCalendarFile(event)], {
    type: 'text/calendar;charset=utf-8',
  })
  const url = URL.createObjectURL(file)
  const link = document.createElement('a')

  link.href = url
  link.download = `barbersearch-${event.startAt.slice(0, 10)}.ics`
  link.rel = 'noopener'

  document.body.append(link)
  link.click()
  link.remove()

  window.setTimeout(() => URL.revokeObjectURL(url), 0)
}
