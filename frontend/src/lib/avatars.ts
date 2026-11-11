import { t } from '@/i18n'
import { ApiError } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import type { PhotoModerationStatus } from '@/lib/photo-moderation'
import { isAvatarUploadResponse } from '@/lib/response-guards'

/**
 * Аватар пользователя: загрузка, удаление и разбор отказов.
 *
 * Транспорт повторяет мутации кабинета: изменяющие запросы идут через
 * `mutateWithCsrf` (свежий `X-CSRF-TOKEN`, один повтор на `400 auth.csrf.invalid`).
 * Загрузка — `multipart/form-data` с единственным полем `file`: `Content-Type`
 * с boundary выставляет браузер, поэтому `lib/api.ts` не подставляет JSON.
 *
 * Файл готовит браузер (`prepareAvatarImage`: квадрат 512×512, JPEG), но сервер
 * остаётся авторитетом: он проверяет сигнатуру, стороны и размер теми же правилами,
 * что и портфолио. Поэтому `describeAvatarError` знает общие коды
 * `barberProfile.uploadPhoto.*` и собственные `user.uploadAvatar.*`.
 */

/** Предел файла после клиентской подготовки и серверной проверки (5 МиБ). */
export const AVATAR_MAX_BYTES = 5 * 1024 * 1024

/** Предел исходного файла до декодирования: больше не читаем вовсе. */
export const AVATAR_SOURCE_MAX_BYTES = 15 * 1024 * 1024

/** Сторона квадратного аватара: сервер принимает от 300 px, клиент готовит 512. */
export const AVATAR_SIDE = 512

/** Ответ `201 { avatarUrl, moderationStatus }`: URL раздачи и статус премодерации. */
export type AvatarUploadResponse = {
  avatarUrl: string
  /** Новый кадр всегда ждёт проверки: сервер создаёт его `Pending`. */
  moderationStatus: PhotoModerationStatus
}

/**
 * Загрузка одного файла: `POST /api/users/me/avatar`.
 *
 * `file` уже подготовлен `prepareAvatarImage`; транспорт не перепроверяет размер.
 * Возвращает относительный URL и статус премодерации: до одобрения кадр виден
 * только владельцу, и карточка кабинета показывает бейдж «На проверке».
 */
export async function uploadAvatarWithStatus(
  file: File
): Promise<AvatarUploadResponse> {
  const form = new FormData()
  form.append('file', file)

  return mutateWithCsrf<AvatarUploadResponse>(
    '/users/me/avatar',
    {
      method: 'POST',
      body: form,
      validate: isAvatarUploadResponse,
    }
  )
}

/**
 * Удаление идемпотентно: `204` приходит и когда аватара не было.
 */
export async function deleteAvatar(): Promise<void> {
  await mutateWithCsrf<void>('/users/me/avatar', { method: 'DELETE' })
}

/** Поле, у которого показывается ошибка; `null` — общая строка карточки. */
export type AvatarField = 'file' | null

/** Разбор отказа: что показать, где и стоит ли повторять запрос. */
export type AvatarFailure = {
  field: AvatarField
  message: string
  /** `0`/`429`/`5xx`: данные не изменились, повтор безопасен. */
  canRetry: boolean
}

const FALLBACK = 'Не удалось изменить аватар. Попробуйте ещё раз.'

/**
 * Коды файла: общие с портфолио `barberProfile.uploadPhoto.*` и новые
 * `user.uploadAvatar.*`. Сообщение одинаково для парного кода — пользователю
 * важно, что случилось, а не какой из двух контроллеров ответил.
 */
const FILE_ERRORS: readonly {
  codes: readonly string[]
  message: string
}[] = [
  {
    codes: [
      'barberProfile.uploadPhoto.fileRequired',
      'user.uploadAvatar.fileRequired',
    ],
    message: 'Выберите файл.',
  },
  {
    // < 1 КиБ по сигнатуре: сервер считает такой файл мусором, а не фото.
    codes: ['barberProfile.uploadPhoto.empty'],
    message: 'Изображение пустое или слишком маленькое. Выберите другой файл.',
  },
  {
    codes: [
      'barberProfile.uploadPhoto.tooLarge',
      'user.uploadAvatar.tooLarge',
    ],
    message: 'Файл слишком большой. Максимум — 5 МБ.',
  },
  {
    codes: ['barberProfile.uploadPhoto.unsupportedType'],
    message: 'Поддерживаются только JPEG, PNG и WebP.',
  },
  {
    codes: ['barberProfile.uploadPhoto.invalidImage'],
    message: 'Не удалось прочитать изображение. Выберите другой файл.',
  },
  {
    codes: ['barberProfile.uploadPhoto.dimensions'],
    message: 'Стороны фото должны быть от 300 до 6000 px.',
  },
]

/**
 * Отказ загрузки или удаления аватара.
 *
 * Порядок важен: сначала `413` без ProblemDetails (Kestrel обрывает запрос до
 * действия), затем коды файла (их показывают у поля `file`), затем сбой хранилища,
 * затем `401`/`429`/сеть/`5xx`. Английский `detail` не показываем никогда: его
 * заменяет русский фолбэк.
 */
export function describeAvatarError(error: unknown): AvatarFailure {
  if (!(error instanceof ApiError)) {
    return { field: null, message: t(FALLBACK), canRetry: false }
  }

  if (error.status === 413) {
    return {
      field: 'file',
      message: t('Файл слишком большой. Максимум — 5 МБ.'),
      canRetry: false,
    }
  }

  for (const entry of FILE_ERRORS) {
    if (entry.codes.some((code) => error.hasCode(code))) {
      return {
        field: 'file',
        message: t(entry.message),
        canRetry: false,
      }
    }
  }

  if (error.hasCode('user.uploadAvatar.storage')) {
    return {
      field: null,
      message: t('Не удалось сохранить файл. Попробуйте ещё раз.'),
      canRetry: true,
    }
  }

  return {
    field: null,
    message: describeApiFailure(error, FALLBACK),
    canRetry: isRetryable(error.status),
  }
}

/** `0` — запрос не дошёл, `429` — лимит, `5xx` — сервер: данные не менялись. */
function isRetryable(status: number): boolean {
  return status === 0 || status === 429 || status >= 500
}
