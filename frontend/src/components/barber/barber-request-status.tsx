import type { ReactNode } from 'react'
import { CircleCheck, CircleX, Clock } from 'lucide-react'

import { LoginDialog } from '@/components/login-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useSession } from '@/hooks/use-session'
import { BARBER_CABINET_PATH, BARBER_ONBOARDING_PATH } from '@/lib/barber-cabinet-path'
import {
  BARBER_REQUEST_STATUS_LABELS,
  type BarberRequest,
  type BarberRequestStatus,
} from '@/lib/barber-requests'
import { formatDateTime, formatExperience } from '@/lib/format'
import { navigate } from '@/lib/router'
import { useI18n } from '@/i18n'

const DEFAULT_HEADINGS: Record<BarberRequestStatus, string> = {
  Pending: 'Заявка на рассмотрении',
  Approved: 'Заявка одобрена',
  Rejected: 'Заявка отклонена',
}

/**
 * Статус своей заявки «Стать барбером».
 *
 * Один компонент для страницы `/become-a-barber` и карточки в кабинете: заявка
 * показывается целиком (что отправили, когда, что решили), а действие зависит от
 * статуса. После одобрения сессия заявителя уже недействительна — `ChangeRole`
 * ротирует security stamp, поэтому «Войти снова» единственный честный путь.
 */
export function BarberRequestStatus({
  request,
  heading,
  onReapply,
}: {
  request: BarberRequest
  /** Заголовок карточки; по умолчанию — по статусу заявки. */
  heading?: string
  /** Повторная заявка после отказа: домен разрешает одну новую `Pending`. */
  onReapply?: () => void
}) {
  const { t } = useI18n()
  const { user } = useSession()
  const StatusIcon = STATUS_ICONS[request.status]

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <StatusIcon
            className={`size-5 ${STATUS_ICON_TONES[request.status]}`}
            aria-hidden
          />
        </div>
        <h2 className="font-heading text-xl font-semibold tracking-tight">
          {heading ?? t(DEFAULT_HEADINGS[request.status])}
        </h2>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* `role="status"` объявляет смену статуса: карточка открывается уже с ответом
            администратора, и это единственный сигнал о решении. */}
        <p
          role="status"
          className="inline-flex items-center gap-2 rounded-full border px-2.5 py-1 text-sm font-medium"
        >
          <span aria-hidden className="size-1.5 rounded-full bg-current" />
          {t(BARBER_REQUEST_STATUS_LABELS[request.status])}
        </p>

        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <DetailItem label={t('Имя')}>{request.displayName}</DetailItem>
          <DetailItem label={t('Стаж')}>
            {formatExperience(request.yearsOfExperience)}
          </DetailItem>
          <DetailItem label={t('Город')}>{request.city}</DetailItem>
          <DetailItem label={t('Работает в салоне')}>
            {request.hasSalon ? t('Да, уже в салоне') : t('Нет, работает сам')}
          </DetailItem>
          <DetailItem label={t('Подана')}>
            <time dateTime={request.createdAt}>
              {formatDateTime(request.createdAt)}
            </time>
          </DetailItem>
          {request.reviewedAt && (
            <DetailItem label={t('Рассмотрена')}>
              <time dateTime={request.reviewedAt}>
                {formatDateTime(request.reviewedAt)}
              </time>
            </DetailItem>
          )}
        </dl>

        {request.about && (
          <div>
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
              {t('О себе')}
            </p>
            <p className="mt-1 text-sm break-words whitespace-pre-line">
              {request.about}
            </p>
          </div>
        )}

        {request.status === 'Rejected' && (
          <section
            aria-labelledby="barber-request-rejection-title"
            className="space-y-1 rounded-lg bg-destructive/10 p-3"
          >
            <h3
              id="barber-request-rejection-title"
              className="text-xs font-medium tracking-wide text-destructive uppercase"
            >
              {t('Причина отказа')}
            </h3>
            <p className="text-sm break-words whitespace-pre-line">
              {request.rejectionReason ?? t('Причина не указана.')}
            </p>
          </section>
        )}

        {request.status === 'Rejected' && onReapply && (
          <div className="space-y-2">
            <p className="text-sm text-muted-foreground">
              {t(
                'Подать заявку можно снова: активная заявка у вас одна, поэтому прошлая и не мешает.'
              )}
            </p>
            <Button
              type="button"
              variant="outline"
              className="h-11 sm:h-9"
              onClick={onReapply}
            >
              {t('Подать заявку снова')}
            </Button>
          </div>
        )}

        {request.status === 'Approved' && user?.role === 'Customer' && (
          <div className="space-y-2 border-t pt-4">
            <p className="text-sm text-muted-foreground">
              {t(
                'Роль изменена, но текущая сессия уже недействительна. Войдите снова — после входа вы создадите профиль барбера.'
              )}
            </p>
            <LoginDialog
              trigger={t('Войти снова')}
              variant="default"
              size="sm"
              className="h-11 sm:h-9"
              redirectTo={BARBER_CABINET_PATH}
            />
          </div>
        )}

        {request.status === 'Approved' &&
          user?.role === 'Barber' &&
          user.barberProfileId === null && (
            <div className="space-y-2 border-t pt-4">
              <p className="text-sm text-muted-foreground">
                {t(
                  'Остался один шаг: заполните профиль барбера, чтобы вас нашли клиенты.'
                )}
              </p>
              <Button
                className="h-11 sm:h-9"
                onClick={() => {
                  navigate(BARBER_ONBOARDING_PATH)
                }}
              >
                {t('Создать профиль барбера')}
              </Button>
            </div>
          )}

        {request.status === 'Approved' &&
          user?.role === 'Barber' &&
          user.barberProfileId !== null && (
            <div className="border-t pt-4">
              <Button
                className="h-11 sm:h-9"
                onClick={() => {
                  navigate(BARBER_CABINET_PATH)
                }}
              >
                {t('Открыть кабинет барбера')}
              </Button>
            </div>
          )}
      </CardContent>
    </Card>
  )
}

const STATUS_ICONS = {
  Pending: Clock,
  Approved: CircleCheck,
  Rejected: CircleX,
} as const

const STATUS_ICON_TONES: Record<BarberRequestStatus, string> = {
  Pending: 'text-warning',
  Approved: 'text-success',
  Rejected: 'text-destructive',
}

/** Скелетон статуса: силуэт карточки, чтобы загрузка не прыгала вёрсткой. */
export function BarberRequestStatusSkeleton() {
  return (
    <Card aria-busy="true" aria-hidden>
      <CardHeader>
        <Skeleton className="size-10 rounded-full" />
        <Skeleton className="h-6 w-56" />
      </CardHeader>
      <CardContent className="space-y-3">
        <Skeleton className="h-7 w-40 rounded-full" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-4/5" />
        <Skeleton className="h-4 w-3/5" />
      </CardContent>
    </Card>
  )
}

/** Ошибка чтения заявки: причина и повтор, без кода ответа и английского `detail`. */
export function BarberRequestError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-8 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <CircleX className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">{t('Не удалось проверить заявку')}</p>
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

function DetailItem({
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
      <dd className="mt-0.5 break-words">{children}</dd>
    </div>
  )
}
