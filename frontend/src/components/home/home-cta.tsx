import { Scissors } from 'lucide-react'

import { navigate } from '@/lib/router'

/** Нижний блок витрины: возврат в кабинет к своим записям. */
export function HomeCta() {
  return (
    <section className="border-t">
      <div className="mx-auto flex max-w-5xl flex-col items-start gap-4 px-4 py-12 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-muted">
            <Scissors className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <div>
            <h2 className="font-heading text-lg font-semibold tracking-tight">
              Уже записывались?
            </h2>
            <p className="mt-1 max-w-md text-sm text-muted-foreground">
              Все визиты, их статус и отмена собраны в кабинете — там же можно
              оставить отзыв после стрижки.
            </p>
          </div>
        </div>
        <a
          href="/account"
          className="text-sm font-medium underline-offset-4 hover:underline"
          onClick={(event) => {
            event.preventDefault()
            navigate('/account')
          }}
        >
          Перейти в мои записи
        </a>
      </div>
    </section>
  )
}
