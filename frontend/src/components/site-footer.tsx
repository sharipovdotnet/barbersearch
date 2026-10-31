/** Подвал общий для витрины и кабинета: он не несёт навигации, только контекст проекта. */
export function SiteFooter() {
  return (
    <footer className="border-t">
      <div className="mx-auto flex max-w-5xl flex-col gap-2 px-4 py-6 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
        <p>React 19 · Vite · Tailwind CSS v4 · shadcn/ui</p>
        {/* Адрес dev-прокси в production только путал бы: сборка вырезает ветку целиком. */}
        {import.meta.env.DEV && (
          <p>
            API:{' '}
            <code className="rounded bg-muted px-1 py-0.5 text-xs">/api</code> →
            localhost:5243
          </p>
        )}
      </div>
    </footer>
  )
}
