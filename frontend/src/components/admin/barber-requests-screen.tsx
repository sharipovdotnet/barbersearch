import { useEffect, useRef, useState } from 'react'
import { Inbox, Loader2, RefreshCw, TriangleAlert, X } from 'lucide-react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import {
  BarberRequestDetail,
  BarberRequestDetailPlaceholder,
  BarberRequestDetailState,
} from '@/components/admin/barber-request-detail'
import { BarberRequestList } from '@/components/admin/barber-request-list'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminBarberRequest } from '@/hooks/use-admin-barber-request'
import { useAdminBarberRequestList } from '@/hooks/use-admin-barber-request-list'
import { useAutoRefresh } from '@/hooks/use-auto-refresh'
import { useMediaQuery } from '@/hooks/use-media-query'
import { useI18n } from '@/i18n'
import {
  ADMIN_BARBER_REQUEST_FILTER_LABELS,
  ADMIN_BARBER_REQUEST_FILTERS,
  approveBarberRequest,
  describeAdminBarberRequestActionError,
  isBarberRequestGone,
  isBarberRequestUnauthorized,
  rejectBarberRequest,
  type AdminBarberRequest,
  type AdminBarberRequestAction,
  type AdminBarberRequestFilter,
} from '@/lib/admin-barber-requests'
import { barberRequestsPath } from '@/lib/admin-path'
import { formatClockTime } from '@/lib/format'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * `/admin/barber-requests` — очередь заявок «Стать барбером» и все заявки.
 *
 * Фильтр живёт в `?status=` (очередь — по умолчанию, все заявки — `all`), выбранная
 * заявка — в пути: перезагрузка не теряет ни фильтр, ни карточку, а карточка не
 * может принадлежать чужой выборке. На широком экране список и карточка стоят
 * рядом, на телефоне карточка заменяет список.
 *
 * Счётчиков нет: API отдаёт курсорную страницу без `total`. После решения строка
 * обновляется локально (в очереди — исчезает), затем список и карточка тихо
 * перечитываются, а результат объявляется `role="status"`.
 */
