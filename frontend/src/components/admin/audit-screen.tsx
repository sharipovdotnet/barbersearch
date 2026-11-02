import { Inbox, Loader2, RefreshCw, Search, TriangleAlert } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import { AuditEntry } from '@/components/admin/audit-entry'
import { ListPager } from '@/components/list-pager'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminAuditList } from '@/hooks/use-admin-audit-list'
import {
  ADMIN_AUDIT_ACTOR_MAX_LENGTH,
  ADMIN_AUDIT_ENTITY_LABELS,
  ADMIN_AUDIT_ENTITY_TYPES,
  type AdminAuditEntityType,
} from '@/lib/admin-audit'
import { formatClockTime } from '@/lib/format'
import { formatPageRange } from '@/lib/paging'

/**
 * `/admin/audit` — журнал изменений: чтение без единой операции записи.
 *
 * Строки листаются общим курсорным пейджером и не редактируются: кнопок действий
 * у записи нет и не появится. Фильтры (тип, автор, даты) живут в состоянии React,
 * а не в URL: email автора — PII, ему не место в истории браузера и логах прокси.
 * Даты фильтра трактуются как UTC-сутки, а время строки показывается в локальном
 * поясе браузера — это разные правила, и подпись фильтра говорит об этом прямо.
 */
export function AuditScreen() {
  const [entityType, setEntityType] = useState<AdminAuditEntityType>('all')
  const [actorInput, setActorInput] = useState('')
  const [actor, setActor] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  // Задержка перед запросом: 300 мс хватает, чтобы не искать на каждую букву.
  useEffect(() => {
    const trimmed = actorInput.trim()

    if (trimmed === actor) {
      return
    }

    const timer = window.setTimeout(() => setActor(trimmed), 300)

    return () => window.clearTimeout(timer)
  }, [actorInput, actor])

  const list = useAdminAuditList({ entityType, actor, from, to })
  const headingRef = useRef<HTMLHeadingElement | null>(null)

  const isFiltered =
    entityType !== 'all' || actorInput !== '' || from !== '' || to !== ''

  function resetFilters() {
    setEntityType('all')
    setActorInput('')
    setActor('')
    setFrom('')
    setTo('')
  }

  if (list.requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate returnTo="/admin/audit" onSignedIn={list.reload} />
      </div>
    )
  }

  const isFirstLoad = list.status === 'loading'
  const isEmpty = list.status === 'ready' && list.items.length === 0

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="scroll-mt-20 font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
          >
            Журнал
          </h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            История изменений, которую пишет сервер. Записи не редактируются.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {list.updatedAt !== null && (
            <span className="text-xs text-muted-foreground tabular-nums">
              Обновлено в {formatClockTime(new Date(list.updatedAt))}
            </span>
          )}
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={
              list.isRefreshing || list.isLoadingPage || list.status === 'loading'
            }
            onClick={list.refresh}
          >
            {list.isRefreshing ? (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            ) : (
              <RefreshCw data-icon="inline-start" aria-hidden />
            )}
            {list.isRefreshing ? 'Обновляем…' : 'Обновить'}
          </Button>
        </div>
      </header>

      <div className="space-y-3 rounded-xl border p-3 sm:p-4">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-[10rem_minmax(0,1fr)_10rem_10rem]">
          <div className="flex min-w-0 flex-col justify-end gap-1">
            <label htmlFor="audit-entity-type" className="text-sm font-medium">
              Тип сущности
            </label>
            <select
              id="audit-entity-type"
              value={entityType}
              onChange={(event) => {
                setEntityType(event.target.value as AdminAuditEntityType)
              }}
              className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
            >
              {ADMIN_AUDIT_ENTITY_TYPES.map((value) => (
                <option key={value} value={value}>
                  {ADMIN_AUDIT_ENTITY_LABELS[value]}
                </option>
              ))}
            </select>
          </div>

          <div className="flex min-w-0 flex-col justify-end gap-1">
            <label htmlFor="audit-actor" className="text-sm font-medium">
              Авторская электронная почта
            </label>
            <Input
              id="audit-actor"
              type="search"
              value={actorInput}
              maxLength={ADMIN_AUDIT_ACTOR_MAX_LENGTH}
              placeholder="name@example.com"
              className="h-11"
              onChange={(event) => setActorInput(event.target.value)}
            />
          </div>

          <div className="flex min-w-0 flex-col justify-end gap-1">
            <label htmlFor="audit-from" className="text-sm font-medium">
              С даты
            </label>
            <Input
              id="audit-from"
              type="date"
              value={from}
              aria-describedby="audit-dates-hint"
              className="h-11"
              onChange={(event) => setFrom(event.target.value)}
            />
          </div>

          <div className="flex min-w-0 flex-col justify-end gap-1">
            <label htmlFor="audit-to" className="text-sm font-medium">
              По дату
            </label>
            <Input
              id="audit-to"
              type="date"
              value={to}
              aria-describedby="audit-dates-hint"
              className="h-11"
              onChange={(event) => setTo(event.target.value)}
            />
          </div>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-2">
          <p id="audit-dates-hint" className="text-xs text-muted-foreground">
            Даты — в UTC, границы включают обе даты.
          </p>
          {isFiltered && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={resetFilters}
            >
              Сбросить фильтры
            </Button>
          )}
        </div>
      </div>

      {list.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {list.refreshError}
        </p>
      )}

      <div aria-busy={isFirstLoad || list.isLoadingPage}>
        {isFirstLoad && <AuditSkeleton />}

        {list.status === 'error' && (
          <AuditError
            message={list.error ?? 'Не удалось загрузить журнал.'}
            onRetry={list.reload}
          />
        )}

        {isEmpty && <AuditEmpty isFiltered={isFiltered} />}

        {list.status === 'ready' && list.items.length > 0 && (
          <div className="space-y-3">
            <ul aria-label="Записи журнала" className="space-y-2">
              {list.items.map((entry) => (
                <AuditEntry key={entry.id} entry={entry} />
              ))}
            </ul>

            <ListPager
              label="Страницы журнала"
              rangeLabel={formatPageRange(list.offset, list.items.length)}
              hasPrev={list.hasPrev}
              hasNext={list.hasNext}
              isLoading={list.isLoadingPage}
              error={list.pageError}
              onPrev={list.goPrev}
              onNext={list.goNext}
              pageIndex={list.pageIndex}
              focusTargetRef={headingRef}
            />
          </div>
        )}
      </div>
    </div>
  )
}

