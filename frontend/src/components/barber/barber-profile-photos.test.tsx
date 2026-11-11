import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { BarberProfilePhotos } from '@/components/barber/barber-profile-photos'
import { preparePortfolioImage } from '@/lib/image-prep'
import type { PortfolioPhoto } from '@/lib/portfolio-photos'
import { noContent, problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

vi.mock('@/lib/image-prep', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/image-prep')>()

  return {
    ...actual,
    // jsdom не рисует canvas: подготовку подменяем, транспорт остаётся настоящим.
    preparePortfolioImage: vi.fn(async (file: File) => file),
  }
})

const PROFILE_ID = '9e100000-0000-4000-8000-000000000015'
const BASE = `/api/barber-profiles/${PROFILE_ID}/photos`

const createObjectURL = vi.fn(() => 'blob:preview')
const revokeObjectURL = vi.fn()

function photo(overrides: Partial<PortfolioPhoto> = {}): PortfolioPhoto {
  return {
    id: '9e200000-0000-4000-8000-000000000001',
    url: `/api/media/portfolio/${PROFILE_ID}/9e200000000040008000000000000001.jpg`,
    thumbnailUrl: null,
    description: 'Стрижка',
    sortOrder: 0,
    isPublished: true,
    isBlocked: false,
    moderationStatus: 'Approved',
    rejectionReason: null,
    ...overrides,
  }
}

function renderSection(
  photos: readonly PortfolioPhoto[],
  onChanged: (message: string) => void = vi.fn()
) {
  render(
    <BarberProfilePhotos
      barberProfileId={PROFILE_ID}
      photos={photos}
      onChanged={onChanged}
    />
  )

  return onChanged
}

function uploadedFile(): File {
  return new File(['photo-bytes'], 'IMG_0001.jpeg', { type: 'image/jpeg' })
}

describe('BarberProfilePhotos: список', () => {
  beforeEach(() => {
    URL.createObjectURL = createObjectURL
    URL.revokeObjectURL = revokeObjectURL
  })

  it('пустое портфолио: пояснение и доступная кнопка загрузки', () => {
    renderSection([])

    expect(
      screen.getByRole('heading', { level: 2, name: 'Фотографии' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Работы видны в вашей карточке. Первое фото показывается первым.'
      )
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Фотографий нет. Добавьте работы, чтобы клиенты видели ваш стиль.'
      )
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Добавить фото' })
    ).toBeEnabled()
  })

  it('превью карточки получает srcset и sizes кабинетной сетки', () => {
    const item = photo()

    renderSection([item])

    const image = screen.getByRole('img', { name: 'Стрижка' })

    expect(image).toHaveAttribute('src', item.url)
    expect(image).toHaveAttribute(
      'srcset',
      `${item.url}?size=sm 256w, ${item.url}?size=md 512w, ${item.url}?size=lg 1024w`
    )
    expect(image).toHaveAttribute(
      'sizes',
      '(min-width: 640px) 50vw, 100vw'
    )
  })

  it('десять фото: кнопка загрузки disabled и честный текст лимита', () => {
    const photos = Array.from({ length: 10 }, (_item, index) =>
      photo({
        id: `9e200000-0000-4000-8000-0000000000${index + 10}`,
        description: `Работа ${index + 1}`,
        sortOrder: index,
      })
    )

    renderSection(photos)

    expect(
      screen.getByRole('button', { name: 'Добавить фото' })
    ).toBeDisabled()
    expect(
      screen.getByText(
        'В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.'
      )
    ).toBeInTheDocument()
  })

  it('заблокированная работа: бейдж администратора и только «Удалить»', () => {
    renderSection([photo({ isBlocked: true, isPublished: false })])

    expect(screen.getByText('Скрыто администратором')).toBeInTheDocument()
    expect(screen.queryByText('Скрыто')).not.toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Удалить: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: /Вернуть/ })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: /Скрыть/ })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: /Выше/ })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: /изменить подпись/i })
    ).not.toBeInTheDocument()
  })

  it('скрытая владельцем работа показывает бейдж «Скрыто» и «Вернуть»', () => {
    renderSection([photo({ isPublished: false })])

    expect(screen.getByText('Скрыто')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Вернуть: Стрижка' })
    ).toBeInTheDocument()
  })

  it('кадр «На проверке»: бейдж, пояснение и действия без «Скрыть/Вернуть»', () => {
    renderSection([photo({ moderationStatus: 'Pending' })])

    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Кадр виден только вам, пока администратор его не одобрит.'
      )
    ).toBeInTheDocument()
    // Подпись, порядок и удаление доступны и до решения администратора.
    expect(
      screen.getByRole('button', { name: 'Изменить подпись: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Удалить: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Скрыть: Стрижка' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Вернуть: Стрижка' })
    ).not.toBeInTheDocument()
  })

  it('отклонённый кадр: бейдж, причина и отсутствие «Скрыть/Вернуть»', () => {
    renderSection([
      photo({
        moderationStatus: 'Rejected',
        rejectionReason: 'Слишком тёмный кадр.',
      }),
    ])

    expect(screen.getByText('Отклонено')).toBeInTheDocument()
    expect(
      screen.getByText('Причина: Слишком тёмный кадр.')
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Изменить подпись: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Удалить: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Скрыть: Стрижка' })
    ).not.toBeInTheDocument()
  })

  it('края порядка: «Выше» disabled на первом фото, «Ниже» — на последнем', () => {
    renderSection([
      photo(),
      photo({
        id: '9e200000-0000-4000-8000-000000000002',
        description: 'Борода',
        sortOrder: 1,
      }),
    ])

    expect(
      screen.getByRole('button', { name: 'Выше: Стрижка' })
    ).toBeDisabled()
    expect(
      screen.getByRole('button', { name: 'Ниже: Стрижка' })
    ).toBeEnabled()
    expect(
      screen.getByRole('button', { name: 'Выше: Борода' })
    ).toBeEnabled()
    expect(
      screen.getByRole('button', { name: 'Ниже: Борода' })
    ).toBeDisabled()
  })
})

