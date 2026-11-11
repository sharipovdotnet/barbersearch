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
  /**
   * Сырой URL, даже если фото скрыто администратором: `save()` отправляет его
   * обратно в `PUT users/{id}/profile`, и `null` стёр бы фото при сохранении имени.
   * Рендерить изображение можно только при `isAvatarBlocked === false`.
   */
  avatarUrl: string | null
  /** Фото скрыто администратором: файл и URL остаются, публично его не видно. */
  isAvatarBlocked: boolean
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
  /**
   * `PUT` заменяет поле целиком, поэтому `save()` возвращает текущий URL,
   * в том числе только что загруженный карточкой аватара: иначе имя стёрло бы фото.
   */
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
