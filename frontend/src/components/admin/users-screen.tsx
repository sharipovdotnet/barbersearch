import { useEffect, useRef, useState } from 'react'
import {
  Inbox,
  Loader2,
  RefreshCw,
  Search,
  TriangleAlert,
  Users,
  X,
} from 'lucide-react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import { CreateUserDialog } from '@/components/admin/create-user-dialog'
import {
  UserDetail,
  UserDetailPlaceholder,
  UserDetailState,
} from '@/components/admin/user-detail'
import { UserList } from '@/components/admin/user-list'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminUser } from '@/hooks/use-admin-user'
import { useAdminUserList } from '@/hooks/use-admin-user-list'
import { useMediaQuery } from '@/hooks/use-media-query'
import { useSession } from '@/hooks/use-session'
import { t, useI18n } from '@/i18n'
import {
  ADMIN_USER_ROLE_FILTER_LABELS,
  ADMIN_USER_ROLE_LABELS,
  ADMIN_USER_SEARCH_MAX_LENGTH,
  ADMIN_USER_STATUS_LABELS,
  blockUser,
  blockUserAvatar,
  changeUserRole,
  describeAdminUserActionError,
  getUserDisplayName,
  isUserUnauthorized,
  unblockUser,
  unblockUserAvatar,
  verifyUserEmail,
  verifyUserPhone,
  type AdminUserAction,
  type AdminUserRole,
  type AdminUserRoleFilter,
  type AdminUserRow,
  type AdminUserStatusFilter,
} from '@/lib/admin-users'
import { usersPath } from '@/lib/admin-path'
import { formatClockTime } from '@/lib/format'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

const STATUS_FILTERS: readonly {
  value: AdminUserStatusFilter
  label: string
}[] = [
  { value: 'all', label: ADMIN_USER_STATUS_LABELS.all },
  { value: 'active', label: ADMIN_USER_STATUS_LABELS.active },
  { value: 'blocked', label: ADMIN_USER_STATUS_LABELS.blocked },
  { value: 'unverified', label: ADMIN_USER_STATUS_LABELS.unverified },
]

const ROLE_FILTERS: readonly {
  value: AdminUserRoleFilter
  label: string
}[] = [
  { value: 'all', label: ADMIN_USER_ROLE_FILTER_LABELS.all },
  { value: 'Customer', label: ADMIN_USER_ROLE_FILTER_LABELS.Customer },
  { value: 'Barber', label: ADMIN_USER_ROLE_FILTER_LABELS.Barber },
  { value: 'Admin', label: ADMIN_USER_ROLE_FILTER_LABELS.Admin },
]

type PendingAction = {
  kind: AdminUserAction
  userId: string
} | null

type Announcement = {
  text: string
  undo?: {
    label: string
    action: AdminUserAction
    userId: string
    userName: string
    role?: AdminUserRole
  }
}

/**
 * Экран «Пользователи»: роли, доступ и подтверждения.
 *
 * Сетка — split-view на широком экране; на телефоне список и карточка сменяют
 * друг друга, а выбранный аккаунт живёт в адресе, поэтому перезагрузка не теряет
 * контекст. Поиск отправляется с задержкой: запрос на каждую букву только грел бы
 * лимит. Счётчиков на фильтрах нет — API не отдаёт общее число.
 */
