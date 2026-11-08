import type { MouseEvent } from 'react'
import { Scissors } from 'lucide-react'

import { HeaderAuth } from '@/components/header-auth'
import { LanguageSwitcher } from '@/components/language-switcher'
import { buttonVariants } from '@/components/ui/button'
import { useI18n } from '@/i18n'
import { isAccountPath } from '@/lib/account-path'
import { isAdminPath } from '@/lib/admin-path'
import { isBarberCabinetPath } from '@/lib/barber-cabinet-path'
import { cn } from '@/lib/utils'
import { goToSection, navigate, usePathname } from '@/lib/router'

/**
 * Общая шапка витрины и кабинета.
 *
 * Секции витрины живут на `/`, поэтому ссылки ведут на `/#barbers`: с кабинета это
 * сначала переход на витрину, потом прокрутка к якорю.
 *
 * В полосе только две ссылки витрины и одна кнопка действия: личные разделы
 * (кабинет, кабинет барбера, администрирование) и выход живут в меню аккаунта
 * (`header-auth`), иначе шапка перестаёт помещаться в `max-w-5xl` и подписи
 * рвутся на две строки.
 */
const navLinkClass =
  'whitespace-nowrap rounded-md px-2.5 py-1.5 transition-colors hover:bg-muted hover:text-foreground'

export function SiteHeader() {
  const { t } = useI18n()
  const pathname = usePathname()
  const isAccount = isAccountPath(pathname)
  const isBarberCabinet = isBarberCabinetPath(pathname)
  const isAdmin = isAdminPath(pathname)

  function handleSection(event: MouseEvent, id: string) {
    event.preventDefault()
    goToSection(id)
  }

  return (
    <header className="sticky top-0 z-30 border-b bg-background/85 backdrop-blur">
      <div
        className={cn(
          'mx-auto flex h-14 items-center gap-2 px-4 sm:gap-6',
          isAdmin ? 'max-w-6xl' : 'max-w-5xl'
        )}
      >
        <a
          href="/"
          className="flex shrink-0 items-center gap-2 font-heading text-base font-semibold tracking-tight"
          onClick={(event) => {
            event.preventDefault()
            navigate('/')
          }}
        >
          <Scissors className="size-4" aria-hidden />
          BarberSearch
        </a>

        {!isAdmin && (
          <nav
            aria-label={t('Основная навигация')}
            className="-ml-2.5 hidden items-center gap-0.5 text-sm text-muted-foreground sm:flex"
          >
            <a
              href="/#barbers"
              className={navLinkClass}
              onClick={(event) => handleSection(event, 'barbers')}
            >
              {t('Барберы')}
            </a>
            <a
              href="/#how"
              className={navLinkClass}
              onClick={(event) => handleSection(event, 'how')}
            >
              {t('Как это работает')}
            </a>
          </nav>
        )}

        <div className="ml-auto flex items-center gap-1.5 sm:gap-2">
          {isAdmin && (
            <a
              href="/"
              className={cn(
                buttonVariants({ variant: 'ghost', size: 'sm' }),
                'hidden sm:inline-flex'
              )}
              onClick={(event) => {
                event.preventDefault()
                navigate('/')
              }}
            >
              {t('На сайт')}
            </a>
          )}
          <LanguageSwitcher />
          <HeaderAuth />
          {!isAdmin && (
            <a
              href="/#barbers"
              className={cn(buttonVariants({ size: 'sm' }), 'hidden sm:inline-flex')}
              onClick={(event) => handleSection(event, 'barbers')}
            >
              {isAccount || isBarberCabinet
                ? t('К поиску')
                : t('Найти барбера')}
            </a>
          )}
        </div>
      </div>
    </header>
  )
}
