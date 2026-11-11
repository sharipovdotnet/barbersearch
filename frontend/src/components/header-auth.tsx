import { useState, type MouseEvent } from 'react'
import { LogOut, Scissors, ShieldCheck, UserRound } from 'lucide-react'

import { useMyProfile } from '@/hooks/use-my-profile'
import { useSession } from '@/hooks/use-session'
import { useI18n } from '@/i18n'
import { LoginDialog } from '@/components/login-dialog'
import { SessionRetryButton } from '@/components/session-unavailable'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { isAccountPath } from '@/lib/account-path'
import { isAdminPath } from '@/lib/admin-path'
import { BARBER_CABINET_PATH, isBarberCabinetPath } from '@/lib/barber-cabinet-path'
import { profileInitials } from '@/lib/my-profile'
import { canRenderAvatarPreview } from '@/lib/photo-moderation'
import { navigate, useLocation } from '@/lib/router'

/**
 * Кнопка «Войти» или меню аккаунта в шапке.
 *
 * Личные разделы и выход живут в меню за аватаром, а не отдельными ссылками в полосе:
 * так шапка не зависит от длины адреса и количества ролей и остаётся в одну строку
 * на любой ширине. Пункты-ссылки остаются настоящими `<a href>`, поэтому работают
 * средняя кнопка и открытие в новой вкладке.
 */
export function HeaderAuth() {
  const { t } = useI18n()
  const { status, user, signOut } = useSession()
  // Профиль приходит из общего провайдера: после `setAvatar` в кабинете шапка
  // обновляется без перезагрузки, второго запроса из шапки нет.
  const { profile } = useMyProfile()
  const [isSigningOut, setIsSigningOut] = useState(false)
  const location = useLocation()
  const pathname = location.split('?')[0] ?? ''
  const redirectTo = isAdminPath(pathname)
    ? location
    : isAccountPath(pathname)
      ? location
      : undefined

  if (status === 'loading') {
    // Пока сессия проверяется, скринридер должен знать, что идёт загрузка,
    // а не «ничего нет». Статус живёт только в этом состоянии: в остальных
    // у шапки есть кнопка или ссылка с внятным именем.
    return (
      <div role="status">
        <span className="sr-only">{t('Проверяем сессию…')}</span>
        <Skeleton className="size-7 rounded-full max-sm:size-11" aria-hidden />
      </div>
    )
  }

  if (status === 'unavailable') {
    return <SessionRetryButton />
  }

  if (status === 'anonymous' || !user) {
    return <LoginDialog redirectTo={redirectTo} />
  }

  const isAccount = isAccountPath(pathname)
  const isBarberCabinet = isBarberCabinetPath(pathname)
  const isAdmin = isAdminPath(pathname)
  // До загрузки профиля — первая буква email, как было; после — инициалы имени.
  const initials = profile
    ? profileInitials(profile)
    : user.email.slice(0, 1).toUpperCase()

  async function handleSignOut() {
    setIsSigningOut(true)

    try {
      await signOut()
    } catch {
      // Сессия на сервере могла истечь раньше: из интерфейса выходим в любом случае.
    } finally {
      setIsSigningOut(false)
    }
  }

  /** Обычный клик ведёт внутри SPA, а Ctrl/⌘-клик оставляем браузеру. */
  function handleNavigate(event: MouseEvent<Element>, href: string) {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return
    }

    event.preventDefault()
    navigate(href)
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button
            variant="ghost"
            size="icon"
            className="rounded-full max-sm:size-11"
            aria-label={t('Меню аккаунта')}
            title={t('Меню аккаунта')}
          />
        }
      >
        <Avatar size="sm" aria-hidden>
          {/* Кадр рендерится только в статусах `Pending`/`Approved`: отклонённый
              аватар в шапке заменяют инициалы, заблокированный не виден даже
              владельцу (сервер отдаёт сырой URL, но показывать его нельзя). */}
          {profile && canRenderAvatarPreview(profile) && profile.avatarUrl !== null && (
            <AvatarImage src={profile.avatarUrl} alt="" />
          )}
          {/* Инициалы рисуются `::before`, а не текстовым узлом: иначе у кнопки с
              именем «Меню аккаунта» появляется видимый текст, которого нет в имени,
              и axe валит каждую страницу по `label-content-name-mismatch`. */}
          <AvatarFallback
            data-initials={initials}
            className="before:content-[attr(data-initials)]"
          />
        </Avatar>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="max-w-56 truncate" title={user.email}>
            {user.email}
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          nativeButton={false}
          aria-current={isAccount ? 'page' : undefined}
          render={<a href="/account" />}
          onClick={(event) => handleNavigate(event, '/account')}
        >
          <UserRound aria-hidden />
          {t('Мой кабинет')}
        </DropdownMenuItem>
        {user.role === 'Barber' && (
          <DropdownMenuItem
            nativeButton={false}
            aria-current={isBarberCabinet ? 'page' : undefined}
            render={<a href={BARBER_CABINET_PATH} />}
            onClick={(event) => handleNavigate(event, BARBER_CABINET_PATH)}
          >
            <Scissors aria-hidden />
            {t('Кабинет барбера')}
          </DropdownMenuItem>
        )}
        {user.role === 'Admin' && (
          <DropdownMenuItem
            nativeButton={false}
            aria-current={isAdmin ? 'page' : undefined}
            render={<a href="/admin" />}
            onClick={(event) => handleNavigate(event, '/admin')}
          >
            <ShieldCheck aria-hidden />
            {t('Администрирование')}
          </DropdownMenuItem>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuItem
          disabled={isSigningOut}
          closeOnClick={false}
          onClick={handleSignOut}
        >
          <LogOut aria-hidden />
          {isSigningOut ? t('Выходим…') : t('Выйти')}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
