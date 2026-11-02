import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { COOKIE_REGISTRY, LEGAL_ENTITY } from '@/lib/legal'
import { PrivacyPage } from '@/pages/privacy-page'

describe('PrivacyPage', () => {
  it('публикует заголовок, дату редакции и содержание документа', () => {
    render(<PrivacyPage />)

    expect(
      screen.getByRole('heading', {
        level: 1,
        name: 'Политика в отношении обработки персональных данных',
      })
    ).toBeInTheDocument()
    expect(screen.getByText(/Редакция от/)).toBeInTheDocument()
    expect(
      screen.getByRole('navigation', { name: 'Содержание' })
    ).toBeInTheDocument()
  })

  it('перечисляет в разделе о cookie все записи реестра', () => {
    render(<PrivacyPage />)

    const cookies = screen.getByRole('region', {
      name: /Cookie и аналогичные технологии/,
    })

    for (const cookie of COOKIE_REGISTRY) {
      expect(within(cookies).getByText(cookie.name)).toBeInTheDocument()
    }
  })

  it('указывает реквизиты Оператора и контакт для обращений', () => {
    render(<PrivacyPage />)

    const operator = screen.getByRole('region', {
      name: /Сведения об Операторе/,
    })

    expect(within(operator).getByText(LEGAL_ENTITY.inn)).toBeInTheDocument()
    expect(within(operator).getByText(LEGAL_ENTITY.email)).toBeInTheDocument()
  })
})
