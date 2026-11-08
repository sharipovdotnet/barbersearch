import { t } from '@/i18n'
import { ApiError } from '@/lib/api'
import type { BarberGrade } from '@/lib/barber-grade'
import type { Language, PaymentMethod } from '@/lib/barber-profile-labels'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import { isCreatedId } from '@/lib/response-guards'

/**
 * Создание профиля барбера.
 *
 * Контракт `POST /api/barber-profiles` закрыт политикой `BarberOnly`, профиль всегда
 * принадлежит текущему пользователю: `userId`, рейтинг и подтверждение в теле
 * не передаются — их назначает сервер. Грейд барбер выбирает сам при создании;
 * администратор может изменить его позже. Владелец профиля появляется в claim только
 * в следующем выданном токене, поэтому экран после `201` продлевает сессию.
 */

/** Ограничения домена: те же числа стоят в валидаторах сервера. */
export const BARBER_PROFILE_DISPLAY_NAME_MAX = 120
export const BARBER_PROFILE_EXPERIENCE_MIN = 1
export const BARBER_PROFILE_EXPERIENCE_MAX = 70
export const BARBER_PROFILE_BIO_MAX = 2000
export const BARBER_PROFILE_LATE_POLICY_MAX = 500

/** Тело запроса: строки обрезаны, пустые списки — `[]`, пустые необязательные — `null`. */
export type BarberProfileDraft = {
  displayName: string
  yearsOfExperience: number
  grade: BarberGrade
  bio: string | null
  languages: Language[]
  certificatesAndTraining: string[]
  achievements: string[]
  paymentMethods: PaymentMethod[]
  communicationFormat: string[]
  goodies: string[]
  additionalServices: string[]
  latePolicy: string | null
}

export async function createBarberProfile(
  draft: BarberProfileDraft
): Promise<{ id: string }> {
  // Cookie-мутация: antiforgery-токен обязателен (§9.4).
  return mutateWithCsrf<{ id: string }>('/barber-profiles', {
    method: 'POST',
    body: JSON.stringify(draft),
    validate: isCreatedId,
  })
}

/** Поля, к которым сервер умеет привязать отказ валидации. */
export type BarberProfileField =
  | 'displayName'
  | 'yearsOfExperience'
  | 'grade'
  | 'languages'
  | 'paymentMethods'

export type BarberProfileCreateFailure = {
  /** Поле или группа, у которой показывается ошибка; `null` — общая ошибка формы. */
  field: BarberProfileField | null
  message: string
  /** Транспортный сбой: повтор отправки осмыслен и не дублирует создание. */
  canRetry: boolean
}

const FALLBACK_ERROR = 'Не удалось создать профиль. Попробуйте ещё раз.'

/** Коды домена → поле и русский текст; английский `detail` не показываем никогда. */
const FIELD_ERRORS: readonly {
  code: string
  field: BarberProfileField
  message: string
}[] = [
  {
    code: 'barberProfile.displayName',
    field: 'displayName',
    message: 'Укажите, как вас представить.',
  },
  {
    code: 'barberProfile.displayName.tooLong',
    field: 'displayName',
    message: 'Не длиннее 120 символов.',
  },
  {
    code: 'barberProfile.yearsOfExperience',
    field: 'yearsOfExperience',
    message: 'Опыт — от 1 до 70 лет.',
  },
  {
    code: 'barberProfile.grade.unknown',
    field: 'grade',
    message: 'Выберите грейд из списка.',
  },
  {
    code: 'barberProfile.languages.unknown',
    field: 'languages',
    message: 'Выберите языки из списка.',
  },
  {
    code: 'barberProfile.paymentMethods.unknown',
    field: 'paymentMethods',
    message: 'Выберите способы оплаты из списка.',
  },
]

/**
 * Отказ `POST /api/barber-profiles` в виде «где показать» и «что сказать».
 *
 * `409 barberProfile.create.alreadyExists` сюда не попадает: профиль уже создан, и
 * экран переходит в кабинет вместо показа ошибки.
 */
export function describeBarberProfileCreateError(
  error: unknown
): BarberProfileCreateFailure {
  if (!(error instanceof ApiError)) {
    return { field: null, message: t(FALLBACK_ERROR), canRetry: false }
  }

  const fieldError = FIELD_ERRORS.find((entry) => error.hasCode(entry.code))

  if (fieldError) {
    return {
      field: fieldError.field,
      message: t(fieldError.message),
      canRetry: false,
    }
  }

  return {
    field: null,
    message: describeApiFailure(error, FALLBACK_ERROR),
    canRetry: isRetryable(error.status),
  }
}

/** `0` — запрос не дошёл, `429` — лимит, `5xx` — сервер: профиль не создан, повторяем. */
function isRetryable(status: number): boolean {
  return status === 0 || status === 429 || status >= 500
}
