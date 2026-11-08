import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it } from 'vitest'

import { LanguageSwitcher } from '@/components/language-switcher'
import { I18nProvider, LOCALE_STORAGE_KEY, setCurrentLocale, useI18n } from '@/i18n'

/** Подпись на русском — видимое доказательство, что переключился весь каталог. */
function Probe() {
  const { t } = useI18n()

  return <p>{t('Мой кабинет')}</p>
}

function renderSwitcher() {
  return render(
    <I18nProvider initialLocale="ru">
      <LanguageSwitcher />
      <Probe />
    </I18nProvider>
  )
}

afterEach(() => {
  setCurrentLocale('ru')
  window.localStorage.removeItem(LOCALE_STORAGE_KEY)
  document.documentElement.lang = 'ru'
})

describe('LanguageSwitcher', () => {
  it('показывает текущий язык и переключает каталог на английский', async () => {
    const user = userEvent.setup()
    renderSwitcher()

    expect(screen.getByText('Мой кабинет')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Язык интерфейса' })).toBeInTheDocument()

    const english = screen.getByRole('button', { name: /English/ })
    const russian = screen.getByRole('button', { name: /Русский/ })

    expect(russian).toHaveAttribute('aria-pressed', 'true')
    expect(english).toHaveAttribute('aria-pressed', 'false')

    await user.click(english)

    expect(screen.getByText('My account')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Interface language' })).toBeInTheDocument()
    expect(english).toHaveAttribute('aria-pressed', 'true')
    expect(document.documentElement.lang).toBe('en')
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('en')
  })

  it('возвращает русский и сохраняет выбор', async () => {
    const user = userEvent.setup()
    renderSwitcher()

    await user.click(screen.getByRole('button', { name: /English/ }))
    await user.click(screen.getByRole('button', { name: /Русский/ }))

    expect(screen.getByText('Мой кабинет')).toBeInTheDocument()
    expect(window.localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('ru')
    expect(document.documentElement.lang).toBe('ru')
  })
})
