import { describe, expect, it } from 'vitest'

import {
  BARBER_CABINET_PATH,
  BARBER_IMPORT_PATH,
  BARBER_NOTIFICATIONS_PATH,
  BARBER_ONBOARDING_PATH,
  BARBER_SALONS_PATH,
  BARBER_SCHEDULE_PATH,
  isBarberCabinetPath,
  isBarberImportPath,
  isBarberNotificationsPath,
  isBarberOnboardingPath,
  isBarberSalonsPath,
  isBarberSchedulePath,
} from '@/lib/barber-cabinet-path'

describe('адреса кабинета барбера', () => {
  it('не меняет прежние разделы и адреса-шаги', () => {
    expect(BARBER_CABINET_PATH).toBe('/barber')
    expect(BARBER_SCHEDULE_PATH).toBe('/barber/schedule')
    expect(BARBER_SALONS_PATH).toBe('/barber/salons')
    expect(BARBER_ONBOARDING_PATH).toBe('/barber/onboarding')
    expect(BARBER_IMPORT_PATH).toBe('/barber/import')
  })

  it('уведомления — отдельный адрес, а не четвёртый раздел навигации', () => {
    expect(BARBER_NOTIFICATIONS_PATH).toBe('/barber/notifications')
    expect(isBarberNotificationsPath('/barber/notifications')).toBe(true)
    expect(isBarberNotificationsPath('/barber/notifications/')).toBe(false)
    expect(isBarberNotificationsPath('/barber')).toBe(false)
    expect(isBarberNotificationsPath('/barber/notifications/42')).toBe(false)
  })

  it('общий предикат кабинета по-прежнему знает только два раздела', () => {
    expect(isBarberCabinetPath('/barber')).toBe(true)
    expect(isBarberCabinetPath('/barber/schedule')).toBe(true)
    expect(isBarberCabinetPath('/barber/notifications')).toBe(false)
    expect(isBarberCabinetPath('/barber/salons')).toBe(false)
  })

  it('остальные предикаты не перехватывают уведомления', () => {
    expect(isBarberSchedulePath('/barber/notifications')).toBe(false)
    expect(isBarberSalonsPath('/barber/notifications')).toBe(false)
    expect(isBarberOnboardingPath('/barber/notifications')).toBe(false)
    expect(isBarberImportPath('/barber/notifications')).toBe(false)
  })
})