export function UsersScreen({ userId }: { userId: string | null }) {
  const { t } = useI18n()
  const [statusFilter, setStatusFilter] = useState<AdminUserStatusFilter>('all')
  const [roleFilter, setRoleFilter] = useState<AdminUserRoleFilter>('all')
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')

  // Задержка перед запросом: 300 мс хватает, чтобы не искать на каждую букву.
  useEffect(() => {
    const trimmed = searchInput.trim()

    if (trimmed === search) {
      return
    }

    const timer = window.setTimeout(() => setSearch(trimmed), 300)

    return () => window.clearTimeout(timer)
  }, [searchInput, search])

  const list = useAdminUserList({
    status: statusFilter,
    role: roleFilter,
    search,
  })
  const detail = useAdminUser(userId)
  const { user: sessionUser } = useSession()
  const isWide = useMediaQuery('(min-width: 1024px)')
  const headingRef = useRef<HTMLHeadingElement | null>(null)

  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [actionFailure, setActionFailure] = useState<{
    userId: string
    message: string
  } | null>(null)
  const [announcement, setAnnouncement] = useState<Announcement | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)

  const user = detail.user
  const isSelf = user !== null && sessionUser?.id === user.id
  const actionError =
    user && actionFailure?.userId === user.id
      ? actionFailure.message
      : null
  const currentPendingAction =
    user && pendingAction?.userId === user.id ? pendingAction.kind : null

  // На широком экране первая строка открывается сразу: пустая правая колонка
  // выглядит как ошибка загрузки.
  useEffect(() => {
    if (
      !isWide ||
      userId ||
      list.status !== 'ready' ||
      list.items.length === 0
    ) {
      return
    }

    navigate(usersPath(list.items[0].id), { replace: true })
  }, [isWide, userId, list.status, list.items])

  // На телефоне карточка заменяет список — переход должен начинаться сверху.
  useEffect(() => {
    if (!userId || isWide) {
      return
    }

    window.scrollTo({ top: 0 })
  }, [userId, isWide])

  function resetSearch() {
    setSearchInput('')
    setSearch('')
  }

  function handleStatusFilterChange(next: AdminUserStatusFilter) {
    if (next === statusFilter) {
      return
    }

    setStatusFilter(next)
    // Поиск сбрасывается вместе с фильтром: иначе «Заблокированные» с прошлым
    // запросом выглядит как пустой список, хотя дело в поиске.
    resetSearch()
  }

  function handleRoleFilterChange(next: AdminUserRoleFilter) {
    if (next === roleFilter) {
      return
    }

    setRoleFilter(next)
    resetSearch()
  }

  function handleSelect(id: string) {
    setActionFailure(null)
    navigate(usersPath(id))
  }

  async function runAction(
    kind: AdminUserAction,
    // `userId` — владелец аватара; здесь он совпадает с `id`, но тип общий
    // с карточкой барбера, где аватар принадлежит владельцу профиля.
    target: { id: string; displayName: string; userId?: string },
    payload?: { role?: AdminUserRole }
  ) {
    if (pendingAction) {
      return
    }

    if (kind === 'role' && !payload?.role) {
      return
    }

    // Прежняя роль нужна для отмены: карточка после ответа уже покажет новую.
    const previousRole = user?.id === target.id ? user.role : undefined
    const nextRole = payload?.role

    setPendingAction({ kind, userId: target.id })
    setActionFailure(null)

    try {
      switch (kind) {
        case 'role':
          if (nextRole) {
            await changeUserRole(target.id, nextRole)
          }
          break
        case 'block':
          await blockUser(target.id)
          break
        case 'unblock':
          await unblockUser(target.id)
          break
        case 'verifyEmail':
          await verifyUserEmail(target.id)
          break
        case 'verifyPhone':
          await verifyUserPhone(target.id)
          break
        case 'blockAvatar':
          await blockUserAvatar(target.userId ?? target.id)
          break
        case 'unblockAvatar':
          await unblockUserAvatar(target.userId ?? target.id)
          break
      }

      setAnnouncement(
        announcementFor(kind, target, previousRole, nextRole)
      )

      // Сервер — источник правды для карточки. Строку списка правим локально
      // сразу, поэтому результат действия виден, даже если тихое обновление
      // списка не дойдёт.
      detail.reload()

      const row = list.items.find((item) => item.id === target.id)

      if (row) {
        const next = updatedUserRow(row, kind, nextRole)

        if (matchesUserFilters(next, statusFilter, roleFilter)) {
          list.replaceItem(next.id, next)
        } else {
          list.removeItem(next.id)
        }
      }

      // Прежнее тихое обновление списка сохранено: «Обновлено в HH:MM» и
      // остальные строки остаются серверной правдой, а карточка не сбрасывается.
      list.refresh()
    } catch (cause) {
      if (isUserUnauthorized(cause)) {
        setRequiresSignIn(true)
        return
      }

      setActionFailure({
        userId: target.id,
        message: describeAdminUserActionError(cause, kind),
      })
    } finally {
      setPendingAction(null)
    }
  }

  async function handleUndo(entry: Announcement) {
    if (!entry.undo || pendingAction) {
      return
    }

    setAnnouncement(null)

    await runAction(
      entry.undo.action,
      {
        id: entry.undo.userId,
        displayName: entry.undo.userName,
      },
      entry.undo.role ? { role: entry.undo.role } : undefined
    )
  }

  function handleBack() {
    navigate(usersPath())

    // Фокус возвращается на заголовок, а не в <body>: на телефоне списка ещё нет,
    // пока страница не перерисовалась.
    window.requestAnimationFrame(() => {
      window.requestAnimationFrame(() => headingRef.current?.focus())
    })
  }

  const trimmedSearch = searchInput.trim()
  const isEmpty =
    list.status === 'ready' && list.items.length === 0 && userId === null
  const isMobileDetail = !isWide && userId !== null
  const showGrid =
    list.status === 'ready' && (list.items.length > 0 || userId !== null)

  if (list.requiresSignIn || requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate
          returnTo={usersPath(userId)}
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
          {t('Пользователи')}
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
              {t('Пользователи')}
            </h1>
            <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
              {t(
                'Роли, доступ и подтверждения. Смена роли и блокировка завершают все сессии пользователя, записи и отзывы при этом сохраняются.'
              )}
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
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
              className="h-11 sm:h-7"
              disabled={list.isRefreshing || list.status === 'loading'}
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
            <CreateUserDialog onCreated={list.refresh} />
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
          <p className="min-w-0 flex-1">{announcement.text}</p>
          <div className="flex items-center gap-1">
            {announcement.undo && (
              <Button
                variant="outline"
                size="sm"
                className="h-11 sm:h-7"
                disabled={pendingAction !== null}
                onClick={() => {
                  void handleUndo(announcement)
                }}
              >
                {announcement.undo.label}
              </Button>
            )}
            <button
              type="button"
              aria-label={t('Скрыть сообщение')}
              className="inline-flex size-11 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:size-auto sm:p-1"
              onClick={() => setAnnouncement(null)}
            >
              <X className="size-3.5" aria-hidden />
            </button>
          </div>
        </div>
      )}

      {!isMobileDetail && (
        <div className="flex flex-wrap items-center gap-2">
          <SegmentedFilter
            label={t('Статус пользователей')}
            value={statusFilter}
            options={STATUS_FILTERS}
            onChange={handleStatusFilterChange}
          />
          <SegmentedFilter
            label={t('Роль пользователей')}
            value={roleFilter}
            options={ROLE_FILTERS}
            onChange={handleRoleFilterChange}
          />

          <div className="relative w-full sm:ml-auto sm:w-64">
            <label htmlFor="users-search" className="sr-only">
              {t('Поиск по имени или email')}
            </label>
            <Search
              className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden
            />
            <Input
              id="users-search"
              type="search"
              value={searchInput}
              maxLength={ADMIN_USER_SEARCH_MAX_LENGTH}
              placeholder={t('Имя или email')}
              className="h-11 pr-11 pl-8 sm:h-8 sm:pr-9"
              onChange={(event) => setSearchInput(event.target.value)}
            />
            {searchInput !== '' && (
              <button
                type="button"
                aria-label={t('Очистить поиск')}
                className="absolute top-1/2 right-1 inline-flex size-11 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:right-1.5 sm:size-auto sm:p-1"
                onClick={resetSearch}
              >
                <X className="size-3.5" aria-hidden />
              </button>
            )}
          </div>
        </div>
      )}

      <p role="status" className="sr-only">
        {list.status === 'ready'
          ? t('Показано {count}', { count: list.items.length })
          : ''}
      </p>

      {list.status === 'loading' && <UsersSkeleton />}

      {list.status === 'error' && (
        <UsersError
          message={list.error ?? t('Не удалось загрузить пользователей.')}
          onRetry={list.reload}
        />
      )}

      {isEmpty && (
        <UsersEmpty
          status={statusFilter}
          role={roleFilter}
          search={trimmedSearch}
          onRefresh={list.refresh}
        />
      )}

      {showGrid && (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
          <div className={cn('min-w-0', userId ? 'hidden lg:block' : 'block')}>
            {list.items.length > 0 ? (
              <UserList
                items={list.items}
                selectedId={userId}
                offset={list.offset}
                pageIndex={list.pageIndex}
                hasPrev={list.hasPrev}
                hasNext={list.hasNext}
                isLoadingPage={list.isLoadingPage}
                pageError={list.pageError}
                focusTargetRef={headingRef}
                // Карточка рендерится только при выбранном пользователе: на
                // телефоне список живёт без неё, поэтому `aria-controls`
                // ссылается на существующий элемент или отсутствует.
                detailPanelId={user ? 'user-detail' : null}
                onSelect={handleSelect}
                onPrev={list.goPrev}
                onNext={list.goNext}
              />
            ) : (
              <p className="text-sm text-muted-foreground">
                {t('В загруженной странице нет пользователей.')}
              </p>
            )}
          </div>

          <div className={cn('min-w-0', userId ? 'block' : 'hidden lg:block')}>
            {user ? (
              <UserDetail
                user={user}
                isSelf={isSelf}
                pendingAction={currentPendingAction}
                actionError={actionError}
                onChangeRole={(role) => {
                  void runAction(
                    'role',
                    { id: user.id, displayName: getUserDisplayName(user) },
                    { role }
                  )
                }}
                onBlock={() => {
                  void runAction('block', {
                    id: user.id,
                    displayName: getUserDisplayName(user),
                  })
                }}
                onUnblock={() => {
                  void runAction('unblock', {
                    id: user.id,
                    displayName: getUserDisplayName(user),
                  })
                }}
                onVerifyEmail={() => {
                  void runAction('verifyEmail', {
                    id: user.id,
                    displayName: getUserDisplayName(user),
                  })
                }}
                onVerifyPhone={() => {
                  void runAction('verifyPhone', {
                    id: user.id,
                    displayName: getUserDisplayName(user),
                  })
                }}
                onBlockAvatar={() => {
                  void runAction('blockAvatar', {
                    id: user.id,
                    displayName: getUserDisplayName(user),
                  })
                }}
                onUnblockAvatar={() => {
                  void runAction('unblockAvatar', {
                    id: user.id,
                    displayName: getUserDisplayName(user),
                  })
                }}
                onBack={handleBack}
              />
            ) : userId ? (
              <UserDetailState
                status={detail.status}
                error={detail.error}
                onRetry={detail.reload}
                onBack={handleBack}
              />
            ) : (
              <UserDetailPlaceholder />
            )}
          </div>
        </div>
      )}
    </div>
  )
}

