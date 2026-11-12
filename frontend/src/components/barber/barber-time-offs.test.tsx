import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { BarberTimeOffs } from '@/components/barber/barber-time-offs'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const ENDPOINT = `/api/barber-profiles/${PROFILE_ID}/time-offs`

const CONFLICT = {
  id: '9e100000-0000-4000-8000-000000000031',
  startAt: '2026-12-01T19:00:00+00:00',
  endAt: '2026-12-01T19:30:00+00:00',
  timeZoneId: 'Asia/Yekaterinburg',
  customerName: 'Иван Петров',
  serviceName: 'Стрижка',
  status: 'Confirmed',
}

function conflictResponse() {
  return HttpResponse.json(
    {
      ...problemDetails(409, { errorCodes: ['barberSchedule.conflict'] }),
      conflicts: [CONFLICT],
    },
    { status: 409 }
  )
}

describe('BarberTimeOffs, конфликт сохранения', () => {
  it('на 409 открывает диалог с периодом, а после подтверждения шлёт force', async () => {
    const onChanged = vi.fn()
    let calls = 0
    let forced = ''

    server.use(
      http.post(ENDPOINT, ({ request }) => {
        calls += 1
        forced = new URL(request.url).search

        return calls === 1
          ? conflictResponse()
          : new HttpResponse(null, { status: 204 })
      })
    )

    render(
      <BarberTimeOffs
        barberProfileId={PROFILE_ID}
        timeOffs={[]}
        timeZoneId="Asia/Yekaterinburg"
        onChanged={onChanged}
      />
    )

    fireEvent.change(screen.getByLabelText('С'), {
      target: { value: '2026-12-01' },
    })
    fireEvent.change(screen.getByLabelText('По'), {
      target: { value: '2026-12-03' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Добавить отпуск' }))

    const dialog = await screen.findByRole('alertdialog')
    expect(dialog).toHaveAccessibleName('Записи в нерабочем периоде')
    expect(onChanged).not.toHaveBeenCalled()

    await userEvent.click(
      screen.getByRole('button', { name: 'Всё равно сохранить' })
    )

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Отпуск добавлен.')
    })

    expect(forced).toBe('?force=true')
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('«Удалить» не участвует в проверке конфликтов', async () => {
    const onChanged = vi.fn()
    let method = ''

    server.use(
      http.delete(
        `/api/barber-profiles/${PROFILE_ID}/time-offs/44444444-4444-4444-8444-444444444444`,
        ({ request }) => {
          method = request.method

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    render(
      <BarberTimeOffs
        barberProfileId={PROFILE_ID}
        timeOffs={[
          {
            id: '44444444-4444-4444-8444-444444444444',
            startAt: '2026-11-30T19:00:00+00:00',
            endAt: '2026-12-02T19:00:00+00:00',
            reason: 'Отпуск',
          },
        ]}
        timeZoneId="Asia/Yekaterinburg"
        onChanged={onChanged}
      />
    )

    await userEvent.click(screen.getByRole('button', { name: /^Удалить отпуск/ }))

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Отпуск удалён.')
    })

    expect(method).toBe('DELETE')
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })
})
