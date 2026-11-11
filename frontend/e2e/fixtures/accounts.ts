import { fileURLToPath } from 'node:url'

/**
 * Учётные записи и id фикстур E2E.
 *
 * Значения по умолчанию совпадают с `e2e/fixtures/seed.sql`; переопределяются
 * переменными `E2E_*` (CI подставляет свои, но имена те же).
 */
export const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5173'

export const roles = [
  'admin',
  'barber',
  'profileBarber',
  'newBarber',
  'customer',
  'mutations',
  'applicant',
] as const

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
  // Отдельный барбер со своим подтверждённым профилем: экран `/barber/profile`
  // правит карточку, услуги и специализации, поэтому мутации не должны трогать
  // витринного барбера и его брони. Сид держит прайс и специализации пустыми.
  profileBarber: {
    email:
      process.env.E2E_PROFILE_BARBER_EMAIL ??
      'e2e-profile-barber@barbersearch.test',
    password: process.env.E2E_PROFILE_BARBER_PASSWORD ?? 'e2e-password',
  },
  // Барбер с ролью, но без профиля: онбординг `/barber/onboarding` создаёт его
  // впервые. Сид каждый раз удаляет профиль, созданный прошлым прогоном.
  newBarber: {
    email: process.env.E2E_NEW_BARBER_EMAIL ?? 'e2e-new-barber@barbersearch.test',
    password: process.env.E2E_NEW_BARBER_PASSWORD ?? 'e2e-password',
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
  // Клиент без заявки «Стать барбером»: мутирующий спек подаёт её с нуля, поэтому
  // сид удаляет заявки аккаунта и возвращает ему роль Customer.
  applicant: {
    email: process.env.E2E_APPLICANT_EMAIL ?? 'e2e-applicant@barbersearch.test',
    password: process.env.E2E_APPLICANT_PASSWORD ?? 'e2e-password',
  },
}

