import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  ADMIN_AUDIT_ACTOR_MAX_LENGTH,
  ADMIN_AUDIT_ENTITY_LABELS,
  ADMIN_AUDIT_PAGE_SIZE,
  describeAdminAuditError,
  fetchAdminAuditEntries,
  formatAuditAction,
  formatAuditValue,
} from '@/lib/admin-audit'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Транспорт и форматирование админского журнала.
 *
 * Контракт списка: `entityType` уходит всегда, `actor`/`from`/`to` — только
 * непустыми, `cursor` — только при наличии. Фильтры живут в состоянии React,
 * поэтому email автора в URL попадает только как параметр запроса, а не как
 * часть адреса страницы.
 */

const AUDIT_URL = '/api/admin/audit'

/** Перехватывает GET журнала и складывает параметры каждого запроса. */
function captureAudit(): URLSearchParams[] {
  const captured: URLSearchParams[] = []

  server.use(
    http.get(AUDIT_URL, ({ request }) => {
      captured.push(new URL(request.url).searchParams)

      return HttpResponse.json({ items: [], nextCursor: null })
    })
  )

  return captured
}

function entries(params: URLSearchParams): [string, string][] {
  return [...params.entries()]
}

/** Полная строка ответа: ровно то, что читает экран. */
const ENTRY = {
  id: '9e700000-0000-4000-8000-000000000020',
  occurredAt: '2026-09-10T10:20:00+00:00',
  action: 'user.updated',
  entityType: 'user',
  entityId: '9e000000-0000-4000-8000-000000000002',
  actor: { id: '9e000000-0000-4000-8000-000000000001', email: 'e2e-admin@barbersearch.test' },
  changes: [{ field: 'role', from: 'Customer', to: 'Admin' }],
}

describe('fetchAdminAuditEntries', () => {
  it('по умолчанию отправляет только entityType=all', async () => {
    const captured = captureAudit()

    await expect(
      fetchAdminAuditEntries({ entityType: 'all' })
    ).resolves.toStrictEqual({ items: [], nextCursor: null })

    expect(captured).toHaveLength(1)
    expect(entries(captured[0])).toStrictEqual([['entityType', 'all']])
  })

  it('передаёт фильтры в порядке entityType, actor, from, to', async () => {
    const captured = captureAudit()

    await fetchAdminAuditEntries({
      entityType: 'review',
      actor: 'e2e-admin',
      from: '2026-09-10',
      to: '2026-09-10',
    })

    expect(entries(captured[0])).toStrictEqual([
      ['entityType', 'review'],
      ['actor', 'e2e-admin'],
      ['from', '2026-09-10'],
      ['to', '2026-09-10'],
    ])
  })

  it('не отправляет пустые фильтры', async () => {
    const captured = captureAudit()

    await fetchAdminAuditEntries({
      entityType: 'user',
      actor: '',
      from: '',
      to: '',
    })

    expect(entries(captured[0])).toStrictEqual([['entityType', 'user']])
  })

  it('добавляет курсор последним параметром', async () => {
    const captured = captureAudit()

    await fetchAdminAuditEntries({
      entityType: 'barber_profile',
      actor: 'e2e-admin',
      cursor: 'opaque-cursor',
    })

    expect(entries(captured[0])).toStrictEqual([
      ['entityType', 'barber_profile'],
      ['actor', 'e2e-admin'],
      ['cursor', 'opaque-cursor'],
    ])
  })

  it('битый ответ списка — ошибка', async () => {
    server.use(
      http.get(AUDIT_URL, () =>
        HttpResponse.json({ items: [{ id: '' }], nextCursor: null })
      )
    )

    await expect(
      fetchAdminAuditEntries({ entityType: 'all' })
    ).rejects.toThrow(ApiError)
  })

  it('лишние поля ответа не ломают guard', async () => {
    server.use(
      http.get(AUDIT_URL, () =>
        HttpResponse.json({
          items: [
            {
              ...ENTRY,
              databaseRowId: 42,
              actor: { ...ENTRY.actor, role: 'Admin' },
              changes: [{ ...ENTRY.changes[0], fieldType: 'enum' }],
            },
          ],
          nextCursor: 'next',
          totalCount: 21,
        })
      )
    )

    const page = await fetchAdminAuditEntries({ entityType: 'all' })

    expect(page.nextCursor).toBe('next')
    expect(page.items).toHaveLength(1)
    expect(page.items[0].id).toBe(ENTRY.id)
    expect(page.items[0].entityType).toBe('user')
    expect(page.items[0].actor?.email).toBe('e2e-admin@barbersearch.test')
    // Guard терпит лишние поля, но не вычищает их: проверяем прочитанный контракт.
    expect(page.items[0].changes[0]).toMatchObject({
      field: 'role',
      from: 'Customer',
      to: 'Admin',
    })
  })

  it('скалярное правило строгое: объект в from — битый ответ', async () => {
    server.use(
      http.get(AUDIT_URL, () =>
        HttpResponse.json({
          items: [
            {
              ...ENTRY,
              changes: [{ field: 'role', from: { value: 'Customer' }, to: 'Admin' }],
            },
          ],
          nextCursor: null,
        })
      )
    )

    await expect(
      fetchAdminAuditEntries({ entityType: 'all' })
    ).rejects.toThrow(ApiError)
  })
})

