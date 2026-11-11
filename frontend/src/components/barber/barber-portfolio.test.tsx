import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { BarberPortfolio } from '@/components/barber/barber-portfolio'
import type { PortfolioPhoto } from '@/lib/portfolio-photos'

const FIRST: PortfolioPhoto = {
  id: '9e200000-0000-4000-8000-000000000001',
  url: '/api/media/portfolio/9e100000-0000-4000-8000-000000000015/9e200000000040008000000000000001.jpg',
  thumbnailUrl: null,
  description: 'Стрижка',
  sortOrder: 0,
  isPublished: true,
  isBlocked: false,
  moderationStatus: 'Approved',
  rejectionReason: null,
}

const SECOND: PortfolioPhoto = {
  ...FIRST,
  id: '9e200000-0000-4000-8000-000000000002',
  url: '/api/media/portfolio/9e100000-0000-4000-8000-000000000015/9e200000000040008000000000000002.jpg',
  description: null,
  sortOrder: 1,
}

describe('BarberPortfolio', () => {
  it('пустое портфолио не рисует секцию вовсе', () => {
    const { container } = render(
      <BarberPortfolio photos={[]} barberName="Иван Петров" />
    )

    expect(container).toBeEmptyDOMElement()
    expect(
      screen.queryByRole('heading', { name: 'Работы' })
    ).not.toBeInTheDocument()
  })

  it('сетка: заголовок, подписи и ленивые картинки', () => {
    render(<BarberPortfolio photos={[FIRST, SECOND]} barberName="Иван Петров" />)

    expect(
      screen.getByRole('heading', { level: 2, name: 'Работы' })
    ).toBeInTheDocument()

    const first = screen.getByRole('img', { name: 'Стрижка' })
    const second = screen.getByRole('img', {
      name: 'Работа барбера Иван Петров, фото 2',
    })

    expect(first).toHaveAttribute('loading', 'eager')
    expect(first).toHaveAttribute('fetchpriority', 'high')
    expect(first).toHaveAttribute('decoding', 'async')
    expect(first).toHaveClass('aspect-square', 'w-full', 'object-cover')
    expect(second).toHaveAttribute('loading', 'lazy')
    expect(second).not.toHaveAttribute('fetchpriority')
    expect(second).toHaveAttribute('decoding', 'async')

    expect(
      screen.getByRole('button', { name: 'Открыть фото: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', {
        name: 'Открыть фото: Работа барбера Иван Петров, фото 2',
      })
    ).toBeInTheDocument()
  })

  it('лайтбокс: счётчик, границы, стрелки и Escape', async () => {
    const user = userEvent.setup()

    render(<BarberPortfolio photos={[FIRST, SECOND]} barberName="Иван Петров" />)

    const trigger = screen.getByRole('button', {
      name: 'Открыть фото: Стрижка',
    })

    await user.click(trigger)

    const dialog = screen.getByRole('dialog', { name: 'Фото барбера' })

    expect(within(dialog).getByText('Стрижка')).toBeInTheDocument()
    expect(within(dialog).getByText('1 из 2')).toBeInTheDocument()

    const back = within(dialog).getByRole('button', {
      name: 'Назад — фото 1 из 2',
    })
    const next = within(dialog).getByRole('button', {
      name: 'Дальше — фото 2 из 2',
    })

    expect(back).toBeDisabled()
    expect(next).toBeEnabled()

    fireEvent.keyDown(dialog, { key: 'ArrowRight' })

    expect(within(dialog).getByText('2 из 2')).toBeInTheDocument()
    expect(
      within(dialog).getByRole('button', { name: 'Дальше — фото 2 из 2' })
    ).toBeDisabled()

    fireEvent.keyDown(dialog, { key: 'ArrowLeft' })
    expect(within(dialog).getByText('1 из 2')).toBeInTheDocument()

    // На границе клавиша ничего не меняет.
    fireEvent.keyDown(dialog, { key: 'ArrowLeft' })
    expect(within(dialog).getByText('1 из 2')).toBeInTheDocument()

    await user.keyboard('{Escape}')

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
    // Фокус возвращается на кнопку-триггер, а не в <body>.
    await waitFor(() => {
      expect(trigger).toHaveFocus()
    })
  })

  it('кнопки «Назад»/«Дальше» листают работы', async () => {
    const user = userEvent.setup()

    render(<BarberPortfolio photos={[FIRST, SECOND]} barberName="Иван Петров" />)

    await user.click(
      screen.getByRole('button', { name: 'Открыть фото: Стрижка' })
    )

    const dialog = screen.getByRole('dialog')

    await user.click(
      within(dialog).getByRole('button', { name: 'Дальше — фото 2 из 2' })
    )

    expect(within(dialog).getByText('2 из 2')).toBeInTheDocument()
    expect(
      within(dialog).getByRole('img', {
        name: 'Работа барбера Иван Петров, фото 2',
      })
    ).toBeInTheDocument()
  })

  it('сломанная картинка остаётся muted-подложкой с alt', () => {
    render(<BarberPortfolio photos={[FIRST]} barberName="Иван Петров" />)

    fireEvent.error(screen.getByRole('img', { name: 'Стрижка' }))

    const fallback = screen.getByRole('img', { name: 'Стрижка' })

    expect(fallback.tagName).toBe('SPAN')
    expect(fallback).toHaveClass('bg-muted')
  })
})
