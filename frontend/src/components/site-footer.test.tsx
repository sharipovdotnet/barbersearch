import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { SiteFooter } from '@/components/site-footer'

/**
 * Подвал несёт правовую ссылку: Политика должна открываться с любой страницы.
 */
describe('SiteFooter', () => {
  it('ссылается на Политику', () => {
    render(<SiteFooter />)

    expect(
      screen.getByRole('navigation', { name: 'Правовая информация' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Политика обработки данных' })
    ).toHaveAttribute('href', '/privacy')
  })

  it('ведёт в Политику без якоря', async () => {
    const user = userEvent.setup()

    render(<SiteFooter />)
    await user.click(
      screen.getByRole('link', { name: 'Политика обработки данных' })
    )

    expect(window.location.pathname).toBe('/privacy')
    expect(window.location.hash).toBe('')
  })
})
