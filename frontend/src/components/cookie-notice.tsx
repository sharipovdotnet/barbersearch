import { useState } from 'react'

import { Button } from '@/components/ui/button'
import {
  acknowledgeCookieNotice,
  shouldShowCookieNotice,
} from '@/lib/cookie-notice'
import { COOKIES_SECTION_ID, openCookiesSection, PRIVACY_PATH } from '@/lib/legal'
import { useI18n } from '@/i18n'
import { isPlainClick } from '@/lib/router'

/**
 * Информационное уведомление об использовании cookie.
 *
 * Это не запрос согласия: Сервис ставит только строго необходимые cookie, отказаться от
 * которых нельзя, — поэтому кнопка одна («Понятно»), а не пара «Принять / Отказаться».
 *
 * Уведомление стоит в потоке под шапкой, а не поверх страницы: фиксированная полоса
 * перекрывала бы нижние кнопки (пагинатор, действия в карточках) и перехватывала бы
 * клики по ним. Полоса в потоке сдвигает контент на свою высоту один раз, не мешает
 * читать и закрывается навсегда — отметка живёт в собственной cookie.
 */
export function CookieNotice() {
  const { t } = useI18n()
  const [isVisible, setIsVisible] = useState(shouldShowCookieNotice)

  if (!isVisible) {
    return null
  }

  return (
    <section
      aria-label={t('Уведомление об использовании cookie')}
      className="border-b bg-muted/40 motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-top-2 motion-safe:duration-200"
    >
      <div className="mx-auto flex max-w-5xl flex-col gap-2 px-4 py-3 sm:flex-row sm:items-center sm:justify-between sm:gap-6">
        <p className="text-sm text-muted-foreground">
          {t('Используем только необходимые cookie для входа в аккаунт и защиты форм.')}{' '}
          <span className="hidden sm:inline">
            {t('Аналитических и рекламных cookie нет.')}{' '}
          </span>
          <a
            href={`${PRIVACY_PATH}#${COOKIES_SECTION_ID}`}
            className="rounded-sm font-medium whitespace-nowrap text-foreground underline underline-offset-4 hover:text-foreground/80 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            onClick={(event) => {
              // Ctrl/Cmd-клик не перехватываем: Политика должна открыться в новой вкладке.
              if (!isPlainClick(event)) {
                return
              }

              event.preventDefault()
              openCookiesSection()
            }}
          >
            {t('Подробнее о cookie')}
          </a>
        </p>

        <Button
          type="button"
          size="sm"
          className="h-11 w-full shrink-0 sm:h-8 sm:w-auto"
          onClick={() => {
            acknowledgeCookieNotice()
            setIsVisible(false)
          }}
        >
          {t('Понятно')}
        </Button>
      </div>
    </section>
  )
}
