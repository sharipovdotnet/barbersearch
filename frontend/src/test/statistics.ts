import type {
  AdminStatistics,
  AdminStatisticsSeriesPoint,
} from '@/lib/admin-statistics'

const DAY_MS = 24 * 60 * 60 * 1000

function daysBetween(from: string, to: string): number {
  return (
    Math.round(
      (new Date(`${to}T00:00:00Z`).getTime() -
        new Date(`${from}T00:00:00Z`).getTime()) /
        DAY_MS
    ) + 1
  )
}

function series(from: string, to: string): AdminStatisticsSeriesPoint[] {
  const days = daysBetween(from, to)
  const points: AdminStatisticsSeriesPoint[] = []

  for (let index = 0; index < days; index += 1) {
    const date = new Date(new Date(`${from}T00:00:00Z`).getTime() + index * DAY_MS)
      .toISOString()
      .slice(0, 10)

    points.push({
      date,
      created: index === 0 ? 12 : 0,
      completed: index === 0 ? 4 : 0,
      cancelled: 0,
      noShow: 0,
    })
  }

  return points
}

/** Ответ статистики для тестов: полный контракт и dense-ряд под период. */
export function statisticsFixture(
  from = '2026-10-10',
  to = '2026-11-08',
  withPrevious = false
): AdminStatistics {
  return {
    period: {
      from,
      to,
      days: daysBetween(from, to),
      timeZone: 'UTC',
      previous: withPrevious ? { from: '2026-09-10', to: '2026-10-09' } : null,
    },
    generatedAt: '2026-11-08T09:30:00+00:00',
    overview: {
      completedVisits: { value: 128, previous: 114 },
      turnover: { value: 284500, previous: 263000, currency: 'RUB' },
      averageCheck: { value: 2222.66, previous: 2307.02 },
      newAccounts: { value: 42, previous: 37 },
      noShowRate: { value: 0.047, previous: 0.055, count: 6, sample: 134 },
      stuckBookings: { value: 7 },
    },
    series: { granularity: 'day', points: series(from, to) },
    statuses: {
      pending: 2,
      confirmed: 5,
      completed: 128,
      cancelled: 9,
      noShow: 6,
      due: 150,
      closed: 143,
    },
    clients: {
      withVisit: 96,
      returning: 54,
      firstVisit: 42,
      guestBookings: 31,
      guestShare: 0.18,
      reviewRate: 0.41,
    },
    supply: {
      activeBarbers: 210,
      workingBarbers: 64,
      idleBarbers: 146,
      newProfiles: 5,
      salonsByKind: [{ kind: 'Salon', listed: 30, unlisted: 0 }],
    },
    top: {
      barbers: {
        rows: [
          {
            barberProfileId: '9e800000-0000-4000-8000-000000000001',
            displayName: 'Артём Соколов',
            visits: 20,
            turnover: 50000,
            averageCheck: 2500,
            noShow: 1,
            rating: 4.8,
            reviewsCount: 12,
          },
        ],
        other: { visits: 30, turnover: 70000 },
      },
      services: {
        rows: [
          {
            hairServiceId: '9e800000-0000-4000-8000-000000000002',
            name: 'Fade',
            visits: 10,
            turnover: 25000,
            averageCheck: 2500,
          },
        ],
        other: { visits: 8, turnover: 20000 },
      },
      cities: {
        rows: [
          {
            name: 'Москва',
            visits: 12,
            turnover: 30000,
            averageCheck: 2500,
            activeBarbers: 7,
          },
        ],
        other: { visits: 6, turnover: 15000 },
      },
    },
    quality: {
      rating: 4.72,
      reviews: 38,
      distribution: [0, 1, 2, 5, 30],
      publishedShare: 0.9,
      requests: {
        submitted: 4,
        approved: 2,
        rejected: 1,
        approvalRate: 0.67,
        medianDecisionHours: 26.5,
      },
      salons: {
        submitted: 3,
        approved: 1,
        rejected: 1,
        approvalRate: 0.5,
        medianDecisionHours: 48,
      },
    },
  }
}
