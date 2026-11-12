import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { BarberWeekSchedule } from '@/components/barber/barber-week-schedule'
import type { BarberScheduleSalon } from '@/lib/barber-schedule'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const SALON_ID = '22222222-2222-4222-8222-222222222222'
const ENDPOINT = `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/working-schedules`

const SALON: BarberScheduleSalon = {
  salonId: SALON_ID,
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  timeZoneId: 'Asia/Yekaterinburg',
  isPrimary: true,
  weekly: [
    { dayOfWeek: 'Monday', isDayOff: false, startTime: '10:00:00', endTime: '20:00:00' },
    { dayOfWeek: 'Tuesday', isDayOff: true, startTime: '00:00:00', endTime: '00:00:00' },
    { dayOfWeek: 'Wednesday', isDayOff: true, startTime: '00:00:00', endTime: '00:00:00' },
    { dayOfWeek: 'Thursday', isDayOff: true, startTime: '00:00:00', endTime: '00:00:00' },
    { dayOfWeek: 'Friday', isDayOff: true, startTime: '00:00:00', endTime: '00:00:00' },
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

describe('BarberWeekSchedule, конфликт сохранения', () => {
  it('на 409 открывает диалог, а после подтверждения шлёт force и объявляет успех', async () => {
    const onSaved = vi.fn()
    const searches: string[] = []
    let calls = 0

    server.use(
      http.put(ENDPOINT, ({ request }) => {
        calls += 1
        searches.push(new URL(request.url).search)

        return calls === 1
          ? conflictResponse()
          : new HttpResponse(null, { status: 204 })
      })
    )

    render(
      <BarberWeekSchedule
        barberProfileId={PROFILE_ID}
        salon={SALON}
        onSaved={onSaved}
      />
    )

    fireEvent.change(screen.getByLabelText('Окончание работы, Понедельник'), {
      target: { value: '18:00' },
    })
    await userEvent.click(
      screen.getByRole('button', { name: 'Сохранить расписание' })
    )

    await waitFor(() => {
      expect(searches).toStrictEqual([''])
    })

    const dialog = await screen.findByRole('alertdialog')
    expect(dialog).toHaveAccessibleName('Записи вне новых часов')
    expect(onSaved).not.toHaveBeenCalled()

    await userEvent.click(
      screen.getByRole('button', { name: 'Всё равно сохранить' })
    )

    await waitFor(() => {
      expect(onSaved).toHaveBeenCalledWith('Расписание сохранено.')
    })

    expect(searches).toStrictEqual(['', '?force=true'])
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('«Отмена» оставляет введённое и не отправляет второй запрос', async () => {
    const onSaved = vi.fn()
    let calls = 0

    server.use(
      http.put(ENDPOINT, () => {
        calls += 1

        return conflictResponse()
      })
    )

    render(
      <BarberWeekSchedule
        barberProfileId={PROFILE_ID}
        salon={SALON}
        onSaved={onSaved}
      />
    )

    fireEvent.change(screen.getByLabelText('Окончание работы, Понедельник'), {
      target: { value: '18:00' },
    })
    await userEvent.click(
      screen.getByRole('button', { name: 'Сохранить расписание' })
    )

    await screen.findByRole('alertdialog')
    await userEvent.click(screen.getByRole('button', { name: 'Отмена' }))

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    })

    expect(calls).toBe(1)
    expect(onSaved).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Окончание работы, Понедельник')).toHaveValue(
      '18:00'
    )
  })
})
