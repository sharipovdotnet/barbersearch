import type { MouseEvent } from 'react'

import { COOKIES_SECTION_ID, openCookiesSection, PRIVACY_PATH } from '@/lib/legal'
import { isPlainClick, navigate } from '@/lib/router'

/**
 * Год копирайта считается один раз при загрузке модуля: `new Date()` внутри рендера —
 * нечистая функция, и React не может её оптимизировать (`react(purity)`).
 */
const CURRENT_YEAR = new Date().getFullYear()

/**
 * Подвал витрины и кабинета.
 *
 * Несёт правовую информацию: Политика обработки персональных данных должна быть
 * доступна с любой страницы (часть 2 статьи 18.1 152-ФЗ), поэтому ссылка на неё и на
 * раздел о cookie стоит здесь, а не только в форме регистрации. Адрес dev-прокси
 * показывается лишь в разработке: в production он только путал бы.
 */
export function SiteFooter() {
  return (
    <footer className="border-t">
      <div className="mx-auto flex max-w-5xl flex-col gap-3 px-4 py-6 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
        <nav
          aria-label="Правовая информация"
          className="flex flex-wrap items-center gap-x-4 gap-y-2"
        >
          <a
            href={PRIVACY_PATH}
            className="rounded-sm underline underline-offset-4 hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            onClick={handlePolicyLink}
          >
            Политика обработки данных
          </a>
          <a
            href={`${PRIVACY_PATH}#${COOKIES_SECTION_ID}`}
            className="rounded-sm underline underline-offset-4 hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            onClick={handleCookiesLink}
          >
            Cookie
          </a>
        </nav>

        <div className="flex flex-col gap-1 text-xs sm:items-end">
          <p>© {CURRENT_YEAR} BarberSearch</p>
          {import.meta.env.DEV && (
            <p>
              API:{' '}
              <code className="rounded bg-muted px-1 py-0.5 text-xs">/api</code> →
              localhost:5243
            </p>
          )}
        </div>
      </div>
    </footer>
  )
}

/** Обычный клик ведёт внутри SPA; Ctrl/Cmd-клик оставляем браузеру. */
function handlePolicyLink(event: MouseEvent<HTMLAnchorElement>) {
  if (!isPlainClick(event)) {
    return
  }

  event.preventDefault()
  navigate(PRIVACY_PATH)
}

/** Раздел о cookie: ссылка должна прокрутить и тогда, когда Политика уже открыта. */
function handleCookiesLink(event: MouseEvent<HTMLAnchorElement>) {
  if (!isPlainClick(event)) {
    return
  }

  event.preventDefault()
  openCookiesSection()
}
