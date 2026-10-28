import { useRef, type KeyboardEvent } from 'react'
import { Loader2 } from 'lucide-react'

import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  ADMIN_USER_ACTIVITY_LABELS,
  ADMIN_USER_ROLE_LABELS,
  getUserDisplayName,
  type AdminUserRole,
  type AdminUserRow,
} from '@/lib/admin-users'
import { formatShortDateTime, getInitials } from '@/lib/format'
import { cn } from '@/lib/utils'

/**
 * Список пользователей: плотные строки-кнопки с roving tabindex.
 *
 * Счётчика «всего» нет: API отдаёт только страницу, поэтому в подписи «Показано N»,
 * а не выдуманное общее число. Строка на широком экране укладывается в одну линию,
 * а при нехватке места метаданные переносятся под имя — так список читается и в узкой
 * колонке split-view, и на телефоне.
 */
export function UserList({
  items,
  selectedId,
  nextCursor,
  isLoadingMore,
  loadMoreError,
  onSelect,
  onLoadMore,
}: {
  items: AdminUserRow[]
  selectedId: string | null
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  onSelect: (userId: string) => void
  onLoadMore: () => void
}) {
  const rowRefs = useRef(new Map<string, HTMLButtonElement>())

  const selectedIndex = items.findIndex((user) => user.id === selectedId)
  const tabbableIndex = selectedIndex >= 0 ? selectedIndex : 0

  function handleKeyDown(
    event: KeyboardEvent<HTMLButtonElement>,
    index: number
  ) {
    if (
      event.key !== 'ArrowDown' &&
      event.key !== 'ArrowUp' &&
      event.key !== 'Home' &&
      event.key !== 'End'
    ) {
      return
    }

    event.preventDefault()

    let nextIndex = index

    if (event.key === 'ArrowDown') {
      nextIndex = Math.min(items.length - 1, index + 1)
    } else if (event.key === 'ArrowUp') {
      nextIndex = Math.max(0, index - 1)
    } else if (event.key === 'Home') {
      nextIndex = 0
    } else {
      nextIndex = items.length - 1
    }

    const target = items[nextIndex]

    if (!target || target.id === selectedId) {
      return
    }

    onSelect(target.id)

    // Фокус переезжает вместе с выбором: стрелки ходят по строкам, Tab — нет.
    window.requestAnimationFrame(() => {
      rowRefs.current.get(target.id)?.focus()
    })
  }

  return (
    <section aria-label="Пользователи" className="space-y-3">
      <div className="flex items-center justify-between gap-2 px-1 text-xs text-muted-foreground">
        <span>Показано {items.length}</span>
        <span>Сначала новые</span>
      </div>

      <ul className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
        {items.map((user, index) => {
          const isSelected = user.id === selectedId
          const name = getUserDisplayName(user)
          const lastLoginAt =
            user.lastLoginAt === null
              ? null
              : formatShortDateTime(user.lastLoginAt)

          return (
            <li key={user.id}>
              <button
                ref={(element) => {
                  if (element) {
                    rowRefs.current.set(user.id, element)
                  } else {
                    rowRefs.current.delete(user.id)
                  }
                }}
                type="button"
                aria-current={isSelected ? 'true' : undefined}
                aria-controls="user-detail"
                aria-label={[
                  name,
                  user.email,
                  ADMIN_USER_ROLE_LABELS[user.role],
                  user.isActive
                    ? ADMIN_USER_ACTIVITY_LABELS.active
                    : ADMIN_USER_ACTIVITY_LABELS.blocked,
                  lastLoginAt === null
                    ? 'ещё не входил'
                    : `последний вход ${lastLoginAt}`,
                  user.isEmailVerified ? null : 'email не подтверждён',
                ]
                  .filter(Boolean)
                  .join(', ')}
                tabIndex={index === tabbableIndex ? 0 : -1}
                onClick={() => onSelect(user.id)}
                onKeyDown={(event) => handleKeyDown(event, index)}
                className={cn(
                  'flex min-h-16 w-full flex-wrap items-center gap-x-3 gap-y-1.5 border-l-2 px-3 py-2.5 text-left transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50',
                  isSelected
                    ? 'border-foreground bg-muted/50'
                    : 'border-transparent hover:bg-muted/30'
                )}
              >
                <UserIdentity
                  user={user}
                  className="min-w-0 flex-1 basis-44"
                />

                <span className="flex flex-wrap items-center gap-x-2 gap-y-1 sm:ml-auto">
                  <UserRoleBadge role={user.role} />
                  <UserStatusBadge isActive={user.isActive} />
                  <span className="text-xs whitespace-nowrap text-muted-foreground tabular-nums">
                    {lastLoginAt === null
                      ? 'Ещё не входил'
                      : `Вход ${lastLoginAt}`}
                  </span>
                  {!user.isEmailVerified && (
                    <span className="text-xs text-muted-foreground">
                      email не подтверждён
                    </span>
                  )}
                </span>
              </button>
            </li>
          )
        })}
      </ul>

      <div className="flex flex-col items-center gap-2 pt-1">
        {loadMoreError && (
          <p role="alert" className="text-sm text-destructive">
            {loadMoreError}
          </p>
        )}

        {nextCursor && (
          <Button
            variant="outline"
            size="sm"
            className="h-11 w-full sm:h-7"
            disabled={isLoadingMore}
            onClick={onLoadMore}
          >
            {isLoadingMore ? (
              <>
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
                Загружаем…
              </>
            ) : (
              'Показать ещё'
            )}
          </Button>
        )}
      </div>
    </section>
  )
}

/** Аватар с инициалами, имя и email: одна и та же строка списка и карточки. */
export function UserIdentity({
  user,
  className,
}: {
  user: {
    firstName: string
    lastName: string
    email: string
  }
  className?: string
}) {
  const name = getUserDisplayName(user)

  return (
    <span className={cn('flex min-w-0 items-center gap-2.5', className)}>
      <Avatar size="sm">
        <AvatarFallback className="text-xs">{getInitials(name)}</AvatarFallback>
      </Avatar>
      <span className="min-w-0">
        <span className="block truncate font-medium">{name}</span>
        <span className="block truncate text-xs text-muted-foreground">
          {user.email}
        </span>
      </span>
    </span>
  )
}

/** Бейдж роли: текст важнее цвета, поэтому все роли нейтральные. */
export function UserRoleBadge({
  role,
  className,
}: {
  role: AdminUserRole
  className?: string
}) {
  return (
    <Badge variant="outline" className={className}>
      {ADMIN_USER_ROLE_LABELS[role]}
    </Badge>
  )
}

/** Бейдж статуса: текст «Активен» / «Заблокирован», цвет — только поддержка. */
export function UserStatusBadge({
  isActive,
  className,
}: {
  isActive: boolean
  className?: string
}) {
  return (
    <Badge
      variant={isActive ? 'secondary' : 'destructive'}
      className={className}
    >
      {isActive
        ? ADMIN_USER_ACTIVITY_LABELS.active
        : ADMIN_USER_ACTIVITY_LABELS.blocked}
    </Badge>
  )
}
