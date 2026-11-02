import {
  type MouseEvent,
  type ReactNode,
} from 'react'
import {
  ArrowUpRight,
  CalendarDays,
  MailWarning,
  MessageSquare,
  Scissors,
  ShieldCheck,
  UserRound,
  type LucideIcon,
} from 'lucide-react'

import {
  AccountProfileContext,
  type AccountProfileValue,
} from '@/components/account/account-profile-context'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { useMyProfile } from '@/hooks/use-my-profile'
import {
  ACCOUNT_PATH,
  ACCOUNT_PROFILE_PATH,
  ACCOUNT_REVIEWS_PATH,
  ACCOUNT_SECURITY_PATH,
  type AccountSection,
} from '@/lib/account-path'
import type { SessionUser } from '@/lib/auth'
import { BARBER_CABINET_PATH } from '@/lib/barber-cabinet-path'
import {
  ACCOUNT_ROLE_LABELS,
  profileDisplayName,
  profileInitials,
  type MyProfileRole,
} from '@/lib/my-profile'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Каркас личного кабинета.
 *
 * Слева — кто вошёл и разделы, справа — экран. Один и тот же каркас у записей,
 * отзывов, профиля и безопасности; на мобильном рейл становится сеткой 2×2, чтобы
 * все четыре раздела оставались видны без прокрутки вбок. Роль не закрывает
 * кабинет: клиент, барбер и администратор видят здесь свои данные, а рабочие зоны
 * открываются отдельной ссылкой.
 */

const SECTIONS: readonly {
  section: AccountSection
  href: string
  label: string
  icon: LucideIcon
}[] = [
  {
    section: 'bookings',
    href: ACCOUNT_PATH,
    label: 'Записи',
    icon: CalendarDays,
  },
  {
    section: 'reviews',
    href: ACCOUNT_REVIEWS_PATH,
    label: 'Отзывы',
    icon: MessageSquare,
  },
  {
    section: 'profile',
    href: ACCOUNT_PROFILE_PATH,
    label: 'Профиль',
    icon: UserRound,
  },
  {
    section: 'security',
    href: ACCOUNT_SECURITY_PATH,
    label: 'Безопасность',
    icon: ShieldCheck,
  },
]

export function AccountShell({
  user,
  section,
  title,
  description,
  children,
}: {
  user: SessionUser
  section: AccountSection
  title: string
  description: string
  children: ReactNode
}) {
  // Ключ сбрасывает состояние формы и кэш при смене аккаунта без перезагрузки.
  return (
    <AccountShellBody
      key={user.id}
      user={user}
      section={section}
      title={title}
      description={description}
    >
      {children}
    </AccountShellBody>
  )
}

function AccountShellBody({
  user,
  section,
  title,
  description,
  children,
}: {
  user: SessionUser
  section: AccountSection
  title: string
  description: string
  children: ReactNode
}) {
  const profile = useMyProfile(user.id)

  return (
    <AccountProfileContext.Provider value={profile}>
      <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
        <div className="grid gap-6 lg:grid-cols-[14rem_minmax(0,1fr)] lg:items-start">
          <aside className="min-w-0 lg:sticky lg:top-20">
            <AccountIdentity user={user} profile={profile.profile} />
            <AccountNav section={section} />
            <AccountWorkLink role={user.role} />
          </aside>

          <div className="min-w-0">
            <header className="mb-5">
              <h1 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
                {title}
              </h1>
              <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                {description}
              </p>
            </header>

            {children}
          </div>
        </div>
      </div>
    </AccountProfileContext.Provider>
  )
}

function AccountIdentity({
  user,
  profile,
}: {
  user: SessionUser
  profile: AccountProfileValue['profile']
}) {
  const role = profile?.role ?? (user.role as MyProfileRole)
  const displayName = profile ? profileDisplayName(profile) : user.email
  const email = profile?.email ?? user.email
  const initials = profile
    ? profileInitials(profile)
    : user.email.slice(0, 1).toUpperCase()
  const isEmailVerified = profile?.isEmailVerified ?? user.isEmailVerified

  return (
    <div className="flex items-center gap-3 border-b border-border pb-4">
      <Avatar size="lg" aria-hidden>
        <AvatarFallback>{initials}</AvatarFallback>
      </Avatar>

      <div className="min-w-0">
        <p className="truncate text-sm font-medium" title={displayName}>
          {displayName}
        </p>
        {displayName !== email && (
          <p className="truncate text-xs text-muted-foreground" title={email}>
            {email}
          </p>
        )}
        <p className="mt-0.5 text-xs text-muted-foreground">
          {ACCOUNT_ROLE_LABELS[role] ?? role}
        </p>

        {!isEmailVerified && (
          <p className="mt-1.5 flex items-center gap-1 text-xs text-warning">
            <MailWarning className="size-3 shrink-0" aria-hidden />
            Email не подтверждён
          </p>
        )}
      </div>
    </div>
  )
}

function AccountNav({ section }: { section: AccountSection }) {
  return (
    <nav aria-label="Разделы кабинета" className="mt-4">
      <ul className="grid grid-cols-2 gap-1 lg:grid-cols-1">
        {SECTIONS.map((item) => {
          const Icon = item.icon
          const isCurrent = item.section === section

          return (
            <li key={item.section}>
              <a
                href={item.href}
                aria-current={isCurrent ? 'page' : undefined}
                className={cn(
                  'flex h-14 flex-col items-center justify-center gap-1 rounded-lg px-2 text-xs transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 lg:h-9 lg:flex-row lg:justify-start lg:gap-2 lg:px-3 lg:text-sm',
                  isCurrent
                    ? 'bg-muted font-medium text-foreground'
                    : 'text-muted-foreground hover:text-foreground'
                )}
                onClick={(event: MouseEvent<HTMLAnchorElement>) => {
                  if (isPlainClick(event)) {
                    event.preventDefault()
                    navigate(item.href)
                  }
                }}
              >
                <Icon className="size-4 shrink-0" aria-hidden />
                {item.label}
              </a>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}

function AccountWorkLink({ role }: { role: string }) {
  if (role !== 'Barber' && role !== 'Admin') {
    return null
  }

  const isBarber = role === 'Barber'
  const href = isBarber ? BARBER_CABINET_PATH : '/admin'
  const label = isBarber ? 'Кабинет барбера' : 'Администрирование'
  const Icon = isBarber ? Scissors : ShieldCheck

  return (
    <div className="mt-4 border-t border-border pt-4">
      <a
        href={href}
        className="flex h-11 items-center gap-2 rounded-lg px-3 text-sm text-muted-foreground transition-colors outline-none hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 lg:h-9"
        onClick={(event: MouseEvent<HTMLAnchorElement>) => {
          if (isPlainClick(event)) {
            event.preventDefault()
            navigate(href)
          }
        }}
      >
        <Icon className="size-4 shrink-0" aria-hidden />
        {label}
        <ArrowUpRight className="ml-auto size-3.5 shrink-0" aria-hidden />
      </a>
    </div>
  )
}
