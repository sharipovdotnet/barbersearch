import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { BookingPriceDialog } from '@/components/booking-price-dialog'
import { formatPrice } from '@/lib/format'

/**
 * Диалог правки цены визита — общий для двух кабинетов.
 *
 * Запрос выполняет родитель (`onSubmit`), поэтому диалог отвечает только за ввод:
 * границы `numeric(10,2)`, недоступность сохранения при неизменной сумме и
 * `role="alert"` для серверной ошибки.
 */

function renderDialog(
  props: Partial<{
    currentPrice: number
    isPending: boolean
    error: string | null
    onSubmit: (price: number) => void
    onClose: () => void
  }> = {}
) {
  return render(
    <BookingPriceDialog
      description="6 нояб. в 12:00 · Анна Петрова"
      currentPrice={props.currentPrice ?? 1500}
      isPending={props.isPending ?? false}
      error={props.error ?? null}
      onSubmit={props.onSubmit ?? vi.fn()}
      onClose={props.onClose ?? vi.fn()}
    />
  )
}

const input = () => screen.getByLabelText('Цена, ₽')
const save = () => screen.getByRole('button', { name: 'Сохранить' })

describe('BookingPriceDialog', () => {
  it('показывает контекст визита и текущую сумму', () => {
    renderDialog()

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { name: 'Изменить цену визита' })
    ).toBeInTheDocument()
    expect(screen.getByText('6 нояб. в 12:00 · Анна Петрова')).toBeInTheDocument()
    // Сумма форматируется `Intl` с неразрывными пробелами, поэтому сравнивается
    // точный `textContent`, а не нормализованный матчер.
    expect(screen.getByText(/^Сейчас:/).textContent).toBe(
      `Сейчас: ${formatPrice(1500)}.`
    )
    expect(
      screen.getByText('Клиент увидит новую цену в своих записях.')
    ).toBeInTheDocument()
  })

  it('не даёт сохранить неизменную сумму и возвращает фокус в поле', async () => {
    renderDialog()

    expect(save()).toBeDisabled()

    await waitFor(() => expect(input()).toHaveFocus())
  })

  it('отправляет новое значение числом', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()

    renderDialog({ onSubmit })

    await user.clear(input())
    await user.type(input(), '2000')

    expect(save()).toBeEnabled()

    await user.click(save())

    expect(onSubmit).toHaveBeenCalledWith(2000)
  })

  it('принимает Enter в поле как сохранение', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()

    renderDialog({ onSubmit })

    await user.clear(input())
    await user.type(input(), '2000{Enter}')

    expect(onSubmit).toHaveBeenCalledWith(2000)
  })

  it.each([
    ['-1'],
    ['1500.999'],
    ['100000000'],
    [''],
  ])('не отправляет невалидное значение «%s»', async (value) => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()

    renderDialog({ onSubmit })

    await user.clear(input())

    if (value !== '') {
      await user.type(input(), value)
    }

    expect(save()).toBeDisabled()

    await user.click(save())

    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('показывает серверную ошибку в role=alert', () => {
    renderDialog({ error: 'Цена — не больше двух знаков после запятой.' })

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Цена — не больше двух знаков после запятой.'
    )
  })

  it('Escape закрывает диалог, пока запрос не в полёте', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()

    renderDialog({ onClose })

    await user.keyboard('{Escape}')

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('пока запрос в полёте, поля и кнопки недоступны, Escape не закрывает', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()

    renderDialog({ isPending: true, onClose })

    expect(input()).toBeDisabled()
    expect(save()).toBeDisabled()
    expect(save()).toHaveAttribute('aria-busy', 'true')

    await user.keyboard('{Escape}')

    expect(onClose).not.toHaveBeenCalled()
  })
})