describe('BarberProfilePhotos: мутации', () => {
  beforeEach(() => {
    URL.createObjectURL = createObjectURL
    URL.revokeObjectURL = revokeObjectURL
  })

  it('«Скрыть» снимает с публикации и объявляет результат', async () => {
    const user = userEvent.setup()
    const onChanged = renderSection([photo()])
    let calledPath = ''

    server.use(
      http.delete(`${BASE}/:photoId/publication`, ({ request }) => {
        calledPath = new URL(request.url).pathname

        return noContent()
      })
    )

    await user.click(screen.getByRole('button', { name: 'Скрыть: Стрижка' }))

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Фото скрыто.')
    })
    expect(calledPath).toBe(`${BASE}/${photo().id}/publication`)
  })

  it('«Вернуть» публикует скрытую работу', async () => {
    const user = userEvent.setup()
    const onChanged = renderSection([photo({ isPublished: false })])
    let method = ''

    server.use(
      http.post(`${BASE}/:photoId/publication`, ({ request }) => {
        method = request.method

        return noContent()
      })
    )

    await user.click(screen.getByRole('button', { name: 'Вернуть: Стрижка' }))

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Фото снова в карточке.')
    })
    expect(method).toBe('POST')
  })

  it('порядок меняется одним bulk-запросом с полным массивом id', async () => {
    const user = userEvent.setup()
    const second = photo({
      id: '9e200000-0000-4000-8000-000000000002',
      description: 'Борода',
      sortOrder: 1,
    })
    const onChanged = renderSection([photo(), second])
    let path = ''
    let body: unknown = null

    server.use(
      http.put(`${BASE}/order`, async ({ request }) => {
        path = new URL(request.url).pathname
        body = await request.json()

        return noContent()
      })
    )

    await user.click(screen.getByRole('button', { name: 'Ниже: Стрижка' }))

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Порядок изменён.')
    })
    expect(path).toBe(`${BASE}/order`)
    expect(body).toStrictEqual({
      photoIds: [second.id, photo().id],
    })
  })

  it('удаление: alertdialog с точным текстом и DELETE', async () => {
    const user = userEvent.setup()
    const onChanged = renderSection([photo()])
    let path = ''

    server.use(
      http.delete(`${BASE}/:photoId`, ({ request }) => {
        path = new URL(request.url).pathname

        return noContent()
      })
    )

    await user.click(screen.getByRole('button', { name: 'Удалить: Стрижка' }))

    const dialog = screen.getByRole('alertdialog')

    expect(
      within(dialog).getByText(
        'Фото исчезнет из карточки и поиска. Это необратимо.'
      )
    ).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Удалить' }))

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Фото удалено.')
    })
    expect(path).toBe(`${BASE}/${photo().id}`)
  })

  it('правка подписи: PUT с новым текстом и объявление', async () => {
    const user = userEvent.setup()
    const onChanged = renderSection([photo()])
    let body: unknown = null

    server.use(
      http.put(`${BASE}/:photoId/description`, async ({ request }) => {
        body = await request.json()

        return noContent()
      })
    )

    await user.click(
      screen.getByRole('button', { name: 'Изменить подпись: Стрижка' })
    )

    const textarea = screen.getByLabelText('Подпись')

    await user.clear(textarea)
    await user.type(textarea, 'Фейд и борода')
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Сохранить',
      })
    )

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Подпись сохранена.')
    })
    expect(body).toStrictEqual({ description: 'Фейд и борода' })
  })

  it('ошибка мутации: role="alert" над сеткой, список остаётся', async () => {
    const user = userEvent.setup()
    const onChanged = renderSection([photo()])

    server.use(
      http.delete(`${BASE}/:photoId/publication`, () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    await user.click(screen.getByRole('button', { name: 'Скрыть: Стрижка' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Не удалось выполнить действие с фото. Попробуйте ещё раз.'
    )
    expect(onChanged).not.toHaveBeenCalled()
    expect(screen.getByAltText('Стрижка')).toBeInTheDocument()
  })
})

describe('BarberProfilePhotos: загрузка', () => {
  beforeEach(() => {
    URL.createObjectURL = createObjectURL
    URL.revokeObjectURL = revokeObjectURL
  })

  it('файл, превью, подпись → 201, объявление и закрытие диалога', async () => {
    const user = userEvent.setup()
    const onChanged = renderSection([])
    let uploads = 0

    server.use(
      http.post(`${BASE}/upload`, () => {
        uploads += 1

        return HttpResponse.json(
          { id: '9e200000-0000-4000-8000-000000000099' },
          { status: 201 }
        )
      })
    )

    await user.click(screen.getByRole('button', { name: 'Добавить фото' }))
    await user.upload(screen.getByLabelText('Файл'), uploadedFile())

    expect(
      screen.getByAltText('Предпросмотр выбранного фото')
    ).toBeInTheDocument()
    expect(screen.getByText('0/2000')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Подпись'), 'Стрижка')
    expect(screen.getByText('7/2000')).toBeInTheDocument()

    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Загрузить',
      })
    )

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Фото добавлено.')
    })
    expect(uploads).toBe(1)
    expect(vi.mocked(preparePortfolioImage)).toHaveBeenCalledTimes(1)
    expect(
      screen.queryByText('Предпросмотр выбранного фото')
    ).not.toBeInTheDocument()
    // Object URL освобождён при закрытии диалога.
    await waitFor(() => {
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview')
    })
  })

  it('11-е фото: 409 остаётся в диалоге и объясняет лимит', async () => {
    const user = userEvent.setup()
    const onChanged = renderSection([photo()])

    server.use(
      http.post(`${BASE}/upload`, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['barberProfile.addPhoto.limit'],
          }),
          { status: 409 }
        )
      )
    )

    await user.click(screen.getByRole('button', { name: 'Добавить фото' }))
    await user.upload(screen.getByLabelText('Файл'), uploadedFile())
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Загрузить',
      })
    )

    expect(
      await screen.findByText(
        'В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.'
      )
    ).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(onChanged).not.toHaveBeenCalled()
  })

  it('без файла загрузка не уходит и просит файл', async () => {
    const user = userEvent.setup()
    renderSection([])

    await user.click(screen.getByRole('button', { name: 'Добавить фото' }))
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Загрузить',
      })
    )

    expect(await screen.findByRole('alert')).toHaveTextContent('Выберите файл.')
    expect(vi.mocked(preparePortfolioImage)).not.toHaveBeenCalled()
  })
})
