import { CalendarCheck, ShieldAlert, UserX } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { navigate } from '@/lib/router'

/**
 * Гейты кабинета барбера.
 *
 * Запрос списка уходит только из экрана, который монтируется после проверок:
 * клиент и администратор не должны получать `403`, а барбер без привязанного
 * профиля — `403 auth.forbidden` вместо объяснения.
 */

/** Роль не Barber: честно говорим, что экран не для этой роли. */
export function BarberRoleGate({ role }: { role: string }) {
  const isAdmin = role === 'Admin'

  return (
    <div className="mx-auto w-full max-w-md">
      <Card>
        <CardHeader>
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <ShieldAlert className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <CardTitle>
            {isAdmin ? 'Вы администратор' : 'Это кабинет барбера'}
          </CardTitle>
          <CardDescription>
            {isAdmin
              ? 'Модерация отзывов, барберы и пользователи — в отдельном разделе администрирования.'
              : 'Записи клиентов видит барбер со своим профилем. Ваши записи и отзывы — в кабинете клиента.'}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap items-center gap-2">
          {isAdmin ? (
            <Button
              size="sm"
              className="h-11 sm:h-8"
              onClick={() => {
                navigate('/admin')
              }}
            >
              Открыть администрирование
            </Button>
          ) : (
            <Button
              size="sm"
              className="h-11 sm:h-8"
              onClick={() => {
                navigate('/account')
              }}
            >
              Мои записи
            </Button>
          )}
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={() => {
              navigate('/')
            }}
          >
            На главную
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

/** Барбер без привязанного профиля: сервер закрыл бы его `403`, объясняем заранее. */
export function BarberProfileMissingGate() {
  return (
    <div className="mx-auto w-full max-w-md">
      <Card>
        <CardHeader>
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <UserX className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <CardTitle>Профиль барбера не привязан к аккаунту</CardTitle>
          <CardDescription>
            Записи клиентов приходят только для профиля, связанного с вашим
            аккаунтом. Обратитесь к администратору.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={() => {
              navigate('/')
            }}
          >
            На главную
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

/** Скелетон повторяет силуэт заголовка, вкладок и строк — загрузка не прыгает. */
export function BarberCabinetSkeleton() {
  return (
    <div className="space-y-5" aria-busy="true">
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-4 w-80" />
      </div>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-9 w-56 rounded-lg" />
        <ul className="divide-y rounded-xl border bg-card">
          {SKELETON_KEYS.map((key) => (
            <li key={key} className="space-y-2 p-4">
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-4 w-56" />
              <Skeleton className="h-4 w-full max-w-sm" />
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}

const SKELETON_KEYS = ['barber-skeleton-1', 'barber-skeleton-2', 'barber-skeleton-3']

/** Заголовок экрана: один `h1` и подпись, общие для скелетона и списка. */
export function BarberCabinetHeading() {
  return (
    <header className="flex items-center gap-3">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <CalendarCheck className="size-5 text-muted-foreground" aria-hidden />
      </div>
      <div>
        <h1 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
          Кабинет барбера
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Записи клиентов: подтверждение, завершение и заметки.
        </p>
      </div>
    </header>
  )
}
