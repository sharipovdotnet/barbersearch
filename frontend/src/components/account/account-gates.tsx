import { LogIn, ShieldAlert, TriangleAlert } from 'lucide-react'

import { LoginDialog } from '@/components/login-dialog'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { BARBER_CABINET_PATH } from '@/lib/barber-cabinet-path'
import { navigate } from '@/lib/router'

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

/**
 * Кабинет клиента — единственная реализованная роль.
 *
 * Барберу и администратору честно говорим, что их кабинет ещё не готов, вместо
 * пустого экрана «нет записей», который читался бы как ошибка.
 */
export function RoleGate({ role }: { role: string }) {
  const isBarber = role === 'Barber'
  const isAdmin = role === 'Admin'

  return (
    <div className="mx-auto w-full max-w-md">
      <Card>
        <CardHeader>
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <ShieldAlert className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <CardTitle>
            {isBarber
              ? 'Это кабинет клиента'
              : isAdmin
                ? 'Вы администратор'
                : 'Кабинет администратора ещё не готов'}
          </CardTitle>
          <CardDescription>
            {isBarber
              ? 'Вы вошли как барбер. Это кабинет клиента: записи клиентов, подтверждение и заметки живут в кабинете барбера.'
              : isAdmin
                ? 'Модерация отзывов, барберы и справочники — в отдельном разделе администрирования.'
                : 'Управление каталогом и модерацией отзывов появится отдельным разделом.'}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap items-center gap-2">
          {isBarber && (
            <Button
              size="sm"
              className="h-11 sm:h-8"
              onClick={() => {
                navigate(BARBER_CABINET_PATH)
              }}
            >
              Открыть кабинет барбера
            </Button>
          )}
          {isAdmin && (
            <Button
              size="sm"
              onClick={() => {
                navigate('/admin')
              }}
            >
              Открыть администрирование
            </Button>
          )}
          {!isAdmin && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                navigate('/')
              }}
            >
              На главную
            </Button>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

/** Скелетон повторяет силуэт страницы, чтобы загрузка не прыгала версткой. */
export function AccountSkeleton() {
  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_19rem]">
      <div className="space-y-6">
        <div className="space-y-3">
          <Skeleton className="h-8 w-56" />
          <Skeleton className="h-4 w-72" />
        </div>
        <Skeleton className="h-64 rounded-xl" />
        <div className="space-y-3">
          <Skeleton className="h-8 w-64 rounded-lg" />
          <Skeleton className="h-52 rounded-xl" />
        </div>
      </div>
      <div className="space-y-4">
        <Skeleton className="h-48 rounded-xl" />
        <Skeleton className="h-32 rounded-xl" />
      </div>
    </div>
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