/** Id из seed: витринный барбер, его услуга и салон. */
export const fixtures = {
  barberProfileId: '9e100000-0000-4000-8000-000000000001',
  barberServiceId: '9e300000-0000-4000-8000-000000000001',
  salonId: '9e400000-0000-4000-8000-000000000001',
  /**
   * Изолированный профиль `/barber/profile`: подтверждён и активен, поэтому
   * публичная карточка `/barbers/{id}` доступна, но услуг, специализаций и
   * броней у него нет — счётчики витрины он не двигает. Мутирует только он.
   */
  profileBarber: {
    profileId: '9e100000-0000-4000-8000-000000000015',
    userId: '9e000000-0000-4000-8000-000000000115',
  },
  /** Барбер B2: ближайшая запись клиента и восстановление после её отмены. */
  customerBarberProfileId: '9e100000-0000-4000-8000-000000000002',
  customerBarberServiceId: '9e300000-0000-4000-8000-000000000002',
  /**
   * Клиент мутирующих сцен (`e2e-mutations@…`): его карточка в `/admin/users`
   * показывает аватар, который `account-avatar-mutations.spec.ts` только что загрузил.
   */
  mutationsUserId: '9e000000-0000-4000-8000-000000000004',
  city: 'Москва',
  /** 12 опубликованных отзывов витрины и 21 предстоящая запись кабинета. */
  publicReviews: { first: 10, total: 12 },
  upcomingBookings: 21,
  /**
   * 52 записи истории витрины плюс 24 админских брони на 2026-09-12: они уже
   * закончились, поэтому попадают в историю кабинета, хотя их статусы Pending
   * и Confirmed (скоуп `past` смотрит на `end_at`, а не на статус).
   */
  historyBookings: 76,
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
  /** Активный салон без привязок: мутирующий спек «Мои салоны» привязывает его и откатывает. */
  attachSalonId: '9e400000-0000-4000-8000-000000000003',
  attachSalonName: 'E2E Studio South',
  /** Подписи строк, на которые опираются спеки админского каталога. */
  catalogueLabels: {
    activeHairService: 'E2E Стрижка',
    hiddenHairService: 'E2E Борода',
    hiddenSpecialization: 'E2E Фейд',
    activeSalon: 'E2E Studio',
    hiddenSalon: 'E2E Studio Nord',
  },
  /**
   * Число барберов фикстур с **активной услугой** `E2E Стрижка`: её `usageCount`
   * в админском каталоге. Изолированный `profileBarber` услуг не имеет, поэтому
   * счётчик остаётся 13; счётчик салона 0001 этим профилем не проверяется.
   */
  barbersUsingActiveCatalogueRows: 13,
  /**
   * Салон сценария #48: барбер создаёт его через `POST /api/salons/own`, админ
   * одобряет через каталог. Удаления салона в API нет, поэтому `seed.sql` каждый
   * раз убирает строки с `owner_user_id` фикстур (связи и аудит — тоже).
   * Название уникально, чтобы строка не путалась с админскими салонами.
   */
  ownSalon: {
    name: 'E2E Studio Проверка',
    address: 'ул. Проверочная, 5',
    city: 'Москва',
    country: 'Russia',
    timeZoneId: 'Europe/Moscow',
    latitude: 55.75,
    longitude: 37.62,
    phoneNumber: '+70000000055',
    websiteUrl: 'https://e2e-salon.example',
  },
  /**
   * Админская сводка `/admin/summary` на пересеянной базе: 14 неопубликованных
   * отзывов (12 скрытых мутаций + 2 модерации), 1 профиль без подтверждения
   * (B14, секция 4.1 сида) и 1 выключенный салон (`E2E Studio Nord`).
   *
   * Числа верны только сразу после `seed.sql`: мутирующие спеки правят отзывы.
   */
  summary: {
    pendingReviews: 14,
    unverifiedBarbers: 1,
    inactiveSalons: 1,
  },
  /**
   * Журнал `/admin/audit`: 25 строк на 2026-09-10 UTC (страница «1–20» + «21–25»).
   * `roleChangeRowId` — строка 20 с diff'ом «Роль: Клиент → Администратор»,
   * `systemRowId` — самая свежая строка 21 без автора («Система»).
   * `salonRowId`/`hairServiceRowId`/`specializationRowId`/`bookingRowId` — строки
   * новых типов #41 (22…25) для фильтров и русских diff'ов.
   */
  journal: {
    day: '2026-09-10',
    pageSize: 20,
    total: 25,
    roleChangeRowId: '9e700000-0000-4000-8000-000000000020',
    systemRowId: '9e700000-0000-4000-8000-000000000021',
    salonRowId: '9e700000-0000-4000-8000-000000000022',
    hairServiceRowId: '9e700000-0000-4000-8000-000000000023',
    specializationRowId: '9e700000-0000-4000-8000-000000000024',
    bookingRowId: '9e700000-0000-4000-8000-000000000025',
  },
  /**
   * Брони `/admin/bookings`: отдельный клиент и 24 строки на 2026-09-12 UTC
   * (`seed.sql`, секция 5.7). Поиск по `email` даёт ровно этот набор, поэтому
   * счётчики не зависят от броней остальных фикстур.
   *
   * `total` = первая страница `pageSize` + вторая `secondPage` (20 + 4).
   * `cancelId` (NN 01, Pending) и `noShowId` (NN 19, Confirmed с началом
   * в прошлом) — строки мутирующего спека. `latestId` (NN 24, NoShow) — самая
   * поздняя бронь, то есть первая строка списка: на ней проверяются порядок,
   * пояс салона и выбранная карточка для axe. `untouchedId` (NN 20, Confirmed)
   * нужен сцене «Оставить как есть»: тесты одного файла идут параллельно,
   * поэтому она не делит бронь с отменой и неявкой.
   */
  adminBookings: {
    email: 'e2e-bookings@barbersearch.test',
    pageSize: 20,
    total: 24,
    secondPage: 4,
    pending: 18,
    confirmed: 3,
    completed: 1,
    cancelled: 1,
    noShow: 1,
    cancelId: '9e900000-0000-4000-8000-000000000001',
    noShowId: '9e900000-0000-4000-8000-000000000019',
    untouchedId: '9e900000-0000-4000-8000-000000000020',
    latestId: '9e900000-0000-4000-8000-000000000024',
  },
  /**
   * Заявки «Стать барбером» (#46). Аккаунт `e2e-applicant@…` приходит из сида без
   * заявки и с ролью `Customer`; спек подаёт заявку, админ отклоняет её с этой
   * причиной, а заявитель читает её в своём статусе.
   */
  barberRequest: {
    applicantEmail: 'e2e-applicant@barbersearch.test',
    displayName: 'E2E Заявитель',
    yearsOfExperience: 6,
    city: 'Москва',
    rejectionReason: 'E2E: не хватает подтверждённого опыта',
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
