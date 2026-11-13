import { useState } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ScheduleConflictsDialog } from '@/components/barber/schedule-conflicts-dialog'
import type {
  ScheduleConflict,
  ScheduleWriteKind,
} from '@/lib/barber-schedule'
import { recordRequests } from '@/test/msw/requests'

function conflict(index: number): ScheduleConflict {
  const minute = String(index - 1).padStart(2, '0')

  return {
    id: `9e100000-0000-4000-8000-${String(index).padStart(12, '0')}`,
    startAt: `2026-11-20T09:${minute}:00+00:00`,
    endAt: `2026-11-20T09:${minute}:30+00:00`,
    timeZoneId: 'Asia/Yekaterinburg',
    customerName: `Клиент ${index}`,
    serviceName: 'Стрижка',
    status: 'Confirmed',
  }
}

function renderDialog(
  conflicts: readonly ScheduleConflict[],
  options: {
    kind?: ScheduleWriteKind
    isSaving?: boolean
    error?: string | null
  } = {}
) {
  const onCancel = vi.fn()
  const onConfirm = vi.fn()

  render(
    <ScheduleConflictsDialog
      open
      kind={options.kind ?? 'hours'}
      conflicts={conflicts}
      isSaving={options.isSaving ?? false}
      error={options.error ?? null}
      onCancel={onCancel}
      onConfirm={onConfirm}
    />
  )

  return { onCancel, onConfirm }
}

describe('ScheduleConflictsDialog', () => {
  it('заголовок и описание зависят от вида правки', () => {
    const { unmount } = render(
      <ScheduleConflictsDialog
        open
        kind="hours"
        conflicts={[conflict(1), conflict(2), conflict(3)]}
        isSaving={false}
        error={null}
        onCancel={() => {}}
        onConfirm={() => {}}
      />
    )

    expect(
      screen.getByRole('alertdialog', { name: 'Записи вне новых часов' })
    ).toBeInTheDocument()
    expect(screen.getByText(/^3 записи\./)).toBeInTheDocument()

    unmount()

    render(
      <ScheduleConflictsDialog
        open
        kind="timeOff"
        conflicts={[conflict(1)]}
        isSaving={false}
        error={null}
        onCancel={() => {}}
        onConfirm={() => {}}
      />
    )

    expect(
      screen.getByRole('alertdialog', { name: 'Записи в нерабочем периоде' })
    ).toBeInTheDocument()
    expect(screen.getByText(/^1 запись\./)).toBeInTheDocument()
  })

  it('показывает 20 визитов и «и ещё N» для остальных', () => {
    const conflicts = Array.from({ length: 21 }, (_, index) => conflict(index + 1))

    renderDialog(conflicts)

    expect(screen.getAllByRole('listitem')).toHaveLength(20)
    expect(screen.getByText('и ещё 1 запись')).toBeInTheDocument()
    expect(screen.queryByText(/Клиент 21/)).not.toBeInTheDocument()
  })

  it('показывает время в поясе конфликта, а не браузера', () => {
    renderDialog([conflict(1)])

    // 09:00 UTC — это 14:00 в Екатеринбурге (+05:00).
    expect(screen.getByText(/20 ноября в 14:00/)).toBeInTheDocument()
    expect(screen.getByText(/Клиент 1/)).toBeInTheDocument()
  })

  it('«Отмена» закрывает диалог, ничего не подтверждая и не запрашивая', async () => {
    const requests = recordRequests()
    let confirmed = 0

    function Harness() {
      const [open, setOpen] = useState(true)

      return (
        <ScheduleConflictsDialog
          open={open}
          kind="hours"
          conflicts={[conflict(1)]}
          isSaving={false}
          error={null}
          onCancel={() => {
            setOpen(false)
          }}
          onConfirm={() => {
            confirmed += 1
          }}
        />
      )
    }

    render(<Harness />)

    await userEvent.click(screen.getByRole('button', { name: 'Отмена' }))

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    })

    expect(confirmed).toBe(0)
    expect(requests).toStrictEqual([])
  })

  it('«Всё равно сохранить» — не destructive и подтверждает', async () => {
    const { onConfirm, onCancel } = renderDialog([conflict(1)])
    const confirm = screen.getByRole('button', { name: 'Всё равно сохранить' })

    expect(confirm).not.toHaveClass('destructive')

    await userEvent.click(confirm)

    expect(onConfirm).toHaveBeenCalledTimes(1)
    expect(onCancel).not.toHaveBeenCalled()
  })

  it('во время повтора показывает «Сохраняем…» и блокирует кнопки', () => {
    renderDialog([conflict(1)], { isSaving: true })

    expect(
      screen.getByRole('button', { name: 'Сохраняем…' })
    ).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Отмена' })).toBeDisabled()
  })

  it('показывает ошибку повтора внутри диалога', () => {
    renderDialog([conflict(1)], {
      error: 'Расписание не сохранено: попробуйте ещё раз.',
    })

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Расписание не сохранено: попробуйте ещё раз.'
    )
  })
})
