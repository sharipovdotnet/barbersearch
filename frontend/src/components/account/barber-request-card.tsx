import { Scissors } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { useMyBarberRequest } from '@/hooks/use-my-barber-request'
import { useI18n } from '@/i18n'
import {
  BARBER_REQUEST_STATUS_LABELS,
  type BarberRequest,
} from '@/lib/barber-requests'
import { BECOME_A_BARBER_PATH } from '@/lib/barber-request-path'
import { formatDateTime } from '@/lib/format'
import { isPlainClick, navigate } from '@/lib/router'

/**
 * Карточка «Стать барбером» в кабинете клиента.
 *
 * Отдельный виджет, а не часть списка записей: своя загрузка, своя ошибка и своя
 * кнопка «Повторить», поэтому упавшая заявка не прячет визиты. `401` здесь не
 * показывается — гейт кабинета уже выше, а карточка читает одно
 * `GET /barber-requests/mine`.
 */
export function BarberRequestCard() {
  const { t } = useI18n()
  const mine = useMyBarberRequest()

  // Сессия закончилась: экран кабинета покажет гейт входа, карточке нечего добавить.
  if (mine.requiresSignIn) {
    return null
  }

  return (
    <section
      aria-labelledby="barber-request-card-title"
      className="rounded-xl bg-card p-4 ring-1 ring-foreground/10"
    >
      <div className="flex items-start gap-3">
        <div className="flex size-9 shrink-0 items-center justify-center rounded-full bg-muted">
          <Scissors className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="min-w-0 flex-1 space-y-2">
          <h2
            id="barber-request-card-title"
            className="text-sm font-medium"
          >
            {t('Стать барбером')}
          </h2>

          {(mine.status === 'loading' || mine.status === 'idle') && (
            <p role="status" className="text-sm text-muted-foreground">
              {t('Проверяем заявку…')}
            </p>
          )}

          {mine.status === 'error' && (
            <p role="alert" className="text-sm text-muted-foreground">
              {mine.error ?? t('Не удалось проверить заявку.')}{' '}
              <button
                type="button"
                className="rounded-sm font-medium text-foreground underline underline-offset-4 hover:text-foreground/80 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
                onClick={mine.reload}
              >
                {t('Повторить')}
              </button>
            </p>
          )}

          {mine.status === 'ready' && mine.request === null && (
            <>
              <p className="text-sm text-muted-foreground">
                {t(
                  'Принимайте клиентов сами: заявка открывает кабинет барбера после проверки администратором.'
                )}
              </p>
              <Button
                size="sm"
                className="h-11 sm:h-8"
                onClick={() => {
                  navigate(BECOME_A_BARBER_PATH)
                }}
              >
                {t('Стать барбером')}
              </Button>
            </>
          )}

          {mine.status === 'ready' && mine.request !== null && (
            <BarberRequestSummary request={mine.request} />
          )}
        </div>
      </div>
    </section>
  )
}

/** Короткая строка статуса: полная карточка живёт на `/become-a-barber`. */
function BarberRequestSummary({ request }: { request: BarberRequest }) {
  const { t } = useI18n()

  return (
    <div className="space-y-2">
      <p className="text-sm">
        <span className="font-medium">
          {t(BARBER_REQUEST_STATUS_LABELS[request.status])}
        </span>
        <span className="text-muted-foreground">
          {' '}
          {t('· подана')}{' '}
          <time dateTime={request.createdAt}>
            {formatDateTime(request.createdAt)}
          </time>
        </span>
      </p>

      {request.status === 'Rejected' && request.rejectionReason && (
        <p className="text-sm break-words text-muted-foreground">
          {t('Причина: {reason}', { reason: request.rejectionReason })}
        </p>
      )}

      <a
        href={BECOME_A_BARBER_PATH}
        className="inline-flex min-h-11 items-center rounded-md text-sm font-medium underline underline-offset-4 hover:text-foreground/80 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:min-h-0"
        onClick={(event) => {
          if (!isPlainClick(event)) {
            return
          }

          event.preventDefault()
          navigate(BECOME_A_BARBER_PATH)
        }}
      >
        {t('Подробнее')}
      </a>
    </div>
  )
}
