import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Input } from '@/components/ui/input'
import { setCurrentLocale } from '@/i18n'

/**
 * Нативная подсказка `input[type=date]` зависит от локали браузера, а не от языка
 * интерфейса. Поэтому пустое поле показывает свою подсказку, пока не получило фокус.
 */
describe('Input type=date', () => {
  it('показывает русскую подсказку пустого поля', () => {
    setCurrentLocale('ru')

    render(<Input type="date" aria-label="Дата" value="" onChange={() => {}} />)

    expect(screen.getByText('дд.мм.гггг')).toBeInTheDocument()
  })

  it('переводит подсказку на английский', () => {
    setCurrentLocale('en')

    render(<Input type="date" aria-label="Date" value="" onChange={() => {}} />)

    expect(screen.getByText('DD.MM.YYYY')).toBeInTheDocument()
  })

  it('убирает подсказку у заполненного поля и на фокусе', () => {
    setCurrentLocale('ru')

    const { rerender } = render(
      <Input type="date" aria-label="Дата" value="" onChange={() => {}} />
    )
    const input = screen.getByLabelText('Дата')

    fireEvent.focus(input)
    expect(screen.queryByText('дд.мм.гггг')).not.toBeInTheDocument()

    fireEvent.blur(input)
    rerender(
      <Input
        type="date"
        aria-label="Дата"
        value="2026-11-09"
        onChange={() => {}}
      />
    )
    expect(screen.queryByText('дд.мм.гггг')).not.toBeInTheDocument()
  })
})
