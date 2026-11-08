import { useState, type ReactNode } from 'react'
import {
  ArrowLeft,
  Circle,
  Loader2,
  TriangleAlert,
  Users,
} from 'lucide-react'

import {
  UserRoleBadge,
  UserStatusBadge,
} from '@/components/admin/user-list'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import type { AdminUserDetailStatus } from '@/hooks/use-admin-user'
import { useI18n } from '@/i18n'
import {
  ADMIN_USER_ROLE_LABELS,
  ADMIN_USER_ROLES,
  getUserDisplayName,
  type AdminUserAction,
  type AdminUserDetail,
  type AdminUserRole,
} from '@/lib/admin-users'
import { barbersPath } from '@/lib/admin-path'
import { formatDateTime, getInitials } from '@/lib/format'
import { PRIVACY_POLICY_VERSION } from '@/lib/legal'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * «03.11.2026, 14:05» в UTC: согласие датируется серверным временем, поэтому пояс
 * не зависит от машины администратора. Собирается из частей — формат не плывёт
 * от версии ICU.
 */
const CONSENT_MOMENT_FORMATTER = new Intl.DateTimeFormat('ru-RU', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
  timeZone: 'UTC',
})

function formatConsentMoment(iso: string): string {
  const parts = new Map(
    CONSENT_MOMENT_FORMATTER.formatToParts(new Date(iso)).map((part) => [
      part.type,
      part.value,
    ])
  )

  return `${parts.get('day')}.${parts.get('month')}.${parts.get('year')}, ${parts.get('hour')}:${parts.get('minute')}`
}

/** «2026-11-03» → «03.11.2026»: дата редакции, до которой записи согласия не было. */
function formatPolicyDate(version: string): string {
  const [year, month, day] = version.split('-')

  return `${day}.${month}.${year}`
}

/**
 * Карточка пользователя: контакты, подтверждения, метаданные входа и действия.
 *
 * Смена роли и блокировка необратимы для текущих сессий — сервер завершает их через
 * SecurityStamp, — поэтому оба действия спрашивают подтверждение и прямо называют
 * последствие. Подтверждения email и телефона обратимы и выполняются сразу.
 */
