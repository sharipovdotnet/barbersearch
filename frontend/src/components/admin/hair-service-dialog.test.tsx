import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { HairServiceDialog } from '@/components/admin/hair-service-dialog'
import type { AdminSpecialization } from '@/lib/admin-catalogue'
import {
  adminCatalogueHairService,
  adminCatalogueSpecialization,
  adminDisabledSpecialization,
  problemDetails,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Диалог услуги: выбор специализации вместо категории.
 *
 * Категория услуги производная, поэтому тело запроса несёт `specializationId`
 * и не содержит `category`. Справочник приходит с экрана каталога; здесь проверяем
 * его состояния: полный (включая выключенные строки), пустой и ошибочный.
 */

const SPECIALIZATIONS: AdminSpecialization[] = [
  adminCatalogueSpecialization,
  adminDisabledSpecialization,
]

function renderDialog(
  overrides: Partial<Parameters<typeof HairServiceDialog>[0]> = {}
) {
  const onSaved = vi.fn()
  const onClose = vi.fn()
  const onReloadSpecializations = vi.fn()

  render(
    <HairServiceDialog
      mode="create"
      service={null}
      specializations={SPECIALIZATIONS}
      specializationsStatus="ready"
      specializationsError={null}
      onReloadSpecializations={onReloadSpecializations}
      onClose={onClose}
      onSaved={onSaved}
      {...overrides}
    />
  )

  return { onSaved, onClose, onReloadSpecializations }
}

function dialog(): HTMLElement {
  return screen.getByRole('dialog')
}

describe('HairServiceDialog: специализация', () => {
  it('создание: первая специализация выбрана, выключенная помечена, тело без category', async () => {
    const bodies: unknown[] = []
    const user = userEvent.setup()

    server.use(
      http.post('/api/hair-services', async ({ request }) => {
        bodies.push(await request.json())

        return HttpResponse.json({ id: 'created-id' }, { status: 201 })
      })
    )

    const { onSaved } = renderDialog()
    const form = dialog()

    expect(within(form).getByLabelText('Специализация')).toHaveValue(
      adminCatalogueSpecialization.id
    )
    expect(
      within(form).getByRole('option', { name: 'Оформление бороды (выключена)' })
    ).toBeInTheDocument()
    expect(
      within(form).getByText('Категория определяется специализацией: Стрижка')
    ).toBeInTheDocument()

    await user.type(within(form).getByLabelText('Название'), 'Фейд')
    await user.click(within(form).getByRole('button', { name: 'Создать' }))

    await waitFor(() => {
      expect(bodies).toStrictEqual([
        {
          name: 'Фейд',
          description: null,
          defaultDurationMinutes: 30,
          specializationId: adminCatalogueSpecialization.id,
        },
      ])
    })
    expect(bodies[0]).not.toHaveProperty('category')
    expect(onSaved).toHaveBeenCalledWith('Услуга создана')
  })

  it('правка: показывает текущую специализацию и разрешает сменить её', async () => {
    const bodies: unknown[] = []
    const user = userEvent.setup()

    server.use(
      http.put('/api/hair-services/:id', async ({ request }) => {
        bodies.push(await request.json())

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { onSaved } = renderDialog({
      mode: 'edit',
      service: adminCatalogueHairService,
    })
    const form = dialog()

    expect(within(form).getByLabelText('Специализация')).toHaveValue(
      adminCatalogueHairService.specializationId
    )

    await user.selectOptions(
      within(form).getByLabelText('Специализация'),
      adminDisabledSpecialization.id
    )
    await user.click(within(form).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => {
      expect(bodies).toStrictEqual([
        {
          name: 'Фейд',
          description: null,
          defaultDurationMinutes: 45,
          specializationId: adminDisabledSpecialization.id,
        },
      ])
    })
    expect(onSaved).toHaveBeenCalledWith('Изменения сохранены')
  })

  it('пустой справочник: подсказка, кнопка недоступна, форма не отправляется', async () => {
    const posts = vi.fn()

    server.use(
      http.post('/api/hair-services', () => {
        posts()

        return HttpResponse.json({ id: 'created-id' }, { status: 201 })
      })
    )

    const user = userEvent.setup()
    renderDialog({ specializations: [] })
    const form = dialog()

    expect(
      within(form).getByText(
        'В справочнике нет специализаций. Создайте специализацию во вкладке «Специализации», чтобы добавить услугу.'
      )
    ).toBeInTheDocument()
    expect(within(form).getByRole('button', { name: 'Создать' })).toBeDisabled()

    await user.type(within(form).getByLabelText('Название'), 'Фейд')

    const formElement = form.querySelector('form')

    if (!formElement) {
      throw new Error('Форма диалога не найдена')
    }

    fireEvent.submit(formElement)

    expect(
      await within(form).findAllByText('Выберите специализацию для услуги.')
    ).not.toHaveLength(0)
    expect(posts).not.toHaveBeenCalled()
  })

  it('ошибка справочника объясняется и повторяется кнопкой', async () => {
    const user = userEvent.setup()
    const { onReloadSpecializations } = renderDialog({
      specializations: [],
      specializationsStatus: 'error',
      specializationsError: 'Не удалось загрузить специализации.',
    })
    const form = dialog()

    expect(
      within(form).getByText('Не удалось загрузить специализации.')
    ).toBeInTheDocument()
    expect(within(form).getByRole('button', { name: 'Создать' })).toBeDisabled()

    await user.click(within(form).getByRole('button', { name: 'Повторить' }))

    expect(onReloadSpecializations).toHaveBeenCalledTimes(1)
  })

  it('серверный код пустой специализации показывается у поля', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/hair-services', () =>
        HttpResponse.json(
          problemDetails(400, { errorCodes: ['hairService.specializationId'] }),
          { status: 400 }
        )
      )
    )

    renderDialog()
    const form = dialog()

    await user.type(within(form).getByLabelText('Название'), 'Фейд')
    await user.click(within(form).getByRole('button', { name: 'Создать' }))

    expect(
      (await within(form).findAllByText('Выберите специализацию для услуги.'))
        .length
    ).toBeGreaterThan(0)
  })
})
