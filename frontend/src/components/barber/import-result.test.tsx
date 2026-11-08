import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ImportResult } from '@/components/barber/import-result'
import type { ImportRow, ImportSummary } from '@/hooks/use-barber-import'
import type { BarberBookingOptions } from '@/lib/barber-booking-options'
import type { ImportDraft } from '@/lib/import-parse'

/**
 * Отчёт о переносе календаря описывает текущую пачку, а не накопленный итог.
 *
 * После «Перенести ещё» созданные строки прошлой пачки сняты с выбора, поэтому
 * `summary.created` (он кумулятивный) не должен попадать в текст отчёта.
 */

const OPTIONS: BarberBookingOptions = {
  services: [
    {
      barberServiceId: 'service-haircut',
      hairServiceId: 'hair-haircut',
      name: 'Стрижка',
      category: 'Haircut',
      defaultDurationMinutes: 60,
      durationMinutes: 60,
      price: 2000,
      isActive: true,
    },
  ],
  salons: [
    {
      id: 'profile-salon-s1',
      salonId: 's1',
      name: 'Салон на Ленина',
      address: 'Ленина 1',
      city: 'Екатеринбург',
      country: 'Russia',
      timeZoneId: 'Asia/Yekaterinburg',
      kind: 'Salon',
      isPrimary: true,
      workingSchedules: [],
    },
  ],
}

function draft(overrides: Partial<ImportDraft> = {}): ImportDraft {
  return {
    line: 1,
    raw: 'Стрижка — Иван Петров',
    date: '2026-11-12',
    time: '10:00',
    startAt: '2026-11-12T05:00:00.000Z',
    endAt: '2026-11-12T06:00:00.000Z',
    firstName: 'Иван',
    lastName: 'Петров',
    phone: '+79991234567',
    salonId: 's1',
    serviceId: 'service-haircut',
    serviceCandidates: [],
    issues: [],
    source: 'ics',
    externalUid: 'uid-1',
    recurrenceId: null,
    eventSummary: 'Стрижка — Иван Петров',
    eventStartAt: '2026-11-12T05:00:00.000Z',
    eventEndAt: '2026-11-12T06:00:00.000Z',
    eventDurationMinutes: 60,
    useEventTime: true,
    sourceTimeZone: 'Asia/Yekaterinburg',
    hints: [],
    defaultIncluded: true,
    ...overrides,
  }
}

function row(overrides: Partial<ImportRow> = {}): ImportRow {
  return {
    draft: draft(),
    idempotencyKey: 'ics:0123456789abcdef0123456789abcdef',
    status: 'created',
    failure: null,
    overlap: false,
    slots: null,
    slotsStatus: 'idle',
    included: true,
    ...overrides,
  }
}

function summary(overrides: Partial<ImportSummary> = {}): ImportSummary {
  return {
    total: 1,
    selected: 1,
    ready: 0,
    created: 1,
    failed: 0,
    missingService: 0,
    missingPhone: 0,
    past: 0,
    skipped: 0,
    ...overrides,
  }
}

function result(
  rows: ImportRow[],
  source: 'ics' | 'text',
  value: ImportSummary
) {
  return (
    <ImportResult
      rows={rows}
      summary={value}
      options={OPTIONS}
      source={source}
      canContinue={false}
      isRunning={false}
      onRetryFailed={vi.fn()}
      onContinue={vi.fn()}
      onReview={vi.fn()}
      onApplySlot={vi.fn()}
      onLoadSlots={vi.fn()}
      onOpenBookings={vi.fn()}
    />
  )
}

describe('ImportResult, отчёт календаря', () => {
  it('считает созданные строки текущей пачки, а не кумулятивный summary', () => {
    const firstBatch = row({ draft: draft({ line: 1 }) })
    const { rerender } = render(result([firstBatch], 'ics', summary()))

    expect(screen.getByRole('status')).toHaveTextContent(
      'Перенесено 1 из 1 выбранных записей.'
    )

    // Следующая пачка: созданная строка прошлой пачки снята с выбора.
    const previous = row({
      draft: draft({ line: 1 }),
      included: false,
    })
    const current = row({
      draft: draft({ line: 2, firstName: 'Пётр', lastName: 'Сидоров' }),
    })

    rerender(
      result(
        [previous, current],
        'ics',
        summary({ total: 2, selected: 1, created: 2 })
      )
    )

    expect(screen.getByRole('status')).toHaveTextContent(
      'Перенесено 1 из 1 выбранных записей.'
    )
  })

  it('не включает снятую с выбора созданную строку в текущую пачку', () => {
    const created = row()
    const { rerender } = render(result([created], 'ics', summary()))

    expect(screen.getByRole('status')).toHaveTextContent(
      'Перенесено 1 из 1 выбранных записей.'
    )

    rerender(
      result(
        [{ ...created, included: false }],
        'ics',
        summary({ selected: 0 })
      )
    )

    expect(screen.getByRole('status')).toHaveTextContent(
      'Перенесено 0 из 0 выбранных записей.'
    )
  })
})

describe('ImportResult, отчёт текста', () => {
  it('сохраняет кумулятивную формулировку текстового источника', () => {
    render(result([row()], 'text', summary({ total: 3, created: 2 })))

    expect(screen.getByRole('status')).toHaveTextContent(
      'Перенесено 2 из 3 записей.'
    )
  })
})
