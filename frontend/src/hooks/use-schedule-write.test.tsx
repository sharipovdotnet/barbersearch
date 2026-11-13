import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { useScheduleWrite } from '@/hooks/use-schedule-write'
import { ApiError } from '@/lib/api'
import type { ScheduleConflict } from '@/lib/barber-schedule'
import { problemDetails } from '@/test/msw/handlers'

const CONFLICT: ScheduleConflict = {
  id: '9e100000-0000-4000-8000-000000000031',
  startAt: '2026-11-20T09:00:00+00:00',
  endAt: '2026-11-20T09:30:00+00:00',
  timeZoneId: 'Asia/Yekaterinburg',
  customerName: 'Иван Петров',
  serviceName: 'Стрижка',
  status: 'Confirmed',
}

const SECOND_CONFLICT: ScheduleConflict = {
  ...CONFLICT,
  id: '9e100000-0000-4000-8000-000000000032',
  startAt: '2026-11-20T10:00:00+00:00',
  endAt: '2026-11-20T10:30:00+00:00',
  customerName: 'Пётр Сидоров',
}

function conflictError(conflicts: unknown = [CONFLICT]): ApiError {
  return new ApiError(
    409,
    {
      ...problemDetails(409, { errorCodes: ['barberSchedule.conflict'] }),
      conflicts,
    },
    'Conflict.'
  )
}

function serverError(): ApiError {
  return new ApiError(
    500,
    problemDetails(500, { detail: 'Internal error.' }),
    'Internal error.'
  )
}

function setup() {
  const onSaved = vi.fn()
  const rendered = renderHook(() =>
    useScheduleWrite('hours', {
      fallback: 'Не удалось сохранить расписание.',
      onSaved,
    })
  )

  return { ...rendered, onSaved }
}

type Send = (force: boolean) => Promise<void>

describe('useScheduleWrite', () => {
  it('сохраняет с первой попытки и объявляет успех', async () => {
    const { result, onSaved } = setup()
    const send = vi.fn<Send>().mockResolvedValue(undefined)

    await act(async () => {
      await result.current.save(send)
    })

    expect(send).toHaveBeenCalledTimes(1)
    expect(send).toHaveBeenCalledWith(false)
    expect(onSaved).toHaveBeenCalledTimes(1)
    expect(result.current.conflicts).toBeNull()
    expect(result.current.error).toBeNull()
    expect(result.current.isSaving).toBe(false)
  })

  it('на 409 открывает диалог и не пишет в error', async () => {
    const { result, onSaved } = setup()
    const send = vi.fn<Send>().mockRejectedValue(conflictError())

    await act(async () => {
      await result.current.save(send)
    })

    expect(result.current.conflicts).toStrictEqual([CONFLICT])
    expect(result.current.error).toBeNull()
    expect(result.current.isSaving).toBe(false)
    expect(onSaved).not.toHaveBeenCalled()
  })

  it('confirm повторяет последний запрос с force=true', async () => {
    const { result, onSaved } = setup()
    const send = vi
      .fn<Send>()
      .mockRejectedValueOnce(conflictError())
      .mockResolvedValueOnce(undefined)

    await act(async () => {
      await result.current.save(send)
    })
    await act(async () => {
      await result.current.confirm()
    })

    expect(send).toHaveBeenCalledTimes(2)
    expect(send.mock.calls[0]).toStrictEqual([false])
    expect(send.mock.calls[1]).toStrictEqual([true])
    expect(onSaved).toHaveBeenCalledTimes(1)
    expect(result.current.conflicts).toBeNull()
    expect(result.current.error).toBeNull()
  })

  it('повторный 409 обновляет список и оставляет диалог открытым', async () => {
    const { result } = setup()
    const send = vi
      .fn<Send>()
      .mockRejectedValueOnce(conflictError())
      .mockRejectedValueOnce(conflictError([SECOND_CONFLICT]))

    await act(async () => {
      await result.current.save(send)
    })
    await act(async () => {
      await result.current.confirm()
    })

    expect(result.current.conflicts).toStrictEqual([SECOND_CONFLICT])
    expect(result.current.error).toBeNull()
    expect(result.current.isSaving).toBe(false)
  })

  it('на 500 показывает ошибку в карточке и закрывает диалог', async () => {
    const { result } = setup()
    const send = vi
      .fn<Send>()
      .mockRejectedValueOnce(conflictError())
      .mockRejectedValueOnce(serverError())

    await act(async () => {
      await result.current.save(send)
    })
    await act(async () => {
      await result.current.confirm()
    })

    expect(result.current.conflicts).toBeNull()
    expect(result.current.error).toBe('Не удалось сохранить расписание.')
  })

  it('на 409 с нечитаемым телом объясняет, что данные изменились', async () => {
    const { result } = setup()
    const send = vi
      .fn<Send>()
      .mockRejectedValue(conflictError({ items: [CONFLICT] }))

    await act(async () => {
      await result.current.save(send)
    })

    expect(result.current.conflicts).toBeNull()
    expect(result.current.error).toBe(
      'Расписание не сохранено: записи изменились, обновите страницу.'
    )
  })

  it('cancel закрывает диалог и ничего не отправляет', async () => {
    const { result } = setup()
    const send = vi.fn<Send>().mockRejectedValue(conflictError())

    await act(async () => {
      await result.current.save(send)
    })
    act(() => {
      result.current.cancel()
    })

    expect(result.current.conflicts).toBeNull()
    expect(send).toHaveBeenCalledTimes(1)
  })

  it('clearError сбрасывает текст ошибки', async () => {
    const { result } = setup()
    const send = vi.fn<Send>().mockRejectedValue(serverError())

    await act(async () => {
      await result.current.save(send)
    })

    expect(result.current.error).not.toBeNull()

    act(() => {
      result.current.clearError()
    })

    expect(result.current.error).toBeNull()
  })

  it('не отправляет второй запрос, пока первый ещё идёт', async () => {
    const { result } = setup()
    let resolveFirst: () => void = () => {}
    const send = vi.fn<Send>(
      () =>
        new Promise<void>((resolve) => {
          resolveFirst = resolve
        })
    )

    let first: Promise<void> = Promise.resolve()

    act(() => {
      first = result.current.save(send)
    })

    await act(async () => {
      await result.current.confirm()
    })

    expect(send).toHaveBeenCalledTimes(1)

    await act(async () => {
      resolveFirst()
      await first
    })
  })
})
