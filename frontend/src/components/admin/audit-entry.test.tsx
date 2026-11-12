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

const SALON_ENTRY: AdminAuditEntry = {
  ...ROLE_CHANGE,
  id: '9e700000-0000-4000-8000-000000000022',
  occurredAt: '2026-09-10T10:16:10+00:00',
  action: 'salon.updated',
  entityType: 'salon',
  entityId: '9e400000-0000-4000-8000-000000000001',
  changes: [
    { field: 'phoneNumber', from: '+70000000009', to: '+70000000010' },
  ],
}

const BOOKING_ENTRY: AdminAuditEntry = {
  ...ROLE_CHANGE,
  id: '9e700000-0000-4000-8000-000000000025',
  occurredAt: '2026-09-10T10:16:40+00:00',
  action: 'booking.updated',
  entityType: 'booking',
  entityId: '9e900000-0000-4000-8000-000000000024',
  changes: [{ field: 'status', from: 'Pending', to: 'Cancelled' }],
}

const HAIR_SERVICE_ENTRY: AdminAuditEntry = {
  ...ROLE_CHANGE,
  id: '9e700000-0000-4000-8000-000000000023',
  occurredAt: '2026-09-10T10:16:20+00:00',
  action: 'hair_service.updated',
  entityType: 'hair_service',
  entityId: '9e500000-0000-4000-8000-000000000001',
  changes: [{ field: 'name', from: 'E2E Стрижка', to: 'E2E Стрижка 2' }],
}

const PORTFOLIO_ENTRY: AdminAuditEntry = {
  ...ROLE_CHANGE,
  id: '9e700000-0000-4000-8000-000000000026',
  occurredAt: '2026-09-10T10:17:00+00:00',
  action: 'portfolio_photo.created',
  entityType: 'portfolio_photo',
  entityId: '9e200000-0000-4000-8000-000000000001',
  changes: [
    {
      field: 'url',
      from: null,
      to: '/api/media/portfolio/9e100000-0000-4000-8000-000000000015/0123456789abcdef0123456789abcdef.jpg',
    },
    { field: 'sortOrder', from: null, to: 1 },
    { field: 'moderationStatus', from: 'Pending', to: 'Approved' },
  ],
}

const AVATAR_ENTRY: AdminAuditEntry = {
  ...ROLE_CHANGE,
  id: '9e700000-0000-4000-8000-000000000027',
  occurredAt: '2026-09-10T10:17:10+00:00',
  action: 'user.updated',
  entityType: 'user',
  entityId: '9e000000-0000-4000-8000-000000000004',
  changes: [
    {
      field: 'avatarUrl',
      from: null,
      to: '/api/media/avatars/9e000000-0000-4000-8000-000000000004/0123456789abcdef0123456789abcdef.jpg',
    },
    { field: 'avatarModerationStatus', from: 'Approved', to: 'Pending' },
  ],
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

  it('строка салона показывает «Салон», строка брони — «Бронь»', () => {
    render(
      <ul>
        <AuditEntry entry={SALON_ENTRY} />
        <AuditEntry entry={BOOKING_ENTRY} />
      </ul>
    )

    expect(screen.getByText('Салон')).toBeInTheDocument()
    expect(screen.getByText('Бронь')).toBeInTheDocument()
  })

  it('раскрытый diff брони показывает русский статус', async () => {
    const user = userEvent.setup()

    renderEntry(BOOKING_ENTRY)

    await user.click(screen.getByRole('button', { name: 'Показать изменения' }))

    expect(screen.getByText('Статус')).toBeVisible()
    expect(screen.getByText('Ждёт подтверждения → Отменена')).toBeVisible()
  })

  it('раскрытый diff услуги переводит поле «name» в «Название»', async () => {
    const user = userEvent.setup()

    renderEntry(HAIR_SERVICE_ENTRY)

    await user.click(screen.getByRole('button', { name: 'Показать изменения' }))

    expect(screen.getByText('Название')).toBeVisible()
    expect(screen.getByText('E2E Стрижка → E2E Стрижка 2')).toBeVisible()
  })

  it('строка загрузки кадра показывает «Фото портфолио» и «Создано»', () => {
    renderEntry(PORTFOLIO_ENTRY)

    expect(screen.getByText('Фото портфолио')).toBeInTheDocument()
    expect(screen.getByText('Создано')).toBeInTheDocument()
  })

  it('раскрытый diff кадра: «Файл», «Порядок» и русский статус проверки', async () => {
    const user = userEvent.setup()

    renderEntry(PORTFOLIO_ENTRY)

    await user.click(screen.getByRole('button', { name: 'Показать изменения' }))

    expect(screen.getByText('Файл')).toBeVisible()
    expect(screen.getByText('Порядок')).toBeVisible()
    expect(screen.getByText('Статус проверки')).toBeVisible()
    // Значение moderationStatus переведено по типу сущности `portfolio_photo`.
    expect(screen.getByText('На проверке → Одобрено')).toBeVisible()
  })

  it('строка аватара показывает «Фото профиля» и «Статус проверки фото»', async () => {
    const user = userEvent.setup()

    renderEntry(AVATAR_ENTRY)

    expect(screen.getByText('Пользователь')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Показать изменения' }))

    expect(screen.getByText('Фото профиля')).toBeVisible()
    expect(screen.getByText('Статус проверки фото')).toBeVisible()
    expect(screen.getByText('Одобрено → На проверке')).toBeVisible()
  })

  it('неизвестный тип и поле показываются строкой сервера', async () => {
    const user = userEvent.setup()

    renderEntry({
      ...ROLE_CHANGE,
      entityType: 'coupon',
      action: 'coupon.updated',
      changes: [{ field: 'couponCode', from: 'A', to: 'B' }],
    })

    expect(screen.getByText('coupon')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Показать изменения' }))

    expect(screen.getByText('couponCode')).toBeVisible()
    expect(screen.getByText('A → B')).toBeVisible()
  })

  it('boolean-поле называется «Активность», а не «Доступ»', async () => {
    const user = userEvent.setup()

    renderEntry(ACTIVE_CHANGE)

    await user.click(screen.getByRole('button', { name: 'Показать изменения' }))

    expect(screen.getByText('Активность')).toBeVisible()
    expect(screen.queryByText('Доступ')).not.toBeInTheDocument()
    expect(screen.getByText('Да → Нет')).toBeVisible()
  })
})
