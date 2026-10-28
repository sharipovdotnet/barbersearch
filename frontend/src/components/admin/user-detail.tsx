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
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

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
            Назад
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
              {ADMIN_USER_ROLE_LABELS[user.role]}
            </p>
          </div>
        </div>

        <dl className="grid gap-4 border-t pt-4 sm:grid-cols-2">
          <ContextItem label="Телефон">
            {user.phoneNumber ?? 'Не указан'}
          </ContextItem>

          <ContextItem label="Последний вход">
            {user.lastLoginAt === null
              ? 'Ещё не входил'
              : formatDateTime(user.lastLoginAt)}
          </ContextItem>

          <ContextItem label="Создан">
            {formatDateTime(user.createdAt)}
          </ContextItem>

          <ContextItem label="Обновлён">
            {formatDateTime(user.updatedAt)}
          </ContextItem>

          <ContextItem label="Неудачные попытки входа">
            {user.failedLoginAttempts === 0
              ? 'Нет'
              : String(user.failedLoginAttempts)}
          </ContextItem>

          <ContextItem label="Временная блокировка">
            {user.lockoutEnd === null
              ? 'Нет'
              : `До ${formatDateTime(user.lockoutEnd)}`}
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

  return (
    <section
      aria-labelledby="user-confirmations-title"
      className="space-y-3 border-t pt-4"
    >
      <h3 id="user-confirmations-title" className="text-sm font-medium">
        Подтверждения
      </h3>

      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <div className="min-w-0">
          <dt className="text-xs text-muted-foreground">Email</dt>
          <dd className="mt-0.5 break-all">{user.email}</dd>
          <dd className="text-xs text-muted-foreground">
            {user.isEmailVerified ? 'Подтверждён' : 'Не подтверждён'}
          </dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs text-muted-foreground">Телефон</dt>
          <dd className="mt-0.5">{user.phoneNumber ?? 'Не указан'}</dd>
          <dd className="text-xs text-muted-foreground">
            {!hasPhone
              ? 'Подтверждать нечего'
              : user.isPhoneVerified
                ? 'Подтверждён'
                : 'Не подтверждён'}
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
            ? 'Подтверждаем…'
            : user.isEmailVerified
              ? 'Email подтверждён'
              : 'Подтвердить email'}
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
            ? 'Подтверждаем…'
            : user.isPhoneVerified
              ? 'Телефон подтверждён'
              : 'Подтвердить телефон'}
        </Button>
      </div>

      <p className="text-xs text-muted-foreground">
        {!hasPhone
          ? 'У пользователя не указан телефон — подтверждать нечего.'
          : user.isPhoneVerified && user.isEmailVerified
            ? 'Оба контакта подтверждены вручную: сервис не отправляет писем и SMS.'
            : 'Отметка ставится вручную: сервис не отправляет писем и SMS.'}
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
  const barberProfileId = user.barberProfileId
  const profile = user.barberProfile

  return (
    <section
      aria-labelledby="user-barber-profile-title"
      className="space-y-2 border-t pt-4"
    >
      <h3 id="user-barber-profile-title" className="text-sm font-medium">
        Профиль барбера
      </h3>

      {barberProfileId === null ? (
        <p className="text-sm text-muted-foreground">
          Профиль барбера не привязан: пользователь не работает как барбер.
        </p>
      ) : (
        <div className="rounded-lg border bg-muted/40 p-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="min-w-0 text-sm font-medium">
              {profile?.displayName ?? 'Профиль барбера'}
            </p>
            {profile && (
              <div className="flex flex-wrap items-center gap-1.5">
                <Badge variant={profile.isVerified ? 'secondary' : 'outline'}>
                  {profile.isVerified ? 'Подтверждён' : 'На проверке'}
                </Badge>
                <Badge variant="outline">
                  {profile.isActive ? 'Виден в поиске' : 'Скрыт из поиска'}
                </Badge>
              </div>
            )}
          </div>
          <p className="mt-1 text-xs text-muted-foreground">
            {profile === null
              ? 'Профиль привязан, но сервер не отдал его карточку.'
              : profile.isVerified && profile.isActive
                ? 'Профиль виден в поиске и доступен для записи.'
                : 'Профиль не показывается в поиске, пока не подтверждён и не активен.'}
          </p>
          <a
            href={barbersPath(barberProfileId)}
            className={cn(
              buttonVariants({ variant: 'outline', size: 'sm' }),
              'mt-2 h-11 sm:h-7'
            )}
            onClick={(event) => {
              event.preventDefault()
              navigate(barbersPath(barberProfileId))
            }}
          >
            Открыть карточку
          </a>
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        Привязка профиля здесь не меняется: выбрать профиль из списка пока нельзя.
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
        Действия
      </h3>

      {actionError && (
        <p role="alert" className="text-sm text-destructive">
          {actionError}
        </p>
      )}

      {isSelf ? (
        <p className="rounded-lg border bg-muted/40 p-3 text-sm text-muted-foreground">
          Это ваш аккаунт: роль и доступ здесь не меняются.
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
                    Меняем роль…
                  </>
                ) : (
                  'Изменить роль'
                )}
              </Button>
              <p className="text-xs text-muted-foreground sm:max-w-xs">
                Смена роли завершит все текущие сессии пользователя.
              </p>
            </div>
          </div>

          <div className="space-y-2 border-t pt-3">
            <p className="text-sm font-medium">
              {user.isActive ? 'Доступ открыт' : 'Доступ заблокирован'}
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
                    Блокируем…
                  </>
                ) : pendingAction === 'unblock' ? (
                  <>
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                    Разблокируем…
                  </>
                ) : user.isActive ? (
                  'Заблокировать доступ'
                ) : (
                  'Разблокировать'
                )}
              </Button>
              <p className="text-xs text-muted-foreground sm:max-w-xs">
                {user.isActive
                  ? 'Пользователь потеряет доступ, все его сессии завершатся. Записи и отзывы сохранятся.'
                  : 'Пользователь снова сможет войти. Роль, записи и отзывы не изменятся.'}
              </p>
            </div>
          </div>
        </>
      )}

      <Dialog open={isRoleOpen} onOpenChange={setIsRoleOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Изменить роль?</DialogTitle>
            <DialogDescription>
              Новая роль начнёт действовать сразу после сохранения.
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col gap-2">
            <label htmlFor="user-role" className="text-sm font-medium">
              Роль
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
                  {ADMIN_USER_ROLE_LABELS[value]}
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
              Смена роли завершит все текущие сессии пользователя.
            </p>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              disabled={isPending}
              onClick={() => setIsRoleOpen(false)}
            >
              Отмена
            </Button>
            <Button
              type="button"
              disabled={isPending || role === user.role}
              onClick={() => {
                setIsRoleOpen(false)
                onChangeRole(role)
              }}
            >
              Изменить роль
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={isBlockOpen} onOpenChange={setIsBlockOpen}>
        <DialogContent role="alertdialog">
          <DialogHeader>
            <DialogTitle>Заблокировать доступ?</DialogTitle>
            <DialogDescription>
              Пользователь потеряет доступ, все его сессии завершатся. Записи и
              отзывы сохранятся.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              disabled={isPending}
              onClick={() => setIsBlockOpen(false)}
            >
              Отмена
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
              Заблокировать доступ
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}

/** Заглушка правой колонки, пока пользователь не выбран. */
export function UserDetailPlaceholder() {
  return (
    <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Users className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Выберите пользователя</p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          Слева список: откройте строку, чтобы увидеть контакты, роль и
          подтверждения.
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
  if (status === 'notFound') {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Circle className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Пользователь не найден</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Возможно, ссылка устарела. Вернитесь к списку — он покажет актуальные
            аккаунты.
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
          <p className="font-medium">Не удалось загрузить пользователя</p>
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
        Загружаем пользователя…
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