/**
 * Объявление после действия. Роль и блокировка обратимы, поэтому у них есть
 * подсказка, как вернуть прежнее состояние. У подтверждений отмены нет —
 * обратного эндпоинта в контракте не существует.
 */
function announcementFor(
  kind: AdminUserAction,
  target: { id: string; displayName: string },
  previousRole: AdminUserRole | undefined,
  role: AdminUserRole | undefined
): Announcement {
  const name = target.displayName

  switch (kind) {
    case 'role': {
      const label = role ? t(ADMIN_USER_ROLE_LABELS[role]) : ''
      const undoRole =
        previousRole && previousRole !== role ? previousRole : null

      return {
        text: role
          ? t('Роль изменена: «{role}». Все сессии пользователя завершены.', {
              role: label,
            })
          : t('Роль изменена.'),
        undo: undoRole
          ? {
              label: t('Вернуть роль «{role}»', {
                role: t(ADMIN_USER_ROLE_LABELS[undoRole]),
              }),
              action: 'role',
              userId: target.id,
              userName: name,
              role: undoRole,
            }
          : undefined,
      }
    }
    case 'block':
      return {
        text: t('Доступ заблокирован: «{name}». Все сессии завершены.', {
          name,
        }),
        undo: {
          label: t('Разблокировать'),
          action: 'unblock',
          userId: target.id,
          userName: name,
        },
      }
    case 'unblock':
      return {
        text: t('Доступ восстановлен: «{name}».', { name }),
        undo: {
          label: t('Заблокировать'),
          action: 'block',
          userId: target.id,
          userName: name,
        },
      }
    case 'verifyEmail':
      return { text: t('Email подтверждён: «{name}».', { name }) }
    case 'verifyPhone':
      return { text: t('Телефон подтверждён: «{name}».', { name }) }
    case 'blockAvatar':
      return {
        text: t('Фото скрыто: его больше нет в публичных местах.'),
        undo: {
          label: t('Вернуть фото'),
          action: 'unblockAvatar',
          userId: target.id,
          userName: name,
        },
      }
    case 'unblockAvatar':
      return {
        text: t('Фото снова видно всем.'),
        undo: {
          label: t('Скрыть фото'),
          action: 'blockAvatar',
          userId: target.id,
          userName: name,
        },
      }
  }
}

