import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import {
  describeBarberProfileFailure,
  type BarberProfileFailure,
} from '@/lib/barber-profile-create'
import { diffBarberSpecializations } from '@/lib/barber-specialization-sync'
import type { Language, PaymentMethod } from '@/lib/barber-profile-labels'
import type { BarberProfile } from '@/lib/catalog'
import { mutateWithCsrf } from '@/lib/csrf'
import { isBarberProfile } from '@/lib/response-guards'

/**
 * Транспорт своего профиля барбера.
 *
 * Отдельный модуль, а не `catalog.ts`: своё чтение отдаёт и скрытые услуги, и
 * неподтверждённый профиль, поэтому в публичный кэш карточки оно не пишется.
 * `GET /api/barber-profiles/{id}` — единственный источник данных для правки;
 * `DELETE .../services/{hairServiceId}` скрывает услугу (деактивация), а не стирает
 * строку, поэтому в UI это «Скрыть», а не «Удалить».
 */

/** Цена и длительность услуги этого барбера: тело `PUT .../services/{id}`. */
export type BarberServiceInput = {
  price: number
  durationMinutes: number
}

/**
 * Тело `PUT /api/barber-profiles/{id}`.
 *
 * Грейда здесь нет: его назначает только администратор. `latePolicy` — строка
 * (пустая, а не `null`): контракт `BarberProfileUpdateRequest` объявляет `string`.
 * Ограничения полей — константы `barber-profile-create.ts`, UI берёт их оттуда.
 */
export type BarberProfileUpdateDraft = {
  displayName: string
  yearsOfExperience: number
  bio: string | null
  languages: Language[]
  certificatesAndTraining: string[]
  achievements: string[]
  paymentMethods: PaymentMethod[]
  communicationFormat: string[]
  goodies: string[]
  additionalServices: string[]
  latePolicy: string
}

/** Карточка для правки: скрытые услуги приходят как есть, `isActive: false`. */
export function fetchOwnBarberProfile(
  id: string,
  signal?: AbortSignal
): Promise<BarberProfile> {
  return apiRequest<BarberProfile>(
    `/barber-profiles/${id}`,
    signal ? { signal } : {},
    { validate: isBarberProfile }
  )
}

export async function updateBarberProfile(
  id: string,
  draft: BarberProfileUpdateDraft
): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${id}`, {
    method: 'PUT',
    body: JSON.stringify(draft),
  })
}

/** Повторное добавление уже скрытой услуги реактивирует её с новой ценой. */
export async function addBarberService(
  id: string,
  input: { hairServiceId: string; price: number; durationMinutes: number }
): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${id}/services`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

/** Правка цены и длительности без скрытия услуги и пары DELETE + POST. */
export async function updateBarberService(
  id: string,
  hairServiceId: string,
  input: BarberServiceInput
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${id}/services/${hairServiceId}`,
    {
      method: 'PUT',
      body: JSON.stringify(input),
    }
  )
}

/** «Удаление» услуги = деактивация: строка остаётся ради истории записей. */
export async function removeBarberService(
  id: string,
  hairServiceId: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${id}/services/${hairServiceId}`,
    { method: 'DELETE' }
  )
}

/** Специализация выбирается из справочника, id идёт путём. */
export async function addBarberSpecialization(
  id: string,
  specializationId: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${id}/specializations/${specializationId}`,
    { method: 'POST' }
  )
}

/** Удаление специализации физическое: истории за ней нет. */
export async function removeBarberSpecialization(
  id: string,
  specializationId: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${id}/specializations/${specializationId}`,
    { method: 'DELETE' }
  )
}

/** Итог синхронизации тегов: текст ошибки уже локализован. */
export type BarberSpecializationSyncResult =
  | { ok: true }
  | { ok: false; message: string }

