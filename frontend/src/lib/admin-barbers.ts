import { ApiError, apiRequest } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'

/**
 * Админское чтение барберов и словари для экрана.
 *
 * Контракт отдельный от публичной карточки: сервер отдаёт email владельца и профили,
 * которых нет в поиске. Ошибки ветвятся по `status` и `errorCodes`, английский `detail`
 * наружу не показывается.
 */

/** Грейды приходят строками — так же, как в публичной карточке. */
export type BarberGrade =
  | 'None'
  | 'Intern'
  | 'JuniorBarber'
  | 'MiddleBarber'
  | 'SeniorBarber'
  | 'ArtDirector'
  | 'BarberSchoolTrainer'
  | 'Manager'
  | 'CompetitionBarber'
  | 'BrandAmbassador'
  | 'BarbershopOwner'

/** Русские подписи грейдов: значения из API остаются английскими. */
export const BARBER_GRADE_LABELS: Record<BarberGrade, string> = {
  None: 'Без грейда',
  Intern: 'Стажёр',
  JuniorBarber: 'Младший барбер',
  MiddleBarber: 'Барбер',
  SeniorBarber: 'Старший барбер',
  ArtDirector: 'Арт-директор',
  BarberSchoolTrainer: 'Тренер школы',
  Manager: 'Управляющий',
  CompetitionBarber: 'Соревновательный барбер',
  BrandAmbassador: 'Амбассадор бренда',
  BarbershopOwner: 'Владелец барбершопа',
}

/** Порядок в выборе грейда: от младшего к старшему, как в домене. */
export const BARBER_GRADES = Object.keys(
  BARBER_GRADE_LABELS
) as readonly BarberGrade[]

export type AdminBarberStatusFilter = 'all' | 'pending' | 'hidden'

export const ADMIN_BARBER_STATUS_LABELS: Record<
  AdminBarberStatusFilter,
  string
> = {
  all: 'Все',
  pending: 'На проверке',
  hidden: 'Скрытые',
}

export type AdminBarber = {
  id: string
  displayName: string
  email: string
  grade: BarberGrade
  rating: number | null
  reviewsCount: number
  isActive: boolean
  isVerified: boolean
  createdAt: string
  updatedAt: string
}

/** Чек-лист готовности: подсказка, а не блокировка подтверждения. */
export type AdminBarberReadiness = {
  isProfileFilled: boolean
  isEmailVerified: boolean
  servicesCount: number
  salonsCount: number
  scheduleDaysCount: number
  photosCount: number
}

export type AdminBarberDetail = {
  id: string
  userId: string
  displayName: string
  email: string
  phoneNumber: string | null
  avatarUrl: string | null
  grade: BarberGrade
  bio: string | null
  yearsOfExperience: number
  rating: number | null
  reviewsCount: number
  isActive: boolean
  isVerified: boolean
  readiness: AdminBarberReadiness
  createdAt: string
  updatedAt: string
}

export type AdminBarberPage = {
  items: AdminBarber[]
  nextCursor: string | null
}

/** Размер страницы совпадает с дефолтом сервера. */
export const ADMIN_BARBERS_PAGE_SIZE = 20

/** Максимальная длина поисковой строки на сервере; длиннее он ответит 400. */
export const ADMIN_BARBER_SEARCH_MAX_LENGTH = 100

export async function fetchAdminBarbers({
  status,
  search,
  cursor,
  signal,
}: {
  status: AdminBarberStatusFilter
  search?: string
  cursor?: string | null
  signal?: AbortSignal
}): Promise<AdminBarberPage> {
  const query = new URLSearchParams({
    status,
    pageSize: String(ADMIN_BARBERS_PAGE_SIZE),
  })

  if (search) {
    query.set('search', search)
  }

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<AdminBarberPage>(`/admin/barbers?${query.toString()}`, {
    signal,
  })
}

export async function fetchAdminBarber(
  barberId: string,
  signal?: AbortSignal
): Promise<AdminBarberDetail> {
  return apiRequest<AdminBarberDetail>(`/admin/barbers/${barberId}`, { signal })
}

/** Ошибка списка: 403 обрабатывает страница, остальное показывает текст. */
export function describeAdminBarberListError(
  error: unknown,
  fallback = 'Не удалось загрузить барберов.'
): string {
  if (error instanceof ApiError && error.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  return describeApiFailure(error, fallback)
}

/** Ошибка карточки по прямому URL. */
export function describeAdminBarberDetailError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 404 || error.hasCode('barberProfile.notFound')) {
      return 'Профиль не найден. Возможно, ссылка устарела.'
    }
  }

  return describeApiFailure(error, 'Не удалось загрузить профиль барбера.')
}

/** 401: сессия закончилась, страница предложит войти снова. */
export function isBarberUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}
