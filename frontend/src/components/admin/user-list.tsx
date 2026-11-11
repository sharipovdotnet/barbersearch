import { useRef, type KeyboardEvent, type RefObject } from 'react'

import { ListPager } from '@/components/list-pager'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { useI18n } from '@/i18n'
import {
  ADMIN_USER_ACTIVITY_LABELS,
  ADMIN_USER_ROLE_LABELS,
  getUserDisplayName,
  type AdminUserRole,
  type AdminUserRow,
} from '@/lib/admin-users'
import { formatShortDateTime, getInitials } from '@/lib/format'
import { formatPageRange } from '@/lib/paging'
import type { PhotoModerationStatus } from '@/lib/photo-moderation'
import { cn } from '@/lib/utils'

/**
 * Список пользователей: плотные строки-кнопки с roving tabindex.
 *
 * Счётчика «всего» нет: API отдаёт только страницу, поэтому в подписи «Показано N»,
 * а не выдуманное общее число. Строка на широком экране укладывается в одну линию,
 * а при нехватке места метаданные переносятся под имя — так список читается и в узкой
 * колонке split-view, и на телефоне. Перелистывание — общий `ListPager`: диапазон
 * без выдуманного «из N».
 */
export function UserList({
  items,
  selectedId,
  offset,
  pageIndex,
  hasPrev,
  hasNext,
  isLoadingPage,
  pageError,
  focusTargetRef,
  detailPanelId,
  onSelect,
  onPrev,
  onNext,
}: {
  items: AdminUserRow[]
  selectedId: string | null
  offset: number
  pageIndex: number
  hasPrev: boolean
  hasNext: boolean
  isLoadingPage: boolean
  pageError: string | null
  focusTargetRef: RefObject<HTMLElement | null>
  /** `id` панели карточки, если она отрисована в этом вьюпорте; иначе `null`. */
  detailPanelId: string | null
  onSelect: (userId: string) => void
  onPrev: () => void
  onNext: () => void
}) {
  const rowRefs = useRef(new Map<string, HTMLButtonElement>())
  const { t } = useI18n()

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
    <section aria-label={t('Пользователи')} className="space-y-3">
      <div className="flex items-center justify-between gap-2 px-1 text-xs text-muted-foreground">
        <span>{t('Показано {count}', { count: items.length })}</span>
        <span>{t('Сначала новые')}</span>
      </div>

      <ul className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
        {items.map((user, index) => {
          const isSelected = user.id === selectedId
          const lastLoginAt =
            user.lastLoginAt === null
              ? null
              : formatShortDateTime(user.lastLoginAt)

          // Имя строки — её видимый текст: имя, email, бейджи и дата входа.
          // Собранный `aria-label` дублировал подписи и терял инициалы аватара,
          // из-за чего имя расходилось с экраном (WCAG 2.5.3).
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
                aria-controls={detailPanelId ?? undefined}
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
                      ? t('Ещё не входил')
                      : t('Вход {time}', { time: lastLoginAt })}
                  </span>
                  {!user.isEmailVerified && (
                    <span className="text-xs text-muted-foreground">
                      {t('email не подтверждён')}
                    </span>
                  )}
                </span>
              </button>
            </li>
          )
        })}
      </ul>

      <ListPager
        label={t('Страницы пользователей')}
        rangeLabel={
          items.length === 0 ? '' : formatPageRange(offset, items.length)
        }
        hasPrev={hasPrev}
        hasNext={hasNext}
        isLoading={isLoadingPage}
        error={pageError}
        onPrev={onPrev}
        onNext={onNext}
        pageIndex={pageIndex}
        focusTargetRef={focusTargetRef}
      />
    </section>
  )
}

/**
 * Аватар с инициалами, имя и email: одна и та же строка списка и карточки.
 *
 * Админ видит сырой `avatarUrl` и при скрытом фото — иначе он не смог бы решить,
 * вернуть кадр или нет. Флаг показывает текстовый бейдж: цвет не единственный сигнал.
 * Бейджи идут по приоритету: скрыто администратором → отклонено → на проверке.
 */
export function UserIdentity({
  user,
  className,
}: {
  user: {
    firstName: string
    lastName: string
    email: string
    avatarUrl: string | null
    isAvatarBlocked: boolean
    avatarModerationStatus: PhotoModerationStatus
  }
  className?: string
}) {
  const { t } = useI18n()
  const name = getUserDisplayName(user)

  return (
    <span className={cn('flex min-w-0 items-center gap-2.5', className)}>
      <Avatar size="sm">
        {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" />}
        <AvatarFallback className="text-xs">{getInitials(name)}</AvatarFallback>
      </Avatar>
      <span className="min-w-0">
        <span className="block truncate font-medium">{name}</span>
        <span className="block truncate text-xs text-muted-foreground">
          {user.email}
        </span>
        {user.isAvatarBlocked ? (
          <Badge variant="outline" className="mt-1">
            {t('Фото скрыто')}
          </Badge>
        ) : user.avatarModerationStatus === 'Rejected' ? (
          <Badge variant="destructive" className="mt-1">
            {t('Отклонено')}
          </Badge>
        ) : user.avatarModerationStatus === 'Pending' ? (
          <Badge variant="outline" className="mt-1">
            {t('На проверке')}
          </Badge>
        ) : null}
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
  const { t } = useI18n()

  return (
    <Badge variant="outline" className={className}>
      {t(ADMIN_USER_ROLE_LABELS[role])}
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
  const { t } = useI18n()

  return (
    <Badge
      variant={isActive ? 'secondary' : 'destructive'}
      className={className}
    >
      {isActive
        ? t(ADMIN_USER_ACTIVITY_LABELS.active)
        : t(ADMIN_USER_ACTIVITY_LABELS.blocked)}
    </Badge>
  )
}
