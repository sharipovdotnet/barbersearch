import { fileURLToPath } from 'node:url'

/**
 * Учётные записи и id фикстур E2E.
 *
 * Значения по умолчанию совпадают с `e2e/fixtures/seed.sql`; переопределяются
 * переменными `E2E_*` (CI подставляет свои, но имена те же).
 */
export const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5173'

export const roles = ['admin', 'barber', 'customer', 'mutations'] as const

export type Role = (typeof roles)[number]

type Account = {
  email: string
  password: string
}

export const accounts: Record<Role, Account> = {
  admin: {
    email: process.env.E2E_ADMIN_EMAIL ?? 'e2e-admin@barbersearch.test',
    password: process.env.E2E_ADMIN_PASSWORD ?? 'e2e-password',
  },
  barber: {
    email: process.env.E2E_BARBER_EMAIL ?? 'e2e-barber@barbersearch.test',
    password: process.env.E2E_BARBER_PASSWORD ?? 'e2e-password',
  },
  customer: {
    email: process.env.E2E_CUSTOMER_EMAIL ?? 'e2e-customer@barbersearch.test',
    password: process.env.E2E_CUSTOMER_PASSWORD ?? 'e2e-password',
  },
  // Отдельный клиент для мутирующих сцен: правка и удаление отзывов не должны
  // менять счётчики, которые проверяют читающие спеки.
  mutations: {
    email: process.env.E2E_MUTATIONS_EMAIL ?? 'e2e-mutations@barbersearch.test',
    password: process.env.E2E_MUTATIONS_PASSWORD ?? 'e2e-password',
  },
}

/** Id из seed: витринный барбер, его услуга и салон. */
export const fixtures = {
  barberProfileId: '9e100000-0000-4000-8000-000000000001',
  barberServiceId: '9e300000-0000-4000-8000-000000000001',
  salonId: '9e400000-0000-4000-8000-000000000001',
  /** Барбер B2: ближайшая запись клиента и восстановление после её отмены. */
  customerBarberProfileId: '9e100000-0000-4000-8000-000000000002',
  customerBarberServiceId: '9e300000-0000-4000-8000-000000000002',
  city: 'Москва',
  /** 12 опубликованных отзывов витрины и 21 предстоящая запись кабинета. */
  publicReviews: { first: 10, total: 12 },
  upcomingBookings: 21,
  historyBookings: 52,
  /** Две вкладки `/account`: по 21 строке, то есть по две страницы по 20. */
  customerUpcomingBookings: 21,
  customerHistoryBookings: 21,
  /** Активная услуга и салон витрины: их используют все 13 профилей фикстур. */
  activeHairServiceId: '9e500000-0000-4000-8000-000000000001',
  /** Выключенные строки каталога: публичные чтения их не отдают (см. `seed.sql`). */
  hiddenHairServiceId: '9e500000-0000-4000-8000-000000000002',
  hiddenSpecializationIds: {
    fade: '9e600000-0000-4000-8000-000000000001',
    beard: '9e600000-0000-4000-8000-000000000002',
  },
  hiddenSalonId: '9e400000-0000-4000-8000-000000000002',
  /** Подписи строк, на которые опираются спеки админского каталога. */
  catalogueLabels: {
    activeHairService: 'E2E Стрижка',
    hiddenHairService: 'E2E Борода',
    hiddenSpecialization: 'E2E Фейд',
    activeSalon: 'E2E Studio',
    hiddenSalon: 'E2E Studio Nord',
  },
  /** Число барберов фикстур: `usageCount` активной услуги и активного салона. */
  barbersUsingActiveCatalogueRows: 13,
  /**
   * Журнал `/admin/audit`: 21 строка на 2026-09-10 UTC (страница «1–20» + «21–21»).
   * `roleChangeRowId` — строка 20 с diff'ом «Роль: Клиент → Администратор»,
   * `systemRowId` — самая свежая строка 21 без автора («Система»).
   */
  journal: {
    day: '2026-09-10',
    pageSize: 20,
    total: 21,
    roleChangeRowId: '9e700000-0000-4000-8000-000000000020',
    systemRowId: '9e700000-0000-4000-8000-000000000021',
  },
} as const

/** Размеры страниц фронтенда: те же числа стоят в `src/lib/*`. */
export const pageSize = {
  reviews: 10,
  myReviews: 10,
  cabinet: 20,
  admin: 20,
  account: 20,
  search: 12,
} as const

/** Файл storageState роли: `e2e/.auth/<role>.json` (не коммитится). */
export function storageStatePath(role: Role): string {
  return fileURLToPath(new URL(`../.auth/${role}.json`, import.meta.url))
}
