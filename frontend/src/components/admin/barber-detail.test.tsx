import { type ComponentProps, type ReactElement } from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { BarberDetail } from '@/components/admin/barber-detail'
import type { AdminBarberDetail } from '@/lib/admin-barbers'
import type { PortfolioPhoto } from '@/lib/portfolio-photos'
import { noContent, problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Фотографии в админской карточке барбера: статусы премодерации и локальное
 * обновление строки после решения. Второго `GET` карточки быть не должно.
 */

const BARBER_ID = '9e800000-0000-4000-8000-000000000001'
const USER_ID = '9e800000-0000-4000-8000-000000000011'
const PHOTO_ID = '9e200000-0000-4000-8000-000000000001'

function photo(overrides: Partial<PortfolioPhoto> = {}): PortfolioPhoto {
  return {
    id: PHOTO_ID,
    url: `/api/media/portfolio/${BARBER_ID}/${PHOTO_ID}.jpg`,
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

function detail(overrides: Partial<AdminBarberDetail> = {}): AdminBarberDetail {
  return {
    id: BARBER_ID,
    userId: USER_ID,
    displayName: 'Артём Соколов',
    email: 'artem@barbersearch.test',
    phoneNumber: null,
    avatarUrl: null,
    isAvatarBlocked: false,
    avatarModerationStatus: 'Approved',
    avatarRejectionReason: null,
    grade: 'ArtDirector',
    bio: null,
    yearsOfExperience: 5,
    rating: null,
    reviewsCount: 0,
    isActive: true,
    isVerified: true,
    readiness: {
      isProfileFilled: false,
      isEmailVerified: true,
      servicesCount: 0,
      salonsCount: 0,
      scheduleDaysCount: 0,
      photosCount: 1,
    },
    photos: [photo()],
    createdAt: '2026-09-28T15:12:00+00:00',
    updatedAt: '2026-11-02T10:53:00+00:00',
    ...overrides,
  }
}

type DetailProps = ComponentProps<typeof BarberDetail>

function detailTree(
  barber: AdminBarberDetail,
  overrides: Partial<DetailProps> = {}
): ReactElement {
  return (
    <BarberDetail
      barber={barber}
      pendingAction={null}
      actionError={null}
      onVerify={() => {}}
      onUnverify={() => {}}
      onChangeGrade={() => {}}
      onHide={() => {}}
      onShow={() => {}}
      pendingPhotoId={null}
      photoError={null}
      onBlockPhoto={() => {}}
      onUnblockPhoto={() => {}}
      onBlockAvatar={() => {}}
      onUnblockAvatar={() => {}}
      onBack={() => {}}
      {...overrides}
    />
  )
}

function renderDetail(
  barber: AdminBarberDetail,
  overrides: Partial<DetailProps> = {}
) {
  return render(detailTree(barber, overrides))
}

describe('BarberDetail: статусы фотографий', () => {
  it('кадр «На проверке» предлагает одобрить или отклонить', () => {
    renderDetail(detail({ photos: [photo({ moderationStatus: 'Pending' })] }))

    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Одобрить: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Отклонить: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Скрыть: Стрижка' })
    ).not.toBeInTheDocument()
  })

  it('отклонённый кадр показывает причину без кнопок решения', () => {
    renderDetail(
      detail({
        photos: [
          photo({
            moderationStatus: 'Rejected',
            rejectionReason: 'Слишком тёмный кадр.',
          }),
        ],
      })
    )

    expect(screen.getByText('Отклонено')).toBeInTheDocument()
    expect(
      screen.getByText('Причина: Слишком тёмный кадр.')
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Одобрить: Стрижка' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отклонить: Стрижка' })
    ).not.toBeInTheDocument()
  })

  it('одобренный кадр показывает публикацию и «Скрыть»', () => {
    renderDetail(detail())

    expect(screen.getByText('Опубликовано')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Скрыть: Стрижка' })
    ).toBeInTheDocument()
  })

  it('скрытый владельцем кадр показывает «Скрыто владельцем»', () => {
    renderDetail(detail({ photos: [photo({ isPublished: false })] }))

    expect(screen.getByText('Скрыто владельцем')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Скрыть: Стрижка' })
    ).toBeInTheDocument()
  })

  it('заблокированный кадр остаётся только с «Разблокировать»', () => {
    renderDetail(
      detail({ photos: [photo({ isBlocked: true, isPublished: false })] })
    )

    expect(screen.getByText('Скрыто администратором')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Разблокировать: Стрижка' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Скрыть: Стрижка' })
    ).not.toBeInTheDocument()
  })
})

