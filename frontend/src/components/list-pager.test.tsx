import { createRef } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ListPager } from '@/components/list-pager'

/**
 * Доступные имена пагинатора — обещание WCAG 2.5.3: имя начинается с видимого
 * слова, поэтому голосовое управление находит «Назад», «Дальше» и «Повторить».
 * Поведение диапазона и переходов живёт в E2E и здесь не дублируется (§15).
 */
function renderPager({
  hasPrev = true,
  hasNext = true,
  isLoading = false,
  error = null,
}: {
  hasPrev?: boolean
  hasNext?: boolean
  isLoading?: boolean
  error?: string | null
} = {}) {
  const onPrev = vi.fn()
  const onNext = vi.fn()

  render(
    <ListPager
      label="Страницы отзывов"
      rangeLabel="11–20"
      hasPrev={hasPrev}
      hasNext={hasNext}
      isLoading={isLoading}
      error={error}
      onPrev={onPrev}
      onNext={onNext}
      pageIndex={1}
      focusTargetRef={createRef<HTMLElement>()}
    />
  )

  return { onPrev, onNext }
}

describe('ListPager', () => {
  it('имя «Назад» начинается с видимого текста и содержит его', () => {
    renderPager()

    const previous = screen.getByRole('button', { name: /^Назад/ })

    expect(previous).toHaveAccessibleName('Назад — предыдущая страница')
    expect(previous).toHaveTextContent('Назад')
  })

  it('имя «Дальше» начинается с видимого текста и содержит его', () => {
    renderPager()

    const next = screen.getByRole('button', { name: /^Дальше/ })

    expect(next).toHaveAccessibleName('Дальше — следующая страница')
    expect(next).toHaveTextContent('Дальше')
  })

  it('в состоянии ошибки кнопка называется «Повторить — загрузить следующую страницу»', () => {
    renderPager({ error: 'Не удалось загрузить страницу.' })

    const retry = screen.getByRole('button', { name: /^Повторить/ })

    expect(retry).toHaveAccessibleName(
      'Повторить — загрузить следующую страницу'
    )
    expect(retry).toHaveTextContent('Повторить')
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Не удалось загрузить страницу.'
    )
  })

  it('во время загрузки помечает занятость навигации и кнопки', () => {
    renderPager({ isLoading: true })

    expect(screen.getByRole('navigation', { name: 'Страницы отзывов' })).toHaveAttribute(
      'aria-busy',
      'true'
    )
    expect(screen.getByRole('button', { name: /^Дальше/ })).toHaveAttribute(
      'aria-busy',
      'true'
    )
  })

  it('на краях списка отключает только недоступное направление', () => {
    const { rerender } = render(
      <ListPager
        label="Страницы отзывов"
        rangeLabel="1–10"
        hasPrev={false}
        hasNext
        isLoading={false}
        error={null}
        onPrev={vi.fn()}
        onNext={vi.fn()}
        pageIndex={0}
        focusTargetRef={createRef<HTMLElement>()}
      />
    )

    expect(screen.getByRole('button', { name: /^Назад/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: /^Дальше/ })).toBeEnabled()

    rerender(
      <ListPager
        label="Страницы отзывов"
        rangeLabel="21–30"
        hasPrev
        hasNext={false}
        isLoading={false}
        error={null}
        onPrev={vi.fn()}
        onNext={vi.fn()}
        pageIndex={2}
        focusTargetRef={createRef<HTMLElement>()}
      />
    )

    expect(screen.getByRole('button', { name: /^Назад/ })).toBeEnabled()
    expect(screen.getByRole('button', { name: /^Дальше/ })).toBeDisabled()
  })

  it('нажатия вызывают переходы', async () => {
    const user = userEvent.setup()
    const { onPrev, onNext } = renderPager()

    await user.click(screen.getByRole('button', { name: /^Назад/ }))
    await user.click(screen.getByRole('button', { name: /^Дальше/ }))

    expect(onPrev).toHaveBeenCalledOnce()
    expect(onNext).toHaveBeenCalledOnce()
  })
})
