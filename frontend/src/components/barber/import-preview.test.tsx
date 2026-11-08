import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ImportPreview } from '@/components/barber/import-preview'
import type { ImportRow, ImportSummary } from '@/hooks/use-barber-import'
import type { BarberBookingOptions } from '@/lib/barber-booking-options'
import type { ImportDraft } from '@/lib/import-parse'

/**
 * Предпросмотр календаря: галочка «Перенести», раскрытие полей «Изменить»,
 * выбор всех строк и предупреждение о лимите. Текстовый источник не меняется.
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
    status: 'ready',
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
    ready: 1,
    created: 0,
    failed: 0,
    missingService: 0,
    missingPhone: 0,
    past: 0,
    skipped: 0,
    ...overrides,
  }
}

function renderPreview({
  rows = [row()],
  selectionNotice = null,
  onToggle = vi.fn(),
  onToggleAll = vi.fn(),
}: {
  rows?: ImportRow[]
  selectionNotice?: string | null
  onToggle?: (line: number) => void
  onToggleAll?: (included: boolean) => void
} = {}) {
  render(
    <ImportPreview
      rows={rows}
      summary={summary()}
      options={OPTIONS}
      source="ics"
      selectionNotice={selectionNotice}
      isRunning={false}
      progress={null}
      onUpdate={vi.fn()}
      onToggle={onToggle}
      onToggleAll={onToggleAll}
      onCreate={vi.fn()}
      onBack={vi.fn()}
    />
  )

  return { onToggle, onToggleAll }
}

describe('ImportPreview, календарь', () => {
  it('раскрывает поля строки одной кнопкой «Изменить» и сворачивает их обратно', async () => {
    const user = userEvent.setup()

    renderPreview()

    const edit = screen.getByRole('button', { name: 'Изменить' })

    expect(edit).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByLabelText('Имя, строка 1')).not.toBeInTheDocument()

    await user.click(edit)

    expect(screen.getAllByRole('button', { name: 'Изменить' })).toHaveLength(1)
    expect(edit).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByLabelText('Имя, строка 1')).toHaveValue('Иван')
    expect(screen.getByLabelText('Телефон, строка 1')).toHaveValue(
      '+79991234567'
    )
    expect(screen.getByLabelText('Услуга, строка 1')).toBeInTheDocument()
    expect(screen.getByLabelText('Дата, строка 1')).toBeInTheDocument()
    expect(screen.getByLabelText('Время, строка 1')).toBeInTheDocument()
    expect(screen.getByLabelText('Салон, строка 1')).toBeInTheDocument()

    await user.click(edit)

    expect(edit).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getAllByRole('button', { name: 'Изменить' })).toHaveLength(1)
    expect(screen.queryByLabelText('Имя, строка 1')).not.toBeInTheDocument()
  })

  it('у созданной строки нет «Изменить», а её галочка выключена', () => {
    renderPreview({ rows: [row({ status: 'created' })] })

    expect(
      screen.queryByRole('button', { name: 'Изменить' })
    ).not.toBeInTheDocument()
    expect(
      screen.getByRole('checkbox', {
        name: 'Перенести событие «Стрижка — Иван Петров»',
      })
    ).toHaveAttribute('aria-disabled', 'true')
  })

  it('галочка строки называет событие и переключает строку', async () => {
    const user = userEvent.setup()
    const onToggle = vi.fn()

    renderPreview({ onToggle })

    await user.click(
      screen.getByRole('checkbox', {
        name: 'Перенести событие «Стрижка — Иван Петров»',
      })
    )

    expect(onToggle).toHaveBeenCalledWith(1)
  })

  it('«Выбрать все» и «Снять все» передают флаг выбора', async () => {
    const user = userEvent.setup()
    const onToggleAll = vi.fn()

    renderPreview({ onToggleAll })

    await user.click(screen.getByRole('button', { name: 'Выбрать все' }))
    await user.click(screen.getByRole('button', { name: 'Снять все' }))

    expect(onToggleAll.mock.calls).toStrictEqual([[true], [false]])
  })

  it('показывает предупреждение о лимите в role="status"', () => {
    renderPreview({
      selectionNotice: 'За один раз — не больше 50 записей.',
    })

    expect(
      screen
        .getAllByRole('status')
        .some((region) =>
          region.textContent?.includes('За один раз — не больше 50 записей.')
        )
    ).toBe(true)
  })
})