/**
 * Строка после действия: команда меняет только своё поле, остальное берётся
 * из уже загруженной страницы. Локальная правка видна сразу, не дожидаясь
 * тихого обновления списка.
 */
function updatedUserRow(
  user: AdminUserRow,
  kind: AdminUserAction,
  role?: AdminUserRole
): AdminUserRow {
  switch (kind) {
    case 'role':
      return role ? { ...user, role } : user
    case 'block':
      return { ...user, isActive: false }
    case 'unblock':
      return { ...user, isActive: true }
    case 'verifyEmail':
      return { ...user, isEmailVerified: true }
    case 'verifyPhone':
      return { ...user, isPhoneVerified: true }
    case 'blockAvatar':
      return { ...user, isAvatarBlocked: true }
    case 'unblockAvatar':
      return { ...user, isAvatarBlocked: false }
  }
}

/**
 * Подходит ли строка под фильтры статуса и роли — по тем же правилам, что
 * в запросе сервера (`BuildAdminUsersQuery`): «Активные» — `isActive`,
 * «Заблокированные» — `!isActive`, «Без подтверждения email» — `!isEmailVerified`.
 */
function matchesUserFilters(
  user: AdminUserRow,
  status: AdminUserStatusFilter,
  role: AdminUserRoleFilter
): boolean {
  if (role !== 'all' && user.role !== role) {
    return false
  }

  switch (status) {
    case 'active':
      return user.isActive
    case 'blocked':
      return !user.isActive
    case 'unverified':
      return !user.isEmailVerified
    case 'all':
      return true
  }
}

