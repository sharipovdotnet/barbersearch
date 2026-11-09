import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { EditReviewDialog } from '@/components/account/edit-review-dialog'
import type { MyReview } from '@/lib/my-reviews'

function review(isPublished: boolean): MyReview {
  return {
    id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
    rating: 4,
    text: 'Отличная стрижка',
    isPublished,
    createdAt: '2026-09-01T10:00:00Z',
    updatedAt: '2026-09-01T10:00:00Z',
    barber: {
      id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      displayName: 'Артём Соколов',
    },
    visit: {
      bookingId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
      startAt: '2026-09-01T10:00:00Z',
      timeZoneId: 'Europe/Moscow',
      price: 2400,
      serviceName: 'Fade',
      salonName: 'Барбершоп на Тверской',
    },
  }
}

function renderDialog(value: MyReview) {
  return render(
    <EditReviewDialog
      review={value}
      onClose={() => {}}
      onSave={async () => {}}
    />
  )
}

describe('EditReviewDialog: судьба правки', () => {
  it('опубликованный отзыв: обещает повторную проверку только после смены текста', () => {
    renderDialog(review(true))

    expect(
      screen.getByText(
        'Оценку можно изменить сразу: повторная проверка нужна только для текста.'
      )
    ).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText(/Комментарий/), {
      target: { value: 'Другой текст после одобрения' },
    })

    expect(
      screen.getByText(
        'Изменение текста вернёт отзыв на проверку: до одобрения он не покажется на карточке барбера.'
      )
    ).toBeInTheDocument()
  })

  it('отзыв на проверке: изменения появятся после одобрения', () => {
    renderDialog(review(false))

    expect(
      screen.getByText(
        'Отзыв на проверке: изменения появятся на карточке после одобрения.'
      )
    ).toBeInTheDocument()
  })
})