describe('formatAuditValue', () => {
  it('null — прочерк', () => {
    expect(formatAuditValue('text', null)).toBe('—')
  })

  it('boolean — «Да» и «Нет»', () => {
    expect(formatAuditValue('isActive', true)).toBe('Да')
    expect(formatAuditValue('isActive', false)).toBe('Нет')
  })

  it('скрытое значение не раскрывается', () => {
    expect(formatAuditValue('passwordHash', '[redacted]')).toBe('скрыто')
  })

  it('роль — русская подпись, неизвестная роль — исходная строка', () => {
    expect(formatAuditValue('role', 'Admin')).toBe('Администратор')
    expect(formatAuditValue('role', 'Owner')).toBe('Owner')
  })

  it('грейд — русская подпись', () => {
    expect(formatAuditValue('grade', 'SeniorBarber')).toBe('Старший барбер')
  })

  it('число и неизвестное поле остаются как есть', () => {
    expect(formatAuditValue('rating', 5)).toBe('5')
    expect(formatAuditValue('favouriteColor', 'green')).toBe('green')
  })
})

describe('formatAuditAction', () => {
  it('подпись берётся по хвосту после последней точки', () => {
    expect(formatAuditAction('user.updated')).toBe('Изменено')
    expect(formatAuditAction('review.created')).toBe('Создано')
    expect(formatAuditAction('barber_profile.deleted')).toBe('Удалено')
  })

  it('неизвестное действие — исходный хвост, без выдумки', () => {
    expect(formatAuditAction('user.archived')).toBe('archived')
  })
})

describe('подписи и константы контракта', () => {
  it('подписи типов сущностей — русские', () => {
    expect(ADMIN_AUDIT_ENTITY_LABELS).toStrictEqual({
      all: 'Все сущности',
      user: 'Пользователь',
      barber_profile: 'Барбер',
      review: 'Отзыв',
    })
  })

  it('размер страницы и лимит автора совпадают с сервером', () => {
    expect(ADMIN_AUDIT_PAGE_SIZE).toBe(20)
    expect(ADMIN_AUDIT_ACTOR_MAX_LENGTH).toBe(100)
  })
})

describe('describeAdminAuditError', () => {
  it('403 объясняет роль', () => {
    const error = new ApiError(403, problemDetails(403), 'Forbidden.')

    expect(describeAdminAuditError(error, 'Не удалось загрузить журнал.')).toBe(
      'Раздел доступен только администратору.'
    )
  })

  it('устаревший курсор предлагает начать с первой страницы', () => {
    const error = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['admin.audit.cursor.invalid'] }),
      'Invalid.'
    )

    expect(describeAdminAuditError(error, 'Не удалось загрузить журнал.')).toBe(
      'Список устарел. Обновите и начните с первой страницы.'
    )
  })

  it('429 просит подождать минуту', () => {
    const error = new ApiError(429, problemDetails(429), 'Too many.')

    expect(describeAdminAuditError(error, 'Не удалось загрузить журнал.')).toBe(
      'Слишком много запросов. Подождите минуту и повторите.'
    )
  })

  it('на неизвестной ошибке отдаёт запасной текст', () => {
    expect(
      describeAdminAuditError(new Error('сеть'), 'Не удалось загрузить журнал.')
    ).toBe('Не удалось загрузить журнал.')
  })
})