/** Сегментированный фильтр: одна группа кнопок с `aria-pressed`. */
function SegmentedFilter<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: T
  options: readonly { value: T; label: string }[]
  onChange: (value: T) => void
}) {
  const { t } = useI18n()

  return (
    <div
      role="group"
      aria-label={label}
      className="flex max-w-full flex-wrap items-center gap-0.5 rounded-lg bg-muted p-0.5"
    >
      {options.map((item) => {
        const isSelected = item.value === value

        return (
          <button
            key={item.value}
            type="button"
            aria-pressed={isSelected}
            onClick={() => onChange(item.value)}
            className={cn(
              'inline-flex h-11 items-center rounded-md px-3 text-sm whitespace-nowrap transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
              isSelected
                ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                : 'text-muted-foreground hover:text-foreground'
            )}
          >
            {t(item.label)}
          </button>
        )
      })}
    </div>
  )
}

function UsersSkeleton() {
  const { t } = useI18n()

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        {t('Загружаем пользователей…')}
      </p>
      <Skeleton className="h-4 w-32" aria-hidden />
      <Skeleton className="h-96 rounded-xl" aria-hidden />
    </div>
  )
}

function UsersError({
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
        <p className="font-medium">
          {t('Не удалось загрузить пользователей')}
        </p>
        <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
          {message}
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

function UsersEmpty({
  status,
  role,
  search,
  onRefresh,
}: {
  status: AdminUserStatusFilter
  role: AdminUserRoleFilter
  search: string
  onRefresh: () => void
}) {
  const { t } = useI18n()

  if (search !== '') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Search className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{t('Ничего не найдено')}</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            {t(
              'По запросу «{search}» нет ни имени, ни email. Попробуйте другую строку.',
              { search }
            )}
          </p>
        </div>
      </div>
    )
  }

  if (role !== 'all') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Inbox className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{t('С этой ролью пусто')}</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            {t(
              'Пользователей с ролью «{role}» по текущему статусу нет. Смените фильтр или обновите список.',
              { role: t(ADMIN_USER_ROLE_LABELS[role]) }
            )}
          </p>
        </div>
      </div>
    )
  }

  if (status === 'blocked') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Users className="size-4 text-success" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{t('Заблокированных нет')}</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            {t(
              'Доступ никому не закрыт: все пользователи могут войти в сервис.'
            )}
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onRefresh}
        >
          {t('Обновить')}
        </Button>
      </div>
    )
  }

  if (status === 'unverified') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Users className="size-4 text-success" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{t('Все email подтверждены')}</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            {t(
              'Неподтверждённых адресов нет. Новые появятся здесь после регистрации.'
            )}
          </p>
        </div>
      </div>
    )
  }

  if (status === 'active') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Inbox className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{t('Активных пользователей нет')}</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            {t(
              'Похоже, все аккаунты заблокированы. Проверьте фильтр «Заблокированные».'
            )}
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Users className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">{t('Пользователей пока нет')}</p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {t(
            'Здесь появятся все зарегистрированные аккаунты. Создать первого можно кнопкой «Создать пользователя».'
          )}
        </p>
      </div>
    </div>
  )
}
