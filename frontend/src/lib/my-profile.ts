import { apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { getInitials } from '@/lib/format'
import { isMyProfile } from '@/lib/response-guards'

/**
 * Профиль текущего пользователя для кабинета.
 *
 * `GET api/users/me` — единственное чтение своих данных: `GET api/auth/me` строится из
 * claims и не знает имени, телефона и подтверждений. Ответ приходит из базы, поэтому
 * после `PUT api/users/{id}/profile` кабинет показывает актуальные значения.
 */

export type MyProfileRole = 'Customer' | 'Barber' | 'Admin'

export type MyProfile = {
  id: string
  email: string
  firstName: string
  lastName: string
  phoneNumber: string | null
  avatarUrl: string | null
  role: MyProfileRole
  barberProfileId: string | null
  isEmailVerified: boolean
  isPhoneVerified: boolean
  createdAt: string
  lastLoginAt: string | null
}

export type UpdateMyProfileInput = {
  firstName: string
  lastName: string
  phoneNumber: string | null
  /** Аватар пока не редактируется, но `PUT` заменяет поле целиком: его нужно вернуть. */
  avatarUrl: string | null
}

/** Русские подписи ролей: значения из API остаются английскими. */
export const ACCOUNT_ROLE_LABELS: Record<MyProfileRole, string> = {
  Customer: 'Клиент',
  Barber: 'Барбер',
  Admin: 'Администратор',
}

/** Полное имя, а до загрузки профиля — email: кабинет не должен показывать пустоту. */
export function profileDisplayName(
  profile: Pick<MyProfile, 'firstName' | 'lastName' | 'email'>
): string {
  const name = `${profile.firstName} ${profile.lastName}`.trim()

  return name || profile.email
}

/** Инициалы для аватара; у email берётся первая буква. */
export function profileInitials(
  profile: Pick<MyProfile, 'firstName' | 'lastName' | 'email'>
): string {
  const name = `${profile.firstName} ${profile.lastName}`.trim()

  return name ? getInitials(name) : profile.email.slice(0, 1).toUpperCase()
}

export async function fetchMyProfile(signal?: AbortSignal): Promise<MyProfile> {
  return apiRequest<MyProfile>('/users/me', { signal }, { validate: isMyProfile })
}

/**
 * Сохраняет личные данные. `204` — тело не возвращается, поэтому guard не нужен.
 *
 * `avatarUrl` отправляется как есть: поле nullable, и `null` очистил бы уже
 * установленный аватар.
 */
export async function updateMyProfile(
  id: string,
  input: UpdateMyProfileInput
): Promise<void> {
  await mutateWithCsrf<void>(`/users/${id}/profile`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}
