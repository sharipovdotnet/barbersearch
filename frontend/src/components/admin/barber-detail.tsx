import { useState, type ReactNode } from 'react'
import { ArrowLeft, CheckCircle2, Circle, Loader2, Star } from 'lucide-react'

import { BarberStatusBadge } from '@/components/admin/barber-list'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import type {
  AdminBarberAction,
  AdminBarberDetail,
  BarberGrade,
} from '@/lib/admin-barbers'
import { BARBER_GRADE_LABELS, BARBER_GRADES } from '@/lib/admin-barbers'
import type { AdminBarberDetailStatus } from '@/hooks/use-admin-barber'
import {
  formatDateTime,
  formatDaysCount,
  formatExperience,
  formatRating,
  formatReviewsCount,
  getInitials,
} from '@/lib/format'

/**
 * Карточка барбера: контакты, статусы и чек-лист готовности.
 *
 * Чек-лист — подсказка, а не блокировка: подтвердить профиль можно и с минусами,
 * поэтому «не готово» подсвечивается, но действиям не мешает.
 */
export function BarberDetail({
  barber,
  pendingAction,
  actionError,
  onVerify,
  onUnverify,
  onChangeGrade,
  onHide,
  onShow,
  onBack,
}: {
  barber: AdminBarberDetail
  pendingAction: AdminBarberAction | null
  actionError: string | null
  onVerify: () => void
  onUnverify: () => void
  onChangeGrade: (grade: BarberGrade) => void
  onHide: () => void
  onShow: () => void
  onBack: () => void
}) {
  return (
    <section
      id="barber-detail"
      aria-labelledby="barber-detail-title"
      aria-busy={pendingAction !== null}
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
          <BarberStatusBadge barber={barber} className="ml-auto" />
        </div>

        <div className="flex items-start gap-3">
          <Avatar size="lg">
            {barber.avatarUrl && (
              <AvatarImage src={barber.avatarUrl} alt="" />
            )}
            <AvatarFallback>{getInitials(barber.displayName)}</AvatarFallback>
          </Avatar>
          <div className="min-w-0">
            <h2
              id="barber-detail-title"
              className="font-heading text-xl leading-tight font-semibold tracking-tight"
            >
              {barber.displayName}
            </h2>
            <p className="mt-0.5 text-sm break-all text-muted-foreground">
              {barber.email}
            </p>
            <p className="mt-1 text-xs text-muted-foreground">
              {BARBER_GRADE_LABELS[barber.grade]}
            </p>
          </div>
        </div>

        {barber.bio ? (
          <p className="max-w-prose text-sm leading-relaxed break-words whitespace-pre-line">
            {barber.bio}
          </p>
        ) : (
          <p className="text-sm text-muted-foreground">
            Барбер не заполнил описание о себе.
          </p>
        )}

        <dl className="grid gap-4 border-t pt-4 sm:grid-cols-2">
          <ContextItem label="Стаж">
            {formatExperience(barber.yearsOfExperience)}
          </ContextItem>

          <ContextItem label="Рейтинг">
            {barber.rating === null ? (
              'Опубликованных отзывов нет'
            ) : (
              <span className="inline-flex items-center gap-1.5 tabular-nums">
                <Star className="size-3.5 fill-current" aria-hidden />
                {formatRating(barber.rating)}
                <span className="text-muted-foreground">
                  · {formatReviewsCount(barber.reviewsCount)}
                </span>
              </span>
            )}
          </ContextItem>

          <ContextItem label="Телефон">
            {barber.phoneNumber ?? 'Не указан'}
          </ContextItem>

          <ContextItem label="Профиль создан">
            {formatDateTime(barber.createdAt)}
          </ContextItem>

          <ContextItem label="Обновлён">
            {formatDateTime(barber.updatedAt)}
          </ContextItem>
        </dl>

        <ReadinessChecklist barber={barber} />

        <BarberActions
          barber={barber}
          pendingAction={pendingAction}
          actionError={actionError}
          onVerify={onVerify}
          onUnverify={onUnverify}
          onChangeGrade={onChangeGrade}
          onHide={onHide}
          onShow={onShow}
        />
      </div>
    </section>
  )
}

/**
 * Административные действия с профилем.
 *
 * Подтверждение и скрытие взаимоисключающие: подтвердить профиль, скрытый из поиска,
 * домен не даёт (`barberProfile.verify.inactive`), поэтому кнопка объясняет причину,
 * а не просто гаснет. Скрытие и снятие подтверждения спрашивают подтверждение:
 * у обоих одно последствие — профиль пропадает из выдачи и записи.
 */
