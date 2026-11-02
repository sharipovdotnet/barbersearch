import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { SiteFooter } from '@/components/site-footer'

/**
 * Подвал несёт правовые ссылки: Политика должна открываться с любой страницы, а раздел
 * о cookie — прокручиваться, даже если Политика уже открыта.
 */
describe('SiteFooter', () => {
  it('ссылается на Политику и на её раздел о cookie', () => {
    render(<SiteFooter />)

    expect(
      screen.getByRole('navigation', { name: 'Правовая информация' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Политика обработки данных' })
    ).toHaveAttribute('href', '/privacy')
    expect(screen.getByRole('link', { name: 'Cookie' })).toHaveAttribute(
      'href',
      '/privacy#cookies'
    )
  })

  it('ведёт в раздел о cookie по адресу /privacy#cookies', async () => {
    const user = userEvent.setup()

    render(<SiteFooter />)
    await user.click(screen.getByRole('link', { name: 'Cookie' }))

    expect(window.location.pathname).toBe('/privacy')
    expect(window.location.hash).toBe('#cookies')
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