function AuditSkeleton() {
  return (
    <div className="space-y-2" aria-busy="true">
      <p role="status" className="sr-only">
        Загружаем журнал…
      </p>
      <Skeleton className="h-16 rounded-xl" aria-hidden />
      <Skeleton className="h-16 rounded-xl" aria-hidden />
      <Skeleton className="h-16 rounded-xl" aria-hidden />
      <Skeleton className="h-16 rounded-xl" aria-hidden />
    </div>
  )
}

function AuditError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Не удалось загрузить журнал</p>
        <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
          {message}
        </p>
      </div>
      <Button size="sm" variant="outline" className="h-11 sm:h-8" onClick={onRetry}>
        Повторить
      </Button>
    </div>
  )
}

/**
 * Пустой журнал: без фильтров — «Журнал пуст», с фильтрами — «Ничего не найдено».
 *
 * Второй вариант не предлагает «изменить данные»: журнал неизменяем, и пустая
 * выборка под фильтром — нормальный ответ, а не повод править историю.
 */
function AuditEmpty({ isFiltered }: { isFiltered: boolean }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        {isFiltered ? (
          <Search className="size-4 text-muted-foreground" aria-hidden />
        ) : (
          <Inbox className="size-4 text-muted-foreground" aria-hidden />
        )}
      </div>
      <div className="space-y-1">
        <p className="font-medium">
          {isFiltered ? 'Ничего не найдено' : 'Журнал пуст'}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {isFiltered
            ? 'Под выбранные фильтры записей нет.'
            : 'Сервер ещё не записал ни одного изменения. Новые записи появятся здесь автоматически.'}
        </p>
      </div>
    </div>
  )
}