function BarberActions({
  barber,
  pendingAction,
  actionError,
  onVerify,
  onUnverify,
  onChangeGrade,
  onHide,
  onShow,
}: {
  barber: AdminBarberDetail
  pendingAction: AdminBarberAction | null
  actionError: string | null
  onVerify: () => void
  onUnverify: () => void
  onChangeGrade: (grade: BarberGrade) => void
  onHide: () => void
  onShow: () => void
}) {
  const [gradeDraft, setGradeDraft] = useState({
    serverGrade: barber.grade,
    value: barber.grade,
  })
  const [confirm, setConfirm] = useState<'hide' | 'unverify' | null>(null)
  const isPending = pendingAction !== null

  // Черновик грейда живёт до ответа сервера: после перечитывания карточки
  // `barber.grade` меняется, и выбор снова берётся из данных, а не из памяти формы.
  const grade =
    gradeDraft.serverGrade === barber.grade
      ? gradeDraft.value
      : barber.grade

  return (
    <section
      aria-labelledby="barber-actions-title"
      className="space-y-4 border-t pt-4"
    >
      <h3 id="barber-actions-title" className="text-sm font-medium">
        Действия
      </h3>

      {actionError && (
        <p role="alert" className="text-sm text-destructive">
          {actionError}
        </p>
      )}

      <div className="space-y-2">
        <p className="text-sm">
          {barber.isVerified
            ? 'Профиль подтверждён и участвует в поиске.'
            : 'Профиль ждёт проверки и в поиске не показывается.'}
        </p>

        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          {barber.isVerified ? (
            <Button
              variant="outline"
              className="h-11 sm:h-9"
              disabled={isPending}
              onClick={() => setConfirm('unverify')}
            >
              {pendingAction === 'unverify' ? (
                <>
                  <Loader2
                    data-icon="inline-start"
                    aria-hidden
                    className="animate-spin"
                  />
                  Снимаем…
                </>
              ) : (
                'Снять подтверждение'
              )}
            </Button>
          ) : (
            <Button
              className="h-11 sm:h-9"
              disabled={isPending || !barber.isActive}
              onClick={onVerify}
            >
              {pendingAction === 'verify' ? (
                <>
                  <Loader2
                    data-icon="inline-start"
                    aria-hidden
                    className="animate-spin"
                  />
                  Подтверждаем…
                </>
              ) : (
                'Подтвердить'
              )}
            </Button>
          )}

          <p className="text-xs text-muted-foreground sm:max-w-xs">
            {barber.isVerified
              ? 'Снятие подтверждения уберёт профиль из поиска и записи.'
              : barber.isActive
                ? 'Профиль появится в поиске и станет доступен для записи. Отменить это можно в любой момент.'
                : 'Сначала верните профиль в поиск: подтверждение скрытого профиля домен не разрешает.'}
          </p>
        </div>
      </div>

      <div className="space-y-2 border-t pt-3">
        <label htmlFor="barber-grade" className="text-sm font-medium">
          Грейд
        </label>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          <select
            id="barber-grade"
            value={grade}
            disabled={isPending}
            onChange={(event) =>
              setGradeDraft({
                serverGrade: barber.grade,
                value: event.target.value as BarberGrade,
              })
            }
            className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-9 sm:w-56"
          >
            {BARBER_GRADES.map((value) => (
              <option key={value} value={value}>
                {BARBER_GRADE_LABELS[value]}
              </option>
            ))}
          </select>

          <Button
            variant="outline"
            className="h-11 sm:h-9"
            disabled={isPending || grade === barber.grade}
            onClick={() => onChangeGrade(grade)}
          >
            {pendingAction === 'grade' ? (
              <>
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
                Сохраняем…
              </>
            ) : (
              'Сохранить грейд'
            )}
          </Button>
        </div>
        <p className="text-xs text-muted-foreground">
          Грейд видят клиенты в карточке барбера. Это административное поле: сам
          барбер его не меняет.
        </p>
      </div>

      <div className="space-y-2 border-t pt-3">
        <p className="text-sm font-medium">
          {barber.isActive ? 'Профиль виден в поиске' : 'Профиль скрыт из поиска'}
        </p>
        <Button
          variant={barber.isActive ? 'destructive' : 'outline'}
          className="h-11 sm:h-9"
          disabled={isPending}
          onClick={barber.isActive ? () => setConfirm('hide') : onShow}
        >
          {pendingAction === 'hide' ? (
            <>
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
              Скрываем…
            </>
          ) : pendingAction === 'show' ? (
            <>
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
              Возвращаем…
            </>
          ) : barber.isActive ? (
            'Скрыть из поиска'
          ) : (
            'Вернуть в поиск'
          )}
        </Button>
        <p className="text-xs text-muted-foreground">
          {barber.isActive
            ? 'Профиль исчезнет из поиска, но останется в системе: записи и отзывы сохранятся.'
            : 'Профиль снова появится в поиске, если он подтверждён; неподтверждённый останется в очереди проверки.'}
        </p>
      </div>

      <Dialog
        open={confirm !== null}
        onOpenChange={(open) => {
          if (!open) {
            setConfirm(null)
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {confirm === 'hide'
                ? 'Скрыть барбера из поиска?'
                : 'Снять подтверждение?'}
            </DialogTitle>
            <DialogDescription>
              {confirm === 'hide'
                ? 'Профиль исчезнет из поиска и станет недоступен для записи. Записи и отзывы сохранятся, вернуть профиль можно в любой момент.'
                : 'Профиль исчезнет из поиска и станет недоступен для записи, пока вы не подтвердите его снова.'}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              disabled={isPending}
              onClick={() => setConfirm(null)}
            >
              Отмена
            </Button>
            <Button
              variant="destructive"
              disabled={isPending}
              onClick={() => {
                const action = confirm
                setConfirm(null)

                if (action === 'hide') {
                  onHide()
                } else if (action === 'unverify') {
                  onUnverify()
                }
              }}
            >
              {confirm === 'hide' ? 'Скрыть из поиска' : 'Снять подтверждение'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}

/** Чек-лист готовности: шесть пунктов, по которым видно, чего не хватает. */
function ReadinessChecklist({ barber }: { barber: AdminBarberDetail }) {
  const items = [
    {
      key: 'profile',
      label: 'Профиль заполнен',
      value: barber.readiness.isProfileFilled
        ? 'Описание и опыт есть'
        : 'Нет описания или опыта',
      isDone: barber.readiness.isProfileFilled,
    },
    {
      key: 'email',
      label: 'Email подтверждён',
      value: barber.readiness.isEmailVerified ? 'Подтверждён' : 'Не подтверждён',
      isDone: barber.readiness.isEmailVerified,
    },
    {
      key: 'services',
      label: 'Услуги',
      value:
        barber.readiness.servicesCount === 0
          ? 'Ни одной услуги'
          : String(barber.readiness.servicesCount),
      isDone: barber.readiness.servicesCount > 0,
    },
    {
      key: 'salons',
      label: 'Салоны',
      value:
        barber.readiness.salonsCount === 0
          ? 'Не привязан ни к одному салону'
          : String(barber.readiness.salonsCount),
      isDone: barber.readiness.salonsCount > 0,
    },
    {
      key: 'schedule',
      label: 'Расписание',
      value:
        barber.readiness.scheduleDaysCount === 0
          ? 'Рабочих дней нет'
          : formatDaysCount(barber.readiness.scheduleDaysCount),
      isDone: barber.readiness.scheduleDaysCount > 0,
    },
    {
      key: 'photos',
      label: 'Фото в портфолио',
      value:
        barber.readiness.photosCount === 0
          ? 'Портфолио пустое'
          : String(barber.readiness.photosCount),
      isDone: barber.readiness.photosCount > 0,
    },
  ] as const

  const done = items.filter((item) => item.isDone).length

  return (
    <div className="rounded-lg border bg-muted/40 p-3">
      <div className="flex items-baseline justify-between gap-3">
        <h3 className="text-sm font-medium">Готовность профиля</h3>
        <span className="text-xs text-muted-foreground tabular-nums">
          {done} из {items.length}
        </span>
      </div>
      <p className="mt-1 text-xs text-muted-foreground">
        Подсказка, а не блокировка: подтвердить профиль можно и с минусами.
      </p>

      <ul className="mt-3 space-y-1.5">
        {items.map((item) => (
          <li key={item.key} className="flex items-start gap-2 text-sm">
            {item.isDone ? (
              <CheckCircle2
                className="mt-0.5 size-3.5 shrink-0 text-success"
                aria-hidden
              />
            ) : (
              <Circle
                className="mt-0.5 size-3.5 shrink-0 text-muted-foreground/50"
                aria-hidden
              />
            )}
            <span className="min-w-0 flex-1">
              {item.label}
              <span className="sr-only">
                {item.isDone ? ': готово' : ': не готово'}
              </span>
            </span>
            <span className="text-right text-xs text-muted-foreground">
              {item.value}
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** Заглушка правой колонки, пока барбер не выбран. */
export function BarberDetailPlaceholder() {
  return (
    <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Star className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Выберите барбера</p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          Слева список: откройте строку, чтобы увидеть контакты и готовность
          профиля.
        </p>
      </div>
    </div>
  )
}

/** Состояние правой колонки: загрузка, ошибка или потерянный профиль. */
export function BarberDetailState({
  status,
  error,
  onRetry,
  onBack,
}: {
  status: AdminBarberDetailStatus
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
          <p className="font-medium">Профиль не найден</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Возможно, ссылка устарела. Вернитесь к списку — он покажет актуальные
            профили.
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
        <div className="space-y-1">
          <p className="font-medium">Не удалось загрузить профиль</p>
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
        Загружаем профиль барбера…
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
