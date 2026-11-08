import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { ImportSkipped } from '@/components/barber/import-skipped'

/**
 * Дата пропущенного события показывается в поясе салона: вечерний UTC-инстант
 * уже относится к следующему дню в Екатеринбурге и не должен выглядеть вчерашним.
 */
describe('ImportSkipped, дата события', () => {
  it('показывает календарный день в поясе салона, а не в UTC', () => {
    render(
      <ImportSkipped
        timeZoneId="Asia/Yekaterinburg"
        skipped={[
          {
            reason: 'past',
            summary: 'Прошедший визит',
            startAt: '2026-11-12T23:30:00.000Z',
          },
        ]}
      />
    )

    expect(screen.getByText(/13\s+нояб/)).toBeInTheDocument()
    expect(screen.queryByText(/12\s+нояб/)).not.toBeInTheDocument()
  })
})
