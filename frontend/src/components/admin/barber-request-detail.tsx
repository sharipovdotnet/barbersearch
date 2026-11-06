import { useState, type ReactNode, type RefObject } from 'react'
import { ArrowLeft, Circle, FileText, TriangleAlert } from 'lucide-react'

import { BarberRequestStatusBadge } from '@/components/admin/barber-request-list'
import { BarberRequestActionDialog } from '@/components/admin/barber-request-action-dialog'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { AdminBarberRequestDetailStatus } from '@/hooks/use-admin-barber-request'
import type {
  AdminBarberRequest,
  AdminBarberRequestAction,
} from '@/lib/admin-barber-requests'
import { formatDateTime, formatExperience } from '@/lib/format'

/**
 * Карточка заявки: кто подал, что написал и что с этим решено.
 *
 * Email заявителя есть только в админском контракте — публичная страница его не
 * показывает. Действия доступны только у `Pending`: повторное решение сервер
 * отклонит `409`, поэтому кнопок у разобранной заявки нет. Оба решения
 * подтверждаются `alertdialog`; диалог остаётся открытым, пока не придёт ответ.
 */
export function BarberRequestDetail({
  request,
  headingRef,
  onApprove,
  onReject,
  onBack,
}: {
  request: AdminBarberRequest
  headingRef: RefObject<HTMLHeadingElement | null>
  /** `null` — успех; строка — русский текст ошибки для диалога. */
  onApprove: () => Promise<string | null>
  onReject: (reason: string) => Promise<string | null>
  onBack: () => void
}) {
  const [target, setTarget] = useState<AdminBarberRequestAction | null>(null)

  const isPending = request.status === 'Pending'

  function closeAction() {
    setTarget(null)
    // Кнопка-триггер исчезает вместе со статусом: фокус возвращаем на заголовок,
    // а не оставляем в закрытом диалоге.
    window.requestAnimationFrame(() => headingRef.current?.focus())
  }

  return (
    <section
      id="barber-request-detail"
      aria-labelledby="barber-request-detail-title"
      className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10"
    >
      <div className="space-y-5 p-4 sm:p-5">
        <div className="flex items-center justify-between gap-3">
          <Button
            variant="ghost"
            size="sm"
            className="h-11 sm:h-7 lg:hidden"
            onClick={onBack}
          >
            <ArrowLeft data-icon="inline-start" aria-hidden />
            Назад
          </Button>
          <BarberRequestStatusBadge status={request.status} className="ml-auto" />
        </div>

        <div className="min-w-0">
          <h2
            id="barber-request-detail-title"
            ref={headingRef}
            tabIndex={-1}
            className="scroll-mt-20 font-heading text-xl leading-tight font-semibold tracking-tight"
          >
            {request.displayName}
          </h2>
          <p className="mt-0.5 text-sm break-words text-muted-foreground">
            {request.applicant.fullName} · {request.city}
          </p>
        </div>

        <dl className="grid gap-4 border-t pt-4 sm:grid-cols-2">
          <ContextItem label="Стаж">
            {formatExperience(request.yearsOfExperience)}
          </ContextItem>

          <ContextItem label="Город">{request.city}</ContextItem>

          <ContextItem label="Уже работает в салоне">
            {request.hasSalon ? 'Да' : 'Нет'}
          </ContextItem>

          <ContextItem label="Подана">
            <time dateTime={request.createdAt}>
              {formatDateTime(request.createdAt)}
            </time>
          </ContextItem>

          <ContextItem label="Заявитель">
            <span className="block break-words">
              {request.applicant.fullName}
            </span>
            <span className="block break-all text-muted-foreground">
              {request.applicant.email}
            </span>
          </ContextItem>

          {request.reviewedAt && (
            <ContextItem label="Рассмотрена">
              <time dateTime={request.reviewedAt}>
                {formatDateTime(request.reviewedAt)}
              </time>
            </ContextItem>
          )}
        </dl>

        <div className="border-t pt-4">
          <h3 className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
            О себе
          </h3>
          {request.about ? (
            <p className="mt-1 text-sm break-words whitespace-pre-line">
              {request.about}
            </p>
          ) : (
            <p className="mt-1 text-sm text-muted-foreground">
              Заявитель не заполнил описание.
            </p>
          )}
        </div>

        {request.status === 'Rejected' && (
          <div className="rounded-lg bg-destructive/10 p-3">
            <h3 className="text-xs font-medium tracking-wide text-destructive uppercase">
              Причина отказа
            </h3>
            <p className="mt-1 text-sm break-words whitespace-pre-line">
              {request.rejectionReason ?? 'Причина не указана.'}
            </p>
          </div>
        )}

        {isPending ? (
          <section
            aria-labelledby="barber-request-actions-title"
            className="space-y-3 border-t pt-4"
          >
            <h3
              id="barber-request-actions-title"
              className="text-sm font-medium"
            >
              Решение
            </h3>

            <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
              <Button
                className="h-11 sm:h-9"
                onClick={() => setTarget('approve')}
              >
                Одобрить
              </Button>
              <Button
                variant="outline"
                className="h-11 sm:h-9"
                onClick={() => setTarget('reject')}
              >
                Отклонить
              </Button>
            </div>

            <p className="text-xs text-muted-foreground">
              Одобрение сразу меняет роль и завершает сессии заявителя. Отказ
              требует причину — её увидит заявитель.
            </p>
          </section>
        ) : (
          <p className="border-t pt-4 text-xs text-muted-foreground">
            Заявка разобрана: решение не меняется, повторное действие сервер
            отклонит.
          </p>
        )}
      </div>

      {target !== null && (
        <BarberRequestActionDialog
          kind={target}
          request={request}
          onClose={closeAction}
          onConfirm={(reason) =>
            target === 'approve' ? onApprove() : onReject(reason ?? '')
          }
        />
      )}
    </section>
  )
}

/** Заглушка правой колонки, пока заявка не выбрана. */
export function BarberRequestDetailPlaceholder() {
  return (
    <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <FileText className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Выберите заявку</p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          Слева очередь: откройте строку, чтобы увидеть ответы заявителя и принять
          решение.
        </p>
      </div>
    </div>
  )
}

/** Состояние правой колонки: загрузка, ошибка или потерянная заявка. */
export function BarberRequestDetailState({
  status,
  error,
  onRetry,
  onBack,
}: {
  status: AdminBarberRequestDetailStatus
  error: string | null
  onRetry: () => void
  onBack: () => void
}) {
  if (status === 'notFound') {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Circle className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Заявка не найдена.</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Возможно, ссылка устарела. Вернитесь к списку — он покажет актуальные
            заявки.
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onBack}
        >
          К списку
        </Button>
      </div>
    )
  }

  if (status === 'error') {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
          <TriangleAlert className="size-4 text-destructive" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Не удалось загрузить заявку</p>
          <p
            role="alert"
            className="mx-auto max-w-sm text-sm text-muted-foreground"
          >
            {error ?? 'Попробуйте ещё раз.'}
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onRetry}
        >
          Повторить
        </Button>
      </div>
    )
  }

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        Загружаем заявку…
      </p>
      <Skeleton className="h-12 w-40" aria-hidden />
      <Skeleton className="h-6 w-60" aria-hidden />
      <Skeleton className="h-64 rounded-xl" aria-hidden />
    </div>
  )
}

function ContextItem({
  label,
  children,
}: {
  label: string
  children: ReactNode
}) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
        {label}
      </dt>
      <dd className="mt-1 text-sm">{children}</dd>
    </div>
  )
}
