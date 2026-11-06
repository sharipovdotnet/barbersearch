import { ACCOUNT_PATH } from '@/lib/account-path'
import { isAdminPath } from '@/lib/admin-path'
import {
  BARBER_CABINET_PATH,
  isBarberCabinetPath,
  isBarberOnboardingPath,
  isBarberSalonsPath,
} from '@/lib/barber-cabinet-path'

/**
 * Куда вести пользователя после успешного входа.
 *
 * `null` — остаться на текущей странице: так входит гость, выбирающий слот на
 * карточке барбера. `undefined` — явного возврата нет, поэтому роль открывает
 * свой рабочий раздел. `/account` — общий «Мой кабинет»: барбер и администратор
 * входят сразу в свой кабинет, а не в клиентский. Чужой закрытый раздел тоже не
 * открываем — роль уходит в свой, иначе после входа был бы экран «нет доступа».
 */
export function signInDestination(
  role: string,
  requested: string | null | undefined
): string | null {
  if (requested === null) {
    return null
  }

  const home = homePath(role)

  if (requested === undefined) {
    return home
  }

  if (requested === ACCOUNT_PATH && home !== ACCOUNT_PATH) {
    return home
  }

  if (isAdminPath(requested) && role !== 'Admin') {
    return home
  }

  if (isBarberArea(requested) && role !== 'Barber') {
    return home
  }

  return requested
}

/** Рабочий раздел роли; у клиента другого раздела нет. */
function homePath(role: string): string {
  if (role === 'Barber') {
    return BARBER_CABINET_PATH
  }

  if (role === 'Admin') {
    return '/admin'
  }

  return ACCOUNT_PATH
}

/** Все экраны, которые открываются только роли Barber. */
function isBarberArea(pathname: string): boolean {
  return (
    isBarberCabinetPath(pathname) ||
    isBarberSalonsPath(pathname) ||
    isBarberOnboardingPath(pathname)
  )
}
