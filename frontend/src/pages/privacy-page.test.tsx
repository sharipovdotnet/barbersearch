import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { LEGAL_ENTITY } from '@/lib/legal'
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

  it('объясняет cookie, не публикуя технический перечень', () => {
    render(<PrivacyPage />)

    const cookies = screen.getByRole('region', {
      name: /Cookie и аналогичные технологии/,
    })

    expect(
      within(cookies).getByText(/только строго необходимые cookie/)
    ).toBeInTheDocument()
    expect(
      within(cookies).queryByText(/barbersearch\.auth/)
    ).not.toBeInTheDocument()
  })

  it('указывает реквизиты Оператора и контакт для обращений', () => {
    render(<PrivacyPage />)

    const operator = screen.getByRole('region', {
      name: /Сведения об Операторе/,
    })

    expect(within(operator).getByText(LEGAL_ENTITY.inn)).toBeInTheDocument()
    expect(within(operator).getByText(LEGAL_ENTITY.email)).toBeInTheDocument()
  })

  it('описывает запись согласия: редакция, дата и время UTC, IP-адрес', () => {
    render(<PrivacyPage />)

    const grounds = screen.getByRole('region', {
      name: /Правовые основания обработки/,
    })

    expect(
      within(grounds).getByText(/фиксирует редакцию Политики/)
    ).toBeInTheDocument()
    expect(
      within(grounds).getByText(/дату и время согласия \(UTC\)/)
    ).toBeInTheDocument()
    expect(within(grounds).getByText(/IP-адрес/)).toBeInTheDocument()
  })

  it('говорит, что запись согласия хранится вместе с учётной записью', () => {
    render(<PrivacyPage />)

    const purposes = screen.getByRole('region', {
      name: /Цели и сроки обработки/,
    })

    expect(
      within(purposes).getByText(/хранится вместе с учётной записью/)
    ).toBeInTheDocument()
  })

  it('называет фотографию профиля в составе данных (§3)', () => {
    render(<PrivacyPage />)

    const data = screen.getByRole('region', {
      name: /Состав обрабатываемых данных/,
    })

    expect(
      within(data).getByText(/фотография профиля \(аватар\)/)
    ).toBeInTheDocument()
    expect(
      within(data).getByText(/публикуется в открытой карточке/)
    ).toBeInTheDocument()
  })

  it('держит аватар до удаления пользователем или блокировки аккаунта (§4)', () => {
    render(<PrivacyPage />)

    const purposes = screen.getByRole('region', {
      name: /Цели и сроки обработки/,
    })

    expect(
      within(purposes).getByText(
        /до удаления пользователем или блокировки аккаунта/
      )
    ).toBeInTheDocument()
  })
})