export function UserDetail({
  user,
  isSelf,
  pendingAction,
  actionError,
  onChangeRole,
  onBlock,
  onUnblock,
  onVerifyEmail,
  onVerifyPhone,
  onBack,
}: {
  user: AdminUserDetail
  /** Собственный аккаунт администратора: роль и доступ здесь не меняются. */
  isSelf: boolean
  pendingAction: AdminUserAction | null
  actionError: string | null
  onChangeRole: (role: AdminUserRole) => void
  onBlock: () => void
  onUnblock: () => void
  onVerifyEmail: () => void
  onVerifyPhone: () => void
  onBack: () => void
}) {
  const { t } = useI18n()
  const name = getUserDisplayName(user)

  return (
    <section
      id="user-detail"
      aria-labelledby="user-detail-title"
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
            {t('Назад')}
          </Button>
          <div className="ml-auto flex flex-wrap items-center justify-end gap-1.5">
            <UserRoleBadge role={user.role} />
            <UserStatusBadge isActive={user.isActive} />
          </div>
        </div>

        <div className="flex items-start gap-3">
          <Avatar size="lg">
            {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" />}
            <AvatarFallback>{getInitials(name)}</AvatarFallback>
          </Avatar>
          <div className="min-w-0">
            <h2
              id="user-detail-title"
              className="font-heading text-xl leading-tight font-semibold tracking-tight"
            >
              {name}
            </h2>
            <p className="mt-0.5 text-sm break-all text-muted-foreground">
              {user.email}
            </p>
            <p className="mt-1 text-xs text-muted-foreground">
              {t(ADMIN_USER_ROLE_LABELS[user.role])}
            </p>
          </div>
        </div>

        <dl className="grid gap-4 border-t pt-4 sm:grid-cols-2">
          <ContextItem label={t('Телефон')}>
            {user.phoneNumber ?? t('Не указан')}
          </ContextItem>

          <ContextItem label={t('Последний вход')}>
            {user.lastLoginAt === null
              ? t('Ещё не входил')
              : formatDateTime(user.lastLoginAt)}
          </ContextItem>

          <ContextItem label={t('Создан')}>
            {formatDateTime(user.createdAt)}
          </ContextItem>

          <ContextItem label={t('Обновлён')}>
            {formatDateTime(user.updatedAt)}
          </ContextItem>

          {/* Только чтение: согласие нельзя выдать или отозвать из админки. */}
          <ContextItem label={t('Согласие на обработку ПД')}>
            {user.consentPolicyVersion === null || user.consentedAt === null
              ? t('Записи нет — аккаунт создан до {date}', {
                  date: formatPolicyDate(PRIVACY_POLICY_VERSION),
                })
              : t('Редакция {version} · {moment} UTC', {
                  version: user.consentPolicyVersion,
                  moment: formatConsentMoment(user.consentedAt),
                })}
          </ContextItem>

          <ContextItem label={t('Неудачные попытки входа')}>
            {user.failedLoginAttempts === 0
              ? t('Нет')
              : String(user.failedLoginAttempts)}
          </ContextItem>

          <ContextItem label={t('Временная блокировка')}>
            {user.lockoutEnd === null
              ? t('Нет')
              : t('До {date}', { date: formatDateTime(user.lockoutEnd) })}
          </ContextItem>
        </dl>

        <Confirmations
          user={user}
          pendingAction={pendingAction}
          onVerifyEmail={onVerifyEmail}
          onVerifyPhone={onVerifyPhone}
        />

        <BarberProfileBlock user={user} />

        <UserActions
          user={user}
          isSelf={isSelf}
          pendingAction={pendingAction}
          actionError={actionError}
          onChangeRole={onChangeRole}
          onBlock={onBlock}
          onUnblock={onUnblock}
        />
      </div>
    </section>
  )
}

/**
 * Подтверждения email и телефона.
 *
 * Телефон подтверждается только при заполненном номере, поэтому кнопка не просто
 * гаснет, а объясняет, чего не хватает.
 */
function Confirmations({
  user,
  pendingAction,
  onVerifyEmail,
  onVerifyPhone,
}: {
  user: AdminUserDetail
  pendingAction: AdminUserAction | null
  onVerifyEmail: () => void
  onVerifyPhone: () => void
}) {
  const isPending = pendingAction !== null
  const hasPhone = user.phoneNumber !== null
  const { t } = useI18n()

  return (
    <section
      aria-labelledby="user-confirmations-title"
      className="space-y-3 border-t pt-4"
    >
      <h3 id="user-confirmations-title" className="text-sm font-medium">
        {t('Подтверждения')}
      </h3>

      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <div className="min-w-0">
          <dt className="text-xs text-muted-foreground">Email</dt>
          <dd className="mt-0.5 break-all">{user.email}</dd>
          <dd className="text-xs text-muted-foreground">
            {user.isEmailVerified ? t('Подтверждён') : t('Не подтверждён')}
          </dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs text-muted-foreground">{t('Телефон')}</dt>
          <dd className="mt-0.5">{user.phoneNumber ?? t('Не указан')}</dd>
          <dd className="text-xs text-muted-foreground">
            {!hasPhone
              ? t('Подтверждать нечего')
              : user.isPhoneVerified
                ? t('Подтверждён')
                : t('Не подтверждён')}
          </dd>
        </div>
      </dl>

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <Button
          variant="outline"
          className="h-11 sm:h-9"
          disabled={isPending || user.isEmailVerified}
          onClick={onVerifyEmail}
        >
          {pendingAction === 'verifyEmail' && (
            <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
          )}
          {pendingAction === 'verifyEmail'
            ? t('Подтверждаем…')
            : user.isEmailVerified
              ? t('Email подтверждён')
              : t('Подтвердить email')}
        </Button>
        <Button
          variant="outline"
          className="h-11 sm:h-9"
          disabled={isPending || !hasPhone || user.isPhoneVerified}
          onClick={onVerifyPhone}
        >
          {pendingAction === 'verifyPhone' && (
            <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
          )}
          {pendingAction === 'verifyPhone'
            ? t('Подтверждаем…')
            : user.isPhoneVerified
              ? t('Телефон подтверждён')
              : t('Подтвердить телефон')}
        </Button>
      </div>

      <p className="text-xs text-muted-foreground">
        {!hasPhone
          ? t('У пользователя не указан телефон — подтверждать нечего.')
          : user.isPhoneVerified && user.isEmailVerified
            ? t(
                'Оба контакта подтверждены вручную: сервис не отправляет писем и SMS.'
              )
            : t('Отметка ставится вручную: сервис не отправляет писем и SMS.')}
      </p>
    </section>
  )
}

/**
 * Профиль барбера — только чтение.
 *
 * Привязку не выводим: выбрать профиль из списка нечем, а свободный ввод
 * идентификатора — не действие. Статус и ссылка на карточку дают контекст.
 */
function BarberProfileBlock({ user }: { user: AdminUserDetail }) {
  const { t } = useI18n()
  const barberProfileId = user.barberProfileId
  const profile = user.barberProfile

  return (
    <section
      aria-labelledby="user-barber-profile-title"
      className="space-y-2 border-t pt-4"
    >
      <h3 id="user-barber-profile-title" className="text-sm font-medium">
        {t('Профиль барбера')}
      </h3>

      {barberProfileId === null ? (
        <p className="text-sm text-muted-foreground">
          {t('Профиль барбера не привязан: пользователь не работает как барбер.')}
        </p>
      ) : (
        <div className="rounded-lg border bg-muted/40 p-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="min-w-0 text-sm font-medium">
              {profile?.displayName ?? t('Профиль барбера')}
            </p>
            {profile && (
              <div className="flex flex-wrap items-center gap-1.5">
                <Badge variant={profile.isVerified ? 'secondary' : 'outline'}>
                  {profile.isVerified ? t('Подтверждён') : t('На проверке')}
                </Badge>
                <Badge variant="outline">
                  {profile.isActive ? t('Виден в поиске') : t('Скрыт из поиска')}
                </Badge>
              </div>
            )}
          </div>
          <p className="mt-1 text-xs text-muted-foreground">
            {profile === null
              ? t('Профиль привязан, но сервер не отдал его карточку.')
              : profile.isVerified && profile.isActive
                ? t('Профиль виден в поиске и доступен для записи.')
                : t(
                    'Профиль не показывается в поиске, пока не подтверждён и не активен.'
                  )}
          </p>
          <a
            href={barbersPath(barberProfileId, 'all')}
            className={cn(
              buttonVariants({ variant: 'outline', size: 'sm' }),
              'mt-2 h-11 sm:h-7'
            )}
            onClick={(event) => {
              event.preventDefault()
              navigate(barbersPath(barberProfileId, 'all'))
            }}
          >
            {t('Открыть карточку')}
          </a>
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        {t(
          'Привязка профиля здесь не меняется: выбрать профиль из списка пока нельзя.'
        )}
      </p>
    </section>
  )
}

/**
 * Административные действия.
 *
 * Для собственного аккаунта смена роли и блокировка скрыты: сервер всё равно
 * откажет (`user.role.self`, `user.deactivation.self`), а показывать кнопку,
 * которая гарантированно не сработает, — плохой сценарий.
 */
function UserActions({
  user,
  isSelf,
  pendingAction,
  actionError,
  onChangeRole,
  onBlock,
  onUnblock,
}: {
  user: AdminUserDetail
  isSelf: boolean
  pendingAction: AdminUserAction | null
  actionError: string | null
  onChangeRole: (role: AdminUserRole) => void
  onBlock: () => void
  onUnblock: () => void
}) {
  const [roleDraft, setRoleDraft] = useState({
    serverRole: user.role,
    value: user.role,
  })
  const [isRoleOpen, setIsRoleOpen] = useState(false)
  const [isBlockOpen, setIsBlockOpen] = useState(false)
  const isPending = pendingAction !== null
  const { t } = useI18n()

  // Черновик роли живёт до ответа сервера: после перечитывания карточки
  // `user.role` меняется, и выбор снова берётся из данных, а не из памяти формы.
  const role = roleDraft.serverRole === user.role ? roleDraft.value : user.role

  function openRoleDialog() {
    setRoleDraft({ serverRole: user.role, value: user.role })
    setIsRoleOpen(true)
  }

  return (
    <section
      aria-labelledby="user-actions-title"
      className="space-y-4 border-t pt-4"
    >
      <h3 id="user-actions-title" className="text-sm font-medium">
        {t('Действия')}
      </h3>

      {actionError && (
        <p role="alert" className="text-sm text-destructive">
          {actionError}
        </p>
      )}

      {isSelf ? (
        <p className="rounded-lg border bg-muted/40 p-3 text-sm text-muted-foreground">
          {t('Это ваш аккаунт: роль и доступ здесь не меняются.')}
        </p>
      ) : (
        <>
          <div className="space-y-2">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
              <Button
                variant="outline"
                className="h-11 sm:h-9"
                disabled={isPending}
                onClick={openRoleDialog}
              >
                {pendingAction === 'role' ? (
                  <>
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                    {t('Меняем роль…')}
                  </>
                ) : (
                  t('Изменить роль')
                )}
              </Button>
              <p className="text-xs text-muted-foreground sm:max-w-xs">
                {t('Смена роли завершит все текущие сессии пользователя.')}
              </p>
            </div>
          </div>

          <div className="space-y-2 border-t pt-3">
            <p className="text-sm font-medium">
              {user.isActive ? t('Доступ открыт') : t('Доступ заблокирован')}
            </p>
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
              <Button
                variant={user.isActive ? 'destructive' : 'outline'}
                className="h-11 sm:h-9"
                disabled={isPending}
                onClick={user.isActive ? () => setIsBlockOpen(true) : onUnblock}
              >
                {pendingAction === 'block' ? (
                  <>
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                    {t('Блокируем…')}
                  </>
                ) : pendingAction === 'unblock' ? (
                  <>
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                    {t('Разблокируем…')}
                  </>
                ) : user.isActive ? (
                  t('Заблокировать доступ')
                ) : (
                  t('Разблокировать')
                )}
              </Button>
              <p className="text-xs text-muted-foreground sm:max-w-xs">
                {user.isActive
                  ? t(
                      'Пользователь потеряет доступ, все его сессии завершатся. Записи и отзывы сохранятся.'
                    )
                  : t(
                      'Пользователь снова сможет войти. Роль, записи и отзывы не изменятся.'
                    )}
              </p>
            </div>
          </div>
        </>
      )}

      <Dialog open={isRoleOpen} onOpenChange={setIsRoleOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t('Изменить роль?')}</DialogTitle>
            <DialogDescription>
              {t('Новая роль начнёт действовать сразу после сохранения.')}
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-2">
            <label htmlFor="user-role" className="text-sm font-medium">
              {t('Роль')}
            </label>
            <select
              id="user-role"
              value={role}
              disabled={isPending}
              onChange={(event) => {
                setRoleDraft({
                  serverRole: user.role,
                  value: event.target.value as AdminUserRole,
                })
              }}
              className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-9"
            >
              {ADMIN_USER_ROLES.map((value) => (
                <option key={value} value={value}>
                  {t(ADMIN_USER_ROLE_LABELS[value])}
                </option>
              ))}
            </select>
          </div>

          <div className="flex gap-2.5 rounded-lg bg-destructive/10 p-3">
            <TriangleAlert
              className="mt-0.5 size-4 shrink-0 text-destructive"
              aria-hidden
            />
            <p className="text-sm">
              {t('Смена роли завершит все текущие сессии пользователя.')}
            </p>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              disabled={isPending}
              onClick={() => setIsRoleOpen(false)}
            >
              {t('Отмена')}
            </Button>
            <Button
              type="button"
              disabled={isPending || role === user.role}
              onClick={() => {
                setIsRoleOpen(false)
                onChangeRole(role)
              }}
            >
              {t('Изменить роль')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isBlockOpen} onOpenChange={setIsBlockOpen}>
        <DialogContent role="alertdialog">
          <DialogHeader>
            <DialogTitle>{t('Заблокировать доступ?')}</DialogTitle>
            <DialogDescription>
              {t(
                'Пользователь потеряет доступ, все его сессии завершатся. Записи и отзывы сохранятся.'
              )}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              disabled={isPending}
              onClick={() => setIsBlockOpen(false)}
            >
              {t('Отмена')}
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={isPending}
              onClick={() => {
                setIsBlockOpen(false)
                onBlock()
              }}
            >
              {t('Заблокировать доступ')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}

/** Заглушка правой колонки, пока пользователь не выбран. */
export function UserDetailPlaceholder() {
  const { t } = useI18n()

  return (
    <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Users className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">{t('Выберите пользователя')}</p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {t(
            'Слева список: откройте строку, чтобы увидеть контакты, роль и подтверждения.'
          )}
        </p>
      </div>
    </div>
  )
}

/** Состояние правой колонки: загрузка, ошибка или потерянный пользователь. */
export function UserDetailState({
  status,
  error,
  onRetry,
  onBack,
}: {
  status: AdminUserDetailStatus
  error: string | null
  onRetry: () => void
  onBack: () => void
}) {
  const { t } = useI18n()

  if (status === 'notFound') {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Circle className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{t('Пользователь не найден')}</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            {t(
              'Возможно, ссылка устарела. Вернитесь к списку — он покажет актуальные аккаунты.'
            )}
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onBack}
        >
          {t('К списку')}
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
          <p className="font-medium">{t('Не удалось загрузить пользователя')}</p>
          <p
            role="alert"
            className="mx-auto max-w-sm text-sm text-muted-foreground"
          >
            {error ?? t('Попробуйте ещё раз.')}
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onRetry}
        >
          {t('Повторить')}
        </Button>
      </div>
    )
  }

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        {t('Загружаем пользователя…')}
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
