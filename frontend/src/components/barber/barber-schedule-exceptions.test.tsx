import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { BarberScheduleExceptions } from '@/components/barber/barber-schedule-exceptions'
import type { BarberScheduleSalon } from '@/lib/barber-schedule'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const SALON_ID = '22222222-2222-4222-8222-222222222222'
const ENDPOINT = `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/schedule-exceptions/2026-11-20`

const SALON: BarberScheduleSalon = {
  salonId: SALON_ID,
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  timeZoneId: 'Asia/Yekaterinburg',
  isPrimary: true,
  weekly: [
    { dayOfWeek: 'Monday', isDayOff: false, startTime: '10:00:00', endTime: '20:00:00' },
    { dayOfWeek: 'Tuesday', isDayOff: false, startTime: '10:00:00', endTime: '20:00:00' },
    { dayOfWeek: 'Wednesday', isDayOff: false, startTime: '10:00:00', endTime: '20:00:00' },
    { dayOfWeek: 'Thursday', isDayOff: false, startTime: '10:00:00', endTime: '20:00:00' },
    { dayOfWeek: 'Friday', isDayOff: false, startTime: '10:00:00', endTime: '20:00:00' },
    { dayOfWeek: 'Saturday', isDayOff: true, startTime: '00:00:00', endTime: '00:00:00' },
    { dayOfWeek: 'Sunday', isDayOff: true, startTime: '00:00:00', endTime: '00:00:00' },
  ],
  exceptions: [],
}

const CONFLICT = {
  id: '9e100000-0000-4000-8000-000000000031',
  startAt: '2026-11-20T09:00:00+00:00',
  endAt: '2026-11-20T09:30:00+00:00',
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

describe('BarberScheduleExceptions, конфликт сохранения', () => {
  it('на 409 открывает диалог, а после подтверждения шлёт force и объявляет успех', async () => {
    const onChanged = vi.fn()
    let calls = 0
    let forced = ''

    server.use(
      http.put(ENDPOINT, ({ request }) => {
        calls += 1
        forced = new URL(request.url).search

        return calls === 1
          ? conflictResponse()
          : new HttpResponse(null, { status: 204 })
      })
    )

    render(
      <BarberScheduleExceptions
        barberProfileId={PROFILE_ID}
        salon={SALON}
        onChanged={onChanged}
      />
    )

    fireEvent.change(screen.getByLabelText('Дата'), {
      target: { value: '2026-11-20' },
    })
    await userEvent.click(
      screen.getByRole('button', { name: 'Добавить исключение' })
    )

    const dialog = await screen.findByRole('alertdialog')
    expect(dialog).toHaveAccessibleName('Записи вне новых часов')
    expect(onChanged).not.toHaveBeenCalled()

    await userEvent.click(
      screen.getByRole('button', { name: 'Всё равно сохранить' })
    )

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Исключение добавлено.')
    })

    expect(forced).toBe('?force=true')
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('«Удалить» не участвует в проверке конфликтов', async () => {
    const onChanged = vi.fn()
    let method = ''

    server.use(
      http.delete(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/schedule-exceptions/2026-11-15`,
        ({ request }) => {
          method = request.method

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    render(
      <BarberScheduleExceptions
        barberProfileId={PROFILE_ID}
        salon={{
          ...SALON,
          exceptions: [
            {
              date: '2026-11-15',
              isDayOff: true,
              startTime: '00:00:00',
              endTime: '00:00:00',
              reason: 'Личные дела',
            },
          ],
        }}
        onChanged={onChanged}
      />
    )

    await userEvent.click(
      screen.getByRole('button', { name: /^Удалить исключение/ })
    )

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledWith('Исключение удалено.')
    })

    expect(method).toBe('DELETE')
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })
})
