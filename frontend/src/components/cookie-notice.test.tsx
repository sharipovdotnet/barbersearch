import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { CookieNotice } from '@/components/cookie-notice'
import { acknowledgeCookieNotice } from '@/lib/cookie-notice'

/** Плашка живёт вне страницы, поэтому тесту достаточно её одной. */
describe('CookieNotice', () => {
  it('сообщает, что cookie только необходимые, и ссылается на раздел Политики', () => {
    render(<CookieNotice />)

    expect(
      screen.getByRole('complementary', {
        name: 'Уведомление об использовании cookie',
      })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Подробнее о cookie' })
    ).toHaveAttribute('href', '/privacy#cookies')
    expect(
      screen.getByText(/Аналитических и рекламных cookie нет/)
    ).toBeInTheDocument()
  })

  it('закрывается кнопкой «Понятно» и запоминает это в cookie', async () => {
    const user = userEvent.setup()
    render(<CookieNotice />)

    await user.click(screen.getByRole('button', { name: 'Понятно' }))

    expect(
      screen.queryByRole('complementary', {
        name: 'Уведомление об использовании cookie',
      })
    ).not.toBeInTheDocument()
    expect(document.cookie).toContain('barbersearch.cookie-notice=1')
  })

  it('не показывается, если отметка уже стоит', () => {
    acknowledgeCookieNotice()

    render(<CookieNotice />)

    expect(
      screen.queryByRole('button', { name: 'Понятно' })
    ).not.toBeInTheDocument()
  })
})
