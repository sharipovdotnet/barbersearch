import type { ReactNode } from 'react'
import { LogIn, TriangleAlert } from 'lucide-react'

import { LoginDialog } from '@/components/login-dialog'
import { SessionUnavailable } from '@/components/session-unavailable'
import { Button, buttonVariants } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useSession } from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import { REGISTER_PATH } from '@/lib/register-path'
import { isPlainClick, navigate, usePathname } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Вход в кабинет.
 *
 * Проверяет сессию один раз для всех разделов и монтирует экран только после
 * `authenticated`. Роли не ограничивают кабинет: барбер и администратор тоже видят
 * здесь свои данные, а рабочие зоны открываются из каркаса.
 */
export function AccountAccess({
  title = 'Войдите в личный кабинет',
  description = 'Записи, отзывы и настройки профиля доступны после входа. Вход по email и паролю.',
  children,
}: {
  title?: string
  description?: string
  children: (user: SessionUser) => ReactNode
}) {
  const { status, user } = useSession()
  const pathname = usePathname()

  if (status === 'loading') {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
        <AccountSkeleton />
      </div>
    )
  }

  if (status === 'unavailable') {
    return (
      <div className="mx-auto flex min-h-[55vh] w-full max-w-5xl items-center justify-center px-4 py-8 sm:py-10">
        <SessionUnavailable />
      </div>
    )
  }

  if (status === 'anonymous' || !user) {
    return (
      <div className="mx-auto flex min-h-[55vh] w-full max-w-5xl items-center justify-center px-4 py-8 sm:py-10">
        <SignInGate
          title={title}
          description={description}
          redirectTo={pathname}
        />
      </div>
    )
  }

  return <>{children(user)}</>
}

/** Экран до входа: объясняет, что даёт кабинет, и даёт единственное действие. */
export function SignInGate({
  title = 'Войдите, чтобы увидеть записи',
  description = 'Здесь собраны ваши визиты: время, статус и отмена. Вход по email и паролю.',
  onSignedIn,
  redirectTo = '/account',
}: {
  title?: string
  description?: string
  /** Вызывается после входа: экран «сессия истекла» повторяет запрос данных. */
  onSignedIn?: () => void
  /** Куда вернуться после входа; по умолчанию — «Мои записи». */
  redirectTo?: string
}) {
  return (
    <div className="mx-auto w-full max-w-md">
      <Card>
        <CardHeader>
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <LogIn className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <CardTitle>{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-2">
          <LoginDialog
            trigger="Войти"
            variant="default"
            size="lg"
            className="w-full"
            redirectTo={redirectTo}
            onSignedIn={onSignedIn}
          />
          <a
            href={REGISTER_PATH}
            className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }), 'w-full')}
            onClick={(event) => {
              if (isPlainClick(event)) {
                event.preventDefault()
                navigate(REGISTER_PATH)
              }
            }}
          >
            Зарегистрироваться
          </a>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              navigate('/')
            }}
          >
            Вернуться к поиску
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

/** Скелетон каркаса: рейл и заголовок не прыгают после загрузки. */
export function AccountSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Загружаем кабинет…
      </p>
      <div className="grid gap-6 lg:grid-cols-[14rem_minmax(0,1fr)]">
        <div className="space-y-4">
          <div className="flex items-center gap-3 border-b border-border pb-4">
            <Skeleton className="size-10 rounded-full" aria-hidden />
            <div className="space-y-2">
              <Skeleton className="h-4 w-32" aria-hidden />
              <Skeleton className="h-3 w-40" aria-hidden />
            </div>
          </div>
          <div className="grid grid-cols-2 gap-1 lg:grid-cols-1">
            <Skeleton className="h-14 rounded-lg lg:h-9" aria-hidden />
            <Skeleton className="h-14 rounded-lg lg:h-9" aria-hidden />
            <Skeleton className="h-14 rounded-lg lg:h-9" aria-hidden />
            <Skeleton className="h-14 rounded-lg lg:h-9" aria-hidden />
          </div>
        </div>
        <div className="space-y-5">
          <div className="space-y-3">
            <Skeleton className="h-8 w-56" aria-hidden />
            <Skeleton className="h-4 w-72" aria-hidden />
          </div>
          <Skeleton className="h-64 rounded-xl" aria-hidden />
          <div className="space-y-3">
            <Skeleton className="h-8 w-64 rounded-lg" aria-hidden />
            <Skeleton className="h-52 rounded-xl" aria-hidden />
          </div>
        </div>
      </div>
    </>
  )
}

/** Ошибка загрузки списка: текст причины и повтор, а не пустой экран. */
export function BookingsErrorState({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Не удалось загрузить записи</p>
        <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
          {message}
        </p>
      </div>
      <Button size="sm" variant="outline" onClick={onRetry}>
        Повторить
      </Button>
    </div>
  )
}
