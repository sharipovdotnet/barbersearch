import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { SpecializationDialog } from '@/components/admin/specialization-dialog'
import {
  adminCatalogueSpecialization,
  adminDisabledSpecialization,
  problemDetails,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Диалог специализации: обязательная категория при создании и правке.
 *
 * Категория задаёт фильтр витрины и категорию услуг специализации, поэтому уходит
 * в теле каждого запроса. Имя по-прежнему уникально и проверяется сервером.
 */

function renderDialog(
  overrides: Partial<Parameters<typeof SpecializationDialog>[0]> = {}
) {
  const onSaved = vi.fn()
  const onClose = vi.fn()

  render(
    <SpecializationDialog
      mode="create"
      specialization={null}
      onClose={onClose}
      onSaved={onSaved}
      {...overrides}
    />
  )

  return { onSaved, onClose }
}

function dialog(): HTMLElement {
  return screen.getByRole('dialog')
}

describe('SpecializationDialog: категория', () => {
  it('создание: категория по умолчанию и выбранная уходят в теле', async () => {
    const bodies: unknown[] = []
    const user = userEvent.setup()

    server.use(
      http.post('/api/specializations', async ({ request }) => {
        bodies.push(await request.json())

        return HttpResponse.json({ id: 'created-id' }, { status: 201 })
      })
    )

    const { onSaved } = renderDialog()
    const form = dialog()

    expect(within(form).getByLabelText('Категория')).toHaveValue('Haircut')

    await user.type(within(form).getByLabelText('Название'), 'Массаж')
    await user.selectOptions(within(form).getByLabelText('Категория'), 'Other')
    await user.click(within(form).getByRole('button', { name: 'Создать' }))

    await waitFor(() => {
      expect(bodies).toStrictEqual([
        { name: 'Массаж', category: 'Other', description: null },
      ])
    })
    expect(onSaved).toHaveBeenCalledWith('Специализация создана')
  })

  it('правка: категория заполнена текущей и меняется вместе с названием', async () => {
    const bodies: unknown[] = []
    const user = userEvent.setup()

    server.use(
      http.put('/api/specializations/:id', async ({ request }) => {
        bodies.push(await request.json())

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { onSaved } = renderDialog({
      mode: 'edit',
      specialization: adminDisabledSpecialization,
    })
    const form = dialog()

    expect(within(form).getByLabelText('Категория')).toHaveValue('Beard')

    await user.selectOptions(within(form).getByLabelText('Категория'), 'Styling')
    await user.click(within(form).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => {
      expect(bodies).toStrictEqual([
        {
          name: adminDisabledSpecialization.name,
          category: 'Styling',
          description: null,
        },
      ])
    })
    expect(onSaved).toHaveBeenCalledWith('Специализация переименована')
  })

  it('неизвестная категория от сервера показывается у поля', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/specializations', () =>
        HttpResponse.json(
          problemDetails(400, { errorCodes: ['specialization.category'] }),
          { status: 400 }
        )
      )
    )

    renderDialog()
    const form = dialog()

    await user.type(within(form).getByLabelText('Название'), 'Массаж')
    await user.click(within(form).getByRole('button', { name: 'Создать' }))

    expect(
      (await within(form).findAllByText(
        'Неизвестная категория. Выберите категорию из списка.'
      )).length
    ).toBeGreaterThan(0)
  })

  it('пустое название не отправляет запрос, даже если категория выбрана', async () => {
    const posts = vi.fn()
    const user = userEvent.setup()

    server.use(
      http.post('/api/specializations', () => {
        posts()

        return HttpResponse.json({ id: 'created-id' }, { status: 201 })
      })
    )

    renderDialog()
    const form = dialog()

    await user.selectOptions(within(form).getByLabelText('Категория'), 'Beard')
    await user.click(within(form).getByRole('button', { name: 'Создать' }))

    expect(
      within(form).getByText('Введите название специализации.')
    ).toBeInTheDocument()
    expect(posts).not.toHaveBeenCalled()
  })

  it('подсказка объясняет связь категории и витрины', () => {
    renderDialog()

    expect(
      screen.getByText(
        'Категория специализации определяет фильтр витрины и категорию её услуг.'
      )
    ).toBeInTheDocument()
  })
})

describe('данные для правки', () => {
  it('активная специализация приходит со своей категорией', () => {
    renderDialog({
      mode: 'edit',
      specialization: adminCatalogueSpecialization,
    })

    expect(screen.getByLabelText('Категория')).toHaveValue('Haircut')
  })
})
