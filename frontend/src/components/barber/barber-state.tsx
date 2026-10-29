import { Search, TriangleAlert } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { goToSection, isPlainClick } from '@/lib/router'

/**
 * Состояния страницы барбера.
 *
 * Загрузка объявляется скринридером один раз, а сами скелетоны скрыты от него:
 * иначе он прочитал бы десяток пустых блоков. Ошибка — `role="alert"`,
 * «не найден» — спокойный `role="status"`: это не сбой, а скрытый профиль.
 */

export function BarberProfileSkeleton() {
  return (
    <div aria-busy="true" className="mt-6 space-y-6">
      <p role="status" className="sr-only">
        Загружаем профиль…
      </p>

      <Card aria-hidden="true">
        <CardContent className="flex items-start gap-4">
          <Skeleton className="size-16 shrink-0 rounded-full" />
          <div className="flex-1 space-y-2">
            <Skeleton className="h-7 w-2/3" />
            <Skeleton className="h-4 w-1/2" />
            <Skeleton className="h-4 w-1/3" />
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          <Skeleton className="h-48 rounded-xl" aria-hidden="true" />
          <Skeleton className="h-40 rounded-xl" aria-hidden="true" />
        </div>
        <Skeleton className="h-32 rounded-xl" aria-hidden="true" />
      </div>
    </div>
  )
}

export function BarberProfileError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  return (
    <div
      role="alert"
      className="mt-6 flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center"
    >
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <h1 className="font-heading text-lg font-semibold tracking-tight">
          Не удалось загрузить профиль барбера.
        </h1>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {message}
        </p>
      </div>
      <Button variant="outline" className="h-11 sm:h-10" onClick={onRetry}>
        Повторить
      </Button>
    </div>
  )
}

export function BarberProfileNotFound() {
  return (
    <div
      role="status"
      className="mt-6 flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center"
    >
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Search className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <h1 className="font-heading text-lg font-semibold tracking-tight">
          Барбер не найден
        </h1>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          Возможно, профиль скрыт или ссылка устарела.
        </p>
      </div>
      <a
        href="/#barbers"
        className="rounded-md text-sm font-medium underline underline-offset-4 hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        onClick={(event) => {
          if (!isPlainClick(event)) {
            return
          }

          event.preventDefault()
          goToSection('barbers')
        }}
      >
        Вернуться к поиску
      </a>
    </div>
  )
}