describe('BarberDetail: решения по фотографиям', () => {
  it('«Одобрить» обновляет строку локально, без второго GET', async () => {
    const user = userEvent.setup()
    let path = ''
    let method = ''

    server.use(
      http.post(
        '/api/barber-profiles/:barberProfileId/photos/:photoId/approval',
        ({ request }) => {
          path = new URL(request.url).pathname
          method = request.method

          return noContent()
        }
      )
    )

    renderDetail(detail({ photos: [photo({ moderationStatus: 'Pending' })] }))

    await user.click(
      screen.getByRole('button', { name: 'Одобрить: Стрижка' })
    )

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Фото одобрено.'
    )
    expect(path).toBe(
      `/api/barber-profiles/${BARBER_ID}/photos/${PHOTO_ID}/approval`
    )
    expect(method).toBe('POST')
    expect(screen.getByText('Опубликовано')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Скрыть: Стрижка' })
    ).toBeInTheDocument()
    expect(screen.queryByText('На проверке')).not.toBeInTheDocument()
  })

  it('«Отклонить» требует причину и обновляет строку с причиной', async () => {
    const user = userEvent.setup()
    let body: unknown = null

    server.use(
      http.post(
        '/api/barber-profiles/:barberProfileId/photos/:photoId/rejection',
        async ({ request }) => {
          body = await request.json()

          return noContent()
        }
      )
    )

    renderDetail(detail({ photos: [photo({ moderationStatus: 'Pending' })] }))

    await user.click(
      screen.getByRole('button', { name: 'Отклонить: Стрижка' })
    )

    const dialog = screen.getByRole('alertdialog')

    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(within(dialog).getByRole('alert')).toHaveTextContent(
      'Укажите причину отказа.'
    )
    expect(body).toBeNull()

    await user.type(
      within(dialog).getByLabelText('Причина отказа'),
      'Слишком тёмный кадр.'
    )
    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Фото отклонено.'
    )
    expect(body).toStrictEqual({ reason: 'Слишком тёмный кадр.' })
    expect(screen.getByText('Отклонено')).toBeInTheDocument()
    expect(
      screen.getByText('Причина: Слишком тёмный кадр.')
    ).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('после отмены диалога фокус возвращается на «Отклонить»', async () => {
    const user = userEvent.setup()

    renderDetail(detail({ photos: [photo({ moderationStatus: 'Pending' })] }))

    const trigger = screen.getByRole('button', {
      name: 'Отклонить: Стрижка',
    })

    await user.click(trigger)
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: 'Отмена',
      })
    )

    await waitFor(() => {
      expect(trigger).toHaveFocus()
    })
  })

  it('409 говорит, что фото уже проверено, и оставляет строку на проверке', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(
        '/api/barber-profiles/:barberProfileId/photos/:photoId/approval',
        () =>
          HttpResponse.json(
            problemDetails(409, {
              errorCodes: ['portfolioPhoto.moderation.invalidStatus'],
            }),
            { status: 409 }
          )
      )
    )

    renderDetail(detail({ photos: [photo({ moderationStatus: 'Pending' })] }))

    await user.click(
      screen.getByRole('button', { name: 'Одобрить: Стрижка' })
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Фото уже проверено. Обновите список.'
    )
    expect(screen.getByText('На проверке')).toBeInTheDocument()
  })

  it('скрытие одобренного кадра по-прежнему спрашивает подтверждение', async () => {
    const user = userEvent.setup()
    const onBlockPhoto = vi.fn()

    renderDetail(detail(), { onBlockPhoto })

    await user.click(screen.getByRole('button', { name: 'Скрыть: Стрижка' }))

    const dialog = screen.getByRole('alertdialog')

    expect(dialog).toHaveTextContent('Скрыть фото?')
    expect(onBlockPhoto).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Скрыть' }))

    expect(onBlockPhoto).toHaveBeenCalledOnce()
    expect(onBlockPhoto).toHaveBeenCalledWith(PHOTO_ID)
  })
})
