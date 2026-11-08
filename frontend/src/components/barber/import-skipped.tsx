import { formatEventsCount, formatShortDay } from '@/lib/format'
import type { IcsSkippedEvent, IcsSkippedReason } from '@/lib/ics-parse'

/**
 * «Пропущено N событий»: сводка причин и первые 50 событий списком.
 *
 * Пропущенные события нельзя создать — прошедшие, all-day, отменённые, без UID,
 * с неизвестным поясом или длиннее 12 часов. Поэтому у блока нет ни галочек, ни
 * кнопки «включить»: он объясняет, куда делись события из файла. Экспорт за два
 * года может дать тысячи прошедших событий, поэтому список обрезан — остальное
 * считается словами «и ещё N».
 */

const SHOWN_LIMIT = 50

type ReasonCopy =
  | { kind: 'fixed'; text: string }
  | { kind: 'adjective'; forms: readonly [string, string, string] }

/**
 * Формы прилагательных: `one` / `few` / `many`. Порядок — как у остальных
 * счётчиков проекта, но склонение считает `Intl.PluralRules`, а не остаток.
 */
const REASON_COPY: Record<IcsSkippedReason, ReasonCopy> = {
  past: { kind: 'adjective', forms: ['прошедшее', 'прошедших', 'прошедших'] },
  allDay: { kind: 'fixed', text: 'на весь день' },
  cancelled: {
    kind: 'adjective',
    forms: ['отменённое', 'отменённых', 'отменённых'],
  },
  outsidePeriod: { kind: 'fixed', text: 'за пределами периода' },
  tooLong: {
    kind: 'adjective',
    forms: ['слишком длинное', 'слишком длинных', 'слишком длинных'],
  },
  unknownTimeZone: { kind: 'fixed', text: 'с неизвестным часовым поясом' },
  noUid: { kind: 'fixed', text: 'без UID' },
  invalid: {
    kind: 'adjective',
    forms: ['повреждённое', 'повреждённых', 'повреждённых'],
  },
}

const REASON_ORDER: readonly IcsSkippedReason[] = [
  'past',
  'allDay',
  'cancelled',
  'outsidePeriod',
  'tooLong',
  'unknownTimeZone',
  'noUid',
  'invalid',
]

const pluralRules = new Intl.PluralRules('ru')

function adjectiveForm(
  count: number,
  forms: readonly [string, string, string]
): string {
  const category = pluralRules.select(count)

  if (category === 'one') {
    return forms[0]
  }

  return category === 'few' ? forms[1] : forms[2]
}

/** «2 прошедших», «1 на весь день» — строка сводки по причине. */
function reasonCountText(count: number, reason: IcsSkippedReason): string {
  const copy = REASON_COPY[reason]

  return copy.kind === 'fixed'
    ? `${count} ${copy.text}`
    : `${count} ${adjectiveForm(count, copy.forms)}`
}

/** Причина одного события: та же формулировка, но без числа. */
function reasonLabel(reason: IcsSkippedReason): string {
  const copy = REASON_COPY[reason]

  return copy.kind === 'fixed' ? copy.text : copy.forms[0]
}

function eventDate(event: IcsSkippedEvent): string {
  if (event.startAt === null) {
    return '—'
  }

  return formatShortDay(`${event.startAt.slice(0, 10)}T00:00:00Z`, 'UTC')
}

function eventSummary(event: IcsSkippedEvent): string {
  const summary = event.summary.trim()

  return summary === '' ? '—' : summary
}

export function ImportSkipped({ skipped }: { skipped: IcsSkippedEvent[] }) {
  if (skipped.length === 0) {
    return null
  }

  const counts = new Map<IcsSkippedReason, number>()

  for (const event of skipped) {
    counts.set(event.reason, (counts.get(event.reason) ?? 0) + 1)
  }

  const reasons = REASON_ORDER.filter((reason) => counts.has(reason))
  const shown = skipped.slice(0, SHOWN_LIMIT)
  const rest = skipped.length - shown.length

  return (
    <details className="rounded-xl border bg-card px-3 py-2 text-sm">
      <summary className="flex min-h-11 cursor-pointer items-center rounded-sm font-medium outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:min-h-0 sm:py-1">
        Пропущено {formatEventsCount(skipped.length)}
      </summary>

      <div className="mt-2 space-y-3">
        <ul className="space-y-1 break-words">
          {reasons.map((reason) => (
            <li key={reason}>
              {reasonCountText(counts.get(reason) ?? 0, reason)}
            </li>
          ))}
        </ul>

        <ul className="space-y-1 border-t pt-2 text-muted-foreground break-words">
          {shown.map((event, index) => (
            <li key={`${event.startAt ?? 'none'}:${event.summary}:${index}`}>
              <span className="text-foreground">{eventDate(event)}</span>
              {' · '}
              {eventSummary(event)}
              {' · '}
              {reasonLabel(event.reason)}
            </li>
          ))}
        </ul>

        {rest > 0 && (
          <p className="text-muted-foreground">и ещё {rest}</p>
        )}
      </div>
    </details>
  )
}
