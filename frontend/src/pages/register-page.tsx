import { useEffect } from 'react'
import { CircleCheck } from 'lucide-react'

import { RegisterForm } from '@/components/register-form'
import { LoginDialog } from '@/components/login-dialog'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useSession } from '@/hooks/use-session'
import { ACCOUNT_PATH } from '@/lib/account-path'
import { BARBER_CABINET_PATH } from '@/lib/barber-cabinet-path'
import { navigate } from '@/lib/router'

/**
 * Самостоятельная регистрация клиента.
 *
 * Поток, а не раздел: в шапке ссылки нет, вход — из диалога входа и гейтов. Форма
 * монтируется только после проверки сессии, поэтому `/register` не делает запросов
 * данных; вошедшему пользователю показывается гейт, а не форма.
 */
export function RegisterPage() {
  const { status, user } = useSession()

  useEffect(() => {
    document.title = 'Регистрация — BarberSearch'
  }, [])

  return (
    <div className="mx-auto w-full max-w-md px-4 py-8 sm:py-10">
      {status === 'loading' && <RegisterSkeleton />}

      {status === 'anonymous' && (
        <div className="flex flex-col gap-4">
          <Card>
            <CardHeader>
              <h1 className="font-heading text-2xl font-semibold tracking-tight">
                Регистрация
              </h1>
              <CardDescription>
                Создайте аккаунт, чтобы записываться к барберам.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <RegisterForm />
            </CardContent>
          </Card>

          <div className="flex flex-wrap items-center justify-center gap-1 text-sm text-muted-foreground">
            <span>Уже есть аккаунт?</span>
            <LoginDialog
              trigger="Войти"
              variant="ghost"
              size="sm"
              className="h-11 px-2 sm:h-8"
              redirectTo="/account"
            />
          </div>
        </div>
      )}

      {status === 'authenticated' && user && (
        <AlreadySignedInGate role={user.role} />
      )}
    </div>
  )
}

/**
 * Вошедшему регистрация не нужна: второй аккаунт создавать нечего, честнее увести
 * в его раздел, чем показывать форму, которая всё равно создаст чужой аккаунт.
 */
function AlreadySignedInGate({ role }: { role: string }) {
  const action =
    role === 'Barber'
      ? { label: 'Кабинет барбера', path: BARBER_CABINET_PATH }
      : role === 'Admin'
        ? { label: 'Администрирование', path: '/admin' }
        : { label: 'Мои записи', path: ACCOUNT_PATH }

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <CircleCheck className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <h1 className="font-heading text-2xl font-semibold tracking-tight">
          Вы уже вошли
        </h1>
        <CardDescription>
          Регистрация нужна только тем, у кого нет аккаунта. Продолжите в своём
          разделе — создавать второй аккаунт не нужно.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Button
          className="h-11 sm:h-8"
          onClick={() => {
            navigate(action.path)
          }}
        >
          {action.label}
        </Button>
      </CardContent>
    </Card>
  )
}

/** Скелетон повторяет силуэт карточки, чтобы загрузка не прыгала вёрсткой. */
function RegisterSkeleton() {
  return (
    <div role="status" aria-live="polite">
      <span className="sr-only">Загружаем страницу…</span>
      <Card className="gap-6" aria-hidden>
        <CardContent className="space-y-2">
          <Skeleton className="h-8 w-40" />
          <Skeleton className="h-4 w-full" />
        </CardContent>
        <CardContent className="space-y-4">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-11 w-full" />
        </CardContent>
      </Card>
    </div>
  )
}