/**
 * Приводит теги профиля в соответствие с активными услугами.
 *
 * Diff считается по свежему `GET`, а не по пропсам страницы: после мутации
 * услуги они устарели. Сбой синхронизации не отменяет уже случившееся действие
 * с услугой и не повторяет его — вызывающий код лишь показывает отдельную
 * ошибку. «Тег уже есть» (`409`) и «тега уже нет» (`404`) — гонка с другим
 * клиентом, а не отказ; `addSpecialization.specializationNotFound` — настоящая
 * ошибка и не глотается.
 */
export async function syncBarberSpecializations(
  barberProfileId: string
): Promise<BarberSpecializationSyncResult> {
  let failed = false

  try {
    const profile = await fetchOwnBarberProfile(barberProfileId)
    const { add, remove } = diffBarberSpecializations(
      profile.services,
      profile.specializations
    )

    for (const specializationId of remove) {
      try {
        await removeBarberSpecialization(barberProfileId, specializationId)
      } catch (cause) {
        if (!isSpecializationAlreadyRemoved(cause)) {
          failed = true
        }
      }
    }

    for (const specializationId of add) {
      try {
        await addBarberSpecialization(barberProfileId, specializationId)
      } catch (cause) {
        if (!isSpecializationAlreadyAdded(cause)) {
          failed = true
        }
      }
    }
  } catch {
    // Даже перечитать карточку не удалось: показываем ту же ошибку синхронизации.
    failed = true
  }

  return failed
    ? {
        ok: false,
        message: t('Не удалось обновить специализации. Попробуйте ещё раз.'),
      }
    : { ok: true }
}

/** Тег уже убрали из другого клиента: повторный `DELETE` — не отказ. */
function isSpecializationAlreadyRemoved(cause: unknown): boolean {
  return (
    cause instanceof ApiError &&
    (cause.status === 404 ||
      cause.hasCode('barberProfile.removeSpecialization.notFound'))
  )
}

/** Тег уже добавили из другого клиента: повторный `POST` — не отказ. */
function isSpecializationAlreadyAdded(cause: unknown): boolean {
  return (
    cause instanceof ApiError &&
    (cause.status === 409 ||
      cause.hasCode('barberProfile.addSpecialization.duplicate'))
  )
}

const FALLBACK = 'Не удалось сохранить профиль. Попробуйте ещё раз.'

/** Коды услуг → русский текст; английский `detail` не показываем никогда. */
const SERVICE_ERRORS: readonly { code: string; message: string }[] = [
  {
    code: 'barberProfile.addService.duplicate',
    message: 'Эта услуга уже есть в прайсе.',
  },
  {
    code: 'barberProfile.updateService.notFound',
    message: 'Эта услуга уже скрыта. Обновите страницу.',
  },
  {
    code: 'barberProfile.removeService.notFound',
    message: 'Эта услуга уже скрыта. Обновите страницу.',
  },
  {
    code: 'barberService.updatePrice',
    message: 'Цена не может быть отрицательной.',
  },
  {
    code: 'barberService.updateDuration',
    message: 'Длительность — от 1 до 1440 минут.',
  },
  {
    code: 'barberProfile.addService.duration',
    message: 'Длительность — от 1 до 1440 минут.',
  },
]

/**
 * Отказ сохранения карточки или прайса.
 *
 * Сначала коды услуг (у них своя семантика), затем общие коды полей профиля из
 * `barber-profile-create.ts`. `canRetry` есть только у транспортных сбоев: `0`,
 * `429` и `5xx` не меняют данные, повтор безопасен.
 */
export function describeBarberProfileError(
  error: unknown
): BarberProfileFailure {
  if (error instanceof ApiError) {
    const serviceError = SERVICE_ERRORS.find((entry) =>
      error.hasCode(entry.code)
    )

    if (serviceError) {
      return {
        field: null,
        message: t(serviceError.message),
        canRetry: false,
      }
    }

    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return {
        field: null,
        message: t('Прайс доступен только владельцу профиля.'),
        canRetry: false,
      }
    }
  }

  return describeBarberProfileFailure(error, FALLBACK)
}