export function BarberRequestsScreen({
  requestId,
  status,
}: {
  requestId: string | null
  status: AdminBarberRequestFilter
}) {
  const { t } = useI18n()
  const list = useAdminBarberRequestList(status)
  const detail = useAdminBarberRequest(requestId)
  const isWide = useMediaQuery('(min-width: 1024px)')
  const headingRef = useRef<HTMLHeadingElement | null>(null)
  const detailHeadingRef = useRef<HTMLHeadingElement | null>(null)

  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [isActing, setIsActing] = useState(false)

  // Новая заявка от клиента и решение второго администратора должны появляться
  // без ручного обновления. На время решения опрос стоит: ответ, прочитанный
  // до коммита, вернул бы заявке прошлый статус.
  useAutoRefresh({
    enabled:
      list.status === 'ready' &&
      !list.requiresSignIn &&
      !list.isLoadingPage &&
      !requiresSignIn &&
      !isActing,
    onRefresh: () => {
      list.refresh()

      if (requestId !== null && detail.status !== 'notFound') {
        detail.reload()
      }
    },
  })

  // На телефоне карточка заменяет список — переход должен начинаться сверху.
  useEffect(() => {
    if (!requestId || isWide) {
      return
    }

    window.scrollTo({ top: 0 })
  }, [requestId, isWide])

  // На широком экране первая строка открывается сразу: пустая правая колонка
  // выглядит как ошибка загрузки.
  useEffect(() => {
    if (
      !isWide ||
      requestId ||
      list.status !== 'ready' ||
      list.items.length === 0
    ) {
      return
    }

    navigate(barberRequestsPath(list.items[0].id, status), { replace: true })
  }, [isWide, requestId, list.status, list.items, status])

  const isEmpty =
    list.status === 'ready' && list.items.length === 0 && requestId === null
  const isMobileDetail = !isWide && requestId !== null
  const showGrid =
    list.status === 'ready' && (list.items.length > 0 || requestId !== null)
  // Локальная константа, а не `detail.request` в JSX: замыкания обработчиков
  // должны видеть уже сужённый тип, а не «может быть null».
  const selectedRequest = detail.request

  function handleFilterChange(next: AdminBarberRequestFilter) {
    if (next === status) {
      return
    }

    // Фильтр — часть выборки: переход в адрес без id закрывает карточку прошлой
    // выборки, а на широком экране автовыбор откроет первую строку новой.
    setAnnouncement(null)
    navigate(barberRequestsPath(null, next))
  }

  function handleSelect(id: string) {
    navigate(barberRequestsPath(id, status))
  }

  function handleBack() {
    navigate(barberRequestsPath(null, status))

    // Фокус возвращается на заголовок, а не в <body>: на телефоне списка ещё нет,
    // пока страница не перерисовалась.
    window.requestAnimationFrame(() => {
      window.requestAnimationFrame(() => headingRef.current?.focus())
    })
  }

  /**
   * Решение по заявке. Возвращает текст ошибки для диалога или `null` при успехе.
   *
   * Строка списка обновляется сразу: в очереди одобренная или отклонённая заявка
   * больше не `Pending`, в «Всех» она остаётся с новым статусом. Затем карточка и
   * список перечитываются — сервер остаётся источником правды. `404` на решение
   * значит, что заявку уже удалили: строку убираем молча, а не показываем ошибку.
   */
  async function runAction(
    kind: AdminBarberRequestAction,
    request: AdminBarberRequest,
    reason: string | null
  ): Promise<string | null> {
    setIsActing(true)

    try {
      const updated =
        kind === 'approve'
          ? await approveBarberRequest(request.id)
          : await rejectBarberRequest(request.id, reason ?? '')

      setAnnouncement(
        kind === 'approve'
          ? t(
              'Заявка «{name}» одобрена: роль барбера выдана, сессии заявителя завершены.',
              { name: request.displayName }
            )
          : t('Заявка «{name}» отклонена. Заявитель увидит причину.', {
              name: request.displayName,
            })
      )

      if (status === 'pending' && updated.status !== 'Pending') {
        list.removeItem(updated.id)
      } else {
        list.replaceItem(updated.id, updated)
      }

      detail.reload()
      list.refresh()

      return null
    } catch (cause) {
      if (isBarberRequestUnauthorized(cause)) {
        setRequiresSignIn(true)
        return null
      }

      if (isBarberRequestGone(cause)) {
        list.removeItem(request.id)
        setAnnouncement(
          t('Заявка «{name}» больше не существует — список обновлён.', {
            name: request.displayName,
          })
        )
        detail.reload()
        list.refresh()

        return null
      }

      return describeAdminBarberRequestActionError(cause, kind)
    } finally {
      setIsActing(false)
    }
  }

  if (list.requiresSignIn || requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate
          returnTo={barberRequestsPath(requestId, status)}
          onSignedIn={() => {
            setRequiresSignIn(false)
            list.reload()
            detail.reload()
          }}
        />
      </div>
    )
  }

  return (
    <div className="space-y-5">
      {isMobileDetail && (
        <h1 ref={headingRef} tabIndex={-1} className="sr-only">
          {t('Заявки')}
        </h1>
      )}

      {!isMobileDetail && (
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1
              ref={headingRef}
              tabIndex={-1}
              className="scroll-mt-20 font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
            >
              {t('Заявки')}
            </h1>
            <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
              {t(
                'Клиенты просят роль барбера: проверьте заявку и примите решение. Одобрение сразу меняет роль, отказ требует причину.'
              )}
            </p>
          </div>

          <div className="flex items-center gap-2">
            {list.updatedAt !== null && (
              <span className="text-xs text-muted-foreground tabular-nums">
                {t('Обновлено в {time}', {
                  time: formatClockTime(new Date(list.updatedAt)),
                })}
              </span>
            )}
            <Button
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              disabled={
                list.isRefreshing ||
                list.isLoadingPage ||
                list.status === 'loading'
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
              {list.isRefreshing ? t('Обновляем…') : t('Обновить')}
            </Button>
          </div>
        </header>
      )}

      {list.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {list.refreshError}
        </p>
      )}

      {announcement && (
        <div
          role="status"
          className="flex flex-wrap items-start justify-between gap-2 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p className="min-w-0 flex-1">{announcement}</p>
          <button
            type="button"
            aria-label={t('Скрыть сообщение')}
            className="rounded-md p-1 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            onClick={() => setAnnouncement(null)}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        </div>
      )}

      {!isMobileDetail && (
        <div
          role="group"
          aria-label={t('Какие заявки показать')}
          className="inline-flex items-center gap-0.5 self-start rounded-lg bg-muted p-0.5"
        >
          {ADMIN_BARBER_REQUEST_FILTERS.map((item) => {
            const isSelected = item === status

            return (
              <button
                key={item}
                type="button"
                aria-pressed={isSelected}
                onClick={() => handleFilterChange(item)}
                className={cn(
                  'inline-flex h-11 items-center rounded-md px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
                  isSelected
                    ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                    : 'text-muted-foreground hover:text-foreground'
                )}
              >
                {t(ADMIN_BARBER_REQUEST_FILTER_LABELS[item])}
              </button>
            )
          })}
        </div>
      )}

      {list.status === 'loading' && <RequestsSkeleton />}

      {list.status === 'error' && (
        <RequestsError
          message={list.error ?? t('Не удалось загрузить заявки.')}
          onRetry={list.reload}
        />
      )}

      {isEmpty && <RequestsEmpty status={status} />}

      {showGrid && (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
          <div className={cn('min-w-0', requestId ? 'hidden lg:block' : 'block')}>
            {list.items.length > 0 ? (
              <BarberRequestList
                items={list.items}
                status={status}
                selectedId={requestId}
                offset={list.offset}
                pageIndex={list.pageIndex}
                hasPrev={list.hasPrev}
                hasNext={list.hasNext}
                isLoadingPage={list.isLoadingPage}
                pageError={list.pageError}
                focusTargetRef={headingRef}
                onSelect={handleSelect}
                onPrev={list.goPrev}
                onNext={list.goNext}
              />
            ) : (
              <p className="text-sm text-muted-foreground">
                {t('В загруженной странице нет заявок.')}
              </p>
            )}
          </div>

          <div className={cn('min-w-0', requestId ? 'block' : 'hidden lg:block')}>
            {selectedRequest ? (
              <BarberRequestDetail
                request={selectedRequest}
                headingRef={detailHeadingRef}
                onApprove={() => runAction('approve', selectedRequest, null)}
                onReject={(reason) =>
                  runAction('reject', selectedRequest, reason)
                }
                onBack={handleBack}
              />
            ) : requestId ? (
              <BarberRequestDetailState
                status={detail.status}
                error={detail.error}
                onRetry={detail.reload}
                onBack={handleBack}
              />
            ) : (
              <BarberRequestDetailPlaceholder />
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function RequestsSkeleton() {
  const { t } = useI18n()

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        {t('Загружаем заявки…')}
      </p>
      <Skeleton className="h-4 w-32" aria-hidden />
      <Skeleton className="h-96 rounded-xl" aria-hidden />
    </div>
  )
}

function RequestsError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">{t('Не удалось загрузить заявки')}</p>
        <p
          role="alert"
          className="mx-auto max-w-sm text-sm text-muted-foreground"
        >
          {message}
        </p>
      </div>
      <Button
        size="sm"
        variant="outline"
        className="h-11 sm:h-8"
        onClick={onRetry}
      >
        {t('Повторить')}
      </Button>
    </div>
  )
}

/**
 * Пустое состояние: у очереди и всех заявок разные причины, поэтому и тексты разные.
 * Счётчиков нет — «всего» сервер не отдаёт.
 */
function RequestsEmpty({ status }: { status: AdminBarberRequestFilter }) {
  const { t } = useI18n()
  const isQueue = status === 'pending'

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Inbox className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">
          {isQueue ? t('Заявок нет') : t('Заявок с таким статусом нет')}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {isQueue
            ? t(
                'Очередь пуста: новые заявки появятся здесь, как только клиенты их подадут.'
              )
            : t('Попробуйте очередь — она показывает неразобранные заявки.')}
        </p>
      </div>
    </div>
  )
}
