import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { AuditEntry } from '@/components/admin/audit-entry'
import type { AdminAuditEntry } from '@/lib/admin-audit'

/**
 * Строка журнала: автор, сущность, действие и раскрываемый diff.
 *
 * Проверяем обещания строки, а не экран: время в локальном поясе, «Система»
 * у записи без автора, «Удалённый пользователь» у удалённого аккаунта, русские
 * подписи полей и независимое раскрытие. Русский текст — это copy, и тест
 * сравнивает его целиком: копирайт может измениться (§15).
 */

const ROLE_CHANGE: AdminAuditEntry = {
  id: '9e700000-0000-4000-8000-000000000020',
  occurredAt: '2026-09-10T10:20:00+00:00',
  action: 'user.updated',
  entityType: 'user',
  entityId: '9e000000-0000-4000-8000-000000000002',
  actor: { id: '9e000000-0000-4000-8000-000000000001', email: 'e2e-admin@barbersearch.test' },
  changes: [{ field: 'role', from: 'Customer', to: 'Admin' }],
}

const ACTIVE_CHANGE: AdminAuditEntry = {
  ...ROLE_CHANGE,
  id: '9e700000-0000-4000-8000-000000000021',
  occurredAt: '2026-09-10T10:21:00+00:00',
  actor: null,
  changes: [{ field: 'isActive', from: true, to: false }],
}

function renderEntry(entry: AdminAuditEntry): void {
  render(
    <ul>
      <AuditEntry entry={entry} />
    </ul>
  )
}

describe('AuditEntry', () => {
  it('показывает время, сущность, короткий id и подпись действия', () => {
    renderEntry(ROLE_CHANGE)

    expect(screen.getByText('Пользователь')).toBeInTheDocument()
    expect(screen.getByText('9e000000')).toHaveAttribute(
      'title',
      '9e000000-0000-4000-8000-000000000002'
    )
    // Хвост `user.updated` — «Изменено», а не вся строка действия.
    expect(screen.getByText('Изменено')).toBeInTheDocument()
    expect(screen.getByText('e2e-admin@barbersearch.test')).toBeInTheDocument()

    const time = document.querySelector('time')

    expect(time).toHaveAttribute('dateTime', ROLE_CHANGE.occurredAt)
  })

  it('запись без автора показывает «Система»', () => {
    renderEntry(ACTIVE_CHANGE)

    expect(screen.getByText('Система')).toBeInTheDocument()
  })

  it('удалённый автор показывает «Удалённый пользователь»', () => {
    renderEntry({
      ...ROLE_CHANGE,
      actor: { id: '9e000000-0000-4000-8000-000000000099', email: null },
    })

    expect(screen.getByText('Удалённый пользователь')).toBeInTheDocument()
  })

  it('раскрывает diff русскими подписями и скрывает его повторным нажатием', async () => {
    const user = userEvent.setup()

    renderEntry(ROLE_CHANGE)

    const toggle = screen.getByRole('button', { name: 'Показать изменения' })
    const diffId = toggle.getAttribute('aria-controls')

    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    // Панель существует в DOM и скрыта: `aria-controls` не ведёт в пустоту.
    expect(document.getElementById(diffId ?? '')).toBeInTheDocument()
    expect(screen.getByText('Роль')).not.toBeVisible()

    await user.click(toggle)

    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('button', { name: 'Скрыть изменения' })).toBe(toggle)
    expect(screen.getByText('Роль')).toBeVisible()
    expect(screen.getByText('Клиент → Администратор')).toBeVisible()

    await user.click(toggle)

    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByText('Роль')).not.toBeVisible()
  })

  it('пустой diff честно говорит, что изменений нет', async () => {
    const user = userEvent.setup()

    renderEntry({ ...ROLE_CHANGE, changes: [] })

    await user.click(screen.getByRole('button', { name: 'Показать изменения' }))

    expect(screen.getByText('Изменения не записаны.')).toBeInTheDocument()
  })

  it('раскрытие независимое: вторая строка остаётся свёрнутой', async () => {
    const user = userEvent.setup()

    render(
      <ul>
        <AuditEntry entry={ROLE_CHANGE} />
        <AuditEntry entry={ACTIVE_CHANGE} />
      </ul>
    )

    const toggles = screen.getAllByRole('button', { name: 'Показать изменения' })

    await user.click(toggles[0])

    expect(toggles[0]).toHaveAttribute('aria-expanded', 'true')
    expect(toggles[1]).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByText('Клиент → Администратор')).toBeVisible()
    // Вторая панель существует, но скрыта: раскрытие независимое.
    expect(screen.getByText('Да → Нет')).not.toBeVisible()
  })
})
