import { useState } from 'react'
import { LogOut } from 'lucide-react'

import { useSession } from '@/hooks/use-session'
import { LoginDialog } from '@/components/login-dialog'
import { SessionRetryButton } from '@/components/session-unavailable'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { isAdminPath } from '@/lib/admin-path'
import { navigate, useLocation } from '@/lib/router'

/**
 * Кнопка «Войти» или профиль текущего пользователя в шапке.
 */
export function HeaderAuth() {
  const { status, user, signOut } = useSession()
  const [isSigningOut, setIsSigningOut] = useState(false)
  const location = useLocation()
  const pathname = location.split('?')[0] ?? ''
  const redirectTo = isAdminPath(pathname) ? location : undefined

  if (status === 'loading') {
    // Пока сессия проверяется, скринридер должен знать, что идёт загрузка,
    // а не «ничего нет». Статус живёт только в этом состоянии: в остальных
    // у шапки есть кнопка или ссылка с внятным именем.
    return (
      <div role="status">
        <span className="sr-only">Проверяем сессию…</span>
        <Skeleton className="h-7 w-20" aria-hidden />
      </div>
    )
  }

  if (status === 'unavailable') {
    return <SessionRetryButton />
  }

  if (status === 'anonymous' || !user) {
    return <LoginDialog redirectTo={redirectTo} />
  }

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

  return (
    <>
      <a
        href="/account"
        aria-label="Мои записи"
        title="Мои записи"
        className="rounded-full outline-none transition-opacity hover:opacity-80 focus-visible:ring-3 focus-visible:ring-ring/50"
        onClick={(event) => {
          event.preventDefault()
          navigate('/account')
        }}
      >
        <Avatar size="sm" aria-hidden>
          <AvatarFallback>{user.email.slice(0, 1).toUpperCase()}</AvatarFallback>
        </Avatar>
      </a>
      <span
        className="hidden max-w-40 truncate text-sm text-muted-foreground lg:inline"
        title={user.email}
      >
        {user.email}
      </span>
      <Button
        variant="ghost"
        size="sm"
        onClick={handleSignOut}
        disabled={isSigningOut}
        aria-label={isSigningOut ? 'Выходим…' : 'Выйти'}
      >
        <LogOut data-icon="inline-start" aria-hidden />
        <span className="hidden sm:inline">
          {isSigningOut ? 'Выходим…' : 'Выйти'}
        </span>
      </Button>
    </>
  )
}
