import { ChevronDown, ChevronUp } from 'lucide-react'
import { useId, useState } from 'react'

import { Button } from '@/components/ui/button'
import {
  ADMIN_AUDIT_ENTITY_LABELS,
  ADMIN_AUDIT_FIELD_LABELS,
  formatAuditAction,
  formatAuditValue,
  type AdminAuditActor,
  type AdminAuditChange,
  type AdminAuditEntry,
  type AdminAuditEntityType,
} from '@/lib/admin-audit'
import { formatShortDateTime } from '@/lib/format'

/**
 * Строка журнала: время, автор, сущность и действие; diff раскрывается кнопкой.
 *
 * Имя сущности не резолвится: строка несёт `entity_type` и короткий `entity_id`
 * (полный — в `title`), потому что сущность могла быть переименована или удалена.
 * Раскрытие независимое: у каждой строки своё состояние, фокус остаётся на кнопке.
 */

/** Длина короткого id: полный остаётся в `title`, а не растягивает строку на 320. */
const SHORT_ID_LENGTH = 8

/** Автор: `null` — системная запись, `email: null` — аккаунт уже удалён. */
function actorLabel(actor: AdminAuditActor | null): string {
  if (actor === null) {
    return 'Система'
  }

  return actor.email ?? 'Удалённый пользователь'
}

/** Подпись типа сущности; неизвестный тип показывается строкой сервера как есть. */
function entityLabel(entityType: string): string {
  return (
    ADMIN_AUDIT_ENTITY_LABELS[entityType as AdminAuditEntityType] ?? entityType
  )
}

/** Русское имя поля; неизвестное поле — camelCase как пришло. */
function fieldLabel(field: string): string {
  return ADMIN_AUDIT_FIELD_LABELS[field] ?? field
}

export function AuditEntry({ entry }: { entry: AdminAuditEntry }) {
  const [isExpanded, setIsExpanded] = useState(false)
  const diffId = useId()

  return (
    <li
      // Идентификатор записи аудита: только для E2E-локаторов строки (журнал
      // пишет сервер, других стабильных признаков у строки нет). На вид и
      // доступность не влияет.
      data-entry-id={entry.id}
      className="min-w-0 rounded-xl border px-3 py-3 sm:px-4"
    >
      <div className="flex min-w-0 flex-col items-start gap-2 sm:flex-row sm:justify-between">
        <div className="min-w-0 flex-1 space-y-1">
          <p className="min-w-0 text-sm">
            <time
              dateTime={entry.occurredAt}
              className="text-muted-foreground tabular-nums"
            >
              {formatShortDateTime(entry.occurredAt)}
            </time>
            <span aria-hidden> · </span>
            <span className="break-words">{actorLabel(entry.actor)}</span>
          </p>

          <p className="min-w-0 text-xs text-muted-foreground">
            <span className="font-medium text-foreground">
              {entityLabel(entry.entityType)}
            </span>{' '}
            <span className="font-mono break-all" title={entry.entityId}>
              {entry.entityId.slice(0, SHORT_ID_LENGTH)}
            </span>
            <span aria-hidden> · </span>
            <span>{formatAuditAction(entry.action)}</span>
          </p>
        </div>

        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-11 shrink-0 sm:h-8"
          aria-expanded={isExpanded}
          aria-controls={diffId}
          onClick={() => setIsExpanded((previous) => !previous)}
        >
          {isExpanded ? (
            <ChevronUp data-icon="inline-start" aria-hidden />
          ) : (
            <ChevronDown data-icon="inline-start" aria-hidden />
          )}
          {isExpanded ? 'Скрыть изменения' : 'Показать изменения'}
        </Button>
      </div>

      {/* Панель diff'а остаётся в DOM и скрыта атрибутом `hidden`: `aria-controls`
          всегда ссылается на существующий элемент, а `aria-expanded` сообщает
          состояние. Так же ведут себя остальные раскрывающиеся блоки в проекте. */}
      <div id={diffId} hidden={!isExpanded} className="mt-3 border-t pt-3">
        {entry.changes.length === 0 ? (
          <p className="text-sm text-muted-foreground">Изменения не записаны.</p>
        ) : (
          <dl className="space-y-1 text-sm">
            {entry.changes.map((change: AdminAuditChange) => (
              <div
                key={change.field}
                className="grid gap-x-3 gap-y-0.5 sm:grid-cols-[minmax(0,12rem)_minmax(0,1fr)]"
              >
                <dt className="font-medium break-words">
                  {fieldLabel(change.field)}
                </dt>
                <dd className="min-w-0 break-words text-muted-foreground">
                  {formatAuditValue(change.field, change.from)}
                  {' → '}
                  {formatAuditValue(change.field, change.to)}
                </dd>
              </div>
            ))}
          </dl>
        )}
      </div>
    </li>
  )
}
