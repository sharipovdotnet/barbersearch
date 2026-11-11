import { t } from '@/i18n'
import { ApiError } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import { isCreatedResource } from '@/lib/response-guards'

/**
 * Портфолио барбера: загрузка, подпись, порядок, публикация и админская блокировка.
 *
 * Транспорт повторяет стиль остальных мутаций кабинета: каждый изменяющий запрос —
 * cookie-мутация через `mutateWithCsrf` (свежий `X-CSRF-TOKEN`, один повтор на
 * `400 auth.csrf.invalid`). Загрузка — `multipart/form-data`: `FormData` с полями
 * `file` и `description`; `Content-Type` с boundary выставляет браузер, поэтому
 * `lib/api.ts` не подставляет JSON-заголовок для `FormData`.
 *
 * Коды ошибок §4.8 важнее английского `detail`: `describePortfolioPhotoError`
 * переводит их в русский текст и говорит, показать его у поля `file`/`description`
 * или общей строкой над списком.
 */

/** Фотография портфолио в публичной карточке, кабинете и админском ответе. */
export type PortfolioPhoto = {
  id: string
  /** Относительный `/api/media/...` или абсолютный внешний URL — готов для `<img src>`. */
  url: string
  /** Миниатюр в этом срезе нет: поле остаётся в контракте и равно `null`. */
  thumbnailUrl: string | null
  description: string | null
  sortOrder: number
  isPublished: boolean
  isBlocked: boolean
}

/** Доменный лимит портфолио: одиннадцатая фотография — `409`. */
export const MAX_PORTFOLIO_PHOTOS = 10

/** Предел файла после клиентской подготовки (5 МиБ). */
export const MAX_PHOTO_BYTES = 5 * 1024 * 1024

/** Предел исходного файла до декодирования: больше не читаем вовсе. */
export const MAX_SOURCE_PHOTO_BYTES = 15 * 1024 * 1024

/** Предел подписи: та же длина стоит в домене. */
export const PORTFOLIO_DESCRIPTION_MAX = 2000

/** Ответ `201 { id }` загрузки: отдельного чтения фотографии по id нет. */
export type CreatedResource = {
  id: string
}

/** Тело `PUT .../photos/{photoId}/description`: пустая подпись хранится как `null`. */
export type PortfolioPhotoDescriptionInput = {
  description: string | null
}

function photosPath(profileId: string): string {
  return `/barber-profiles/${profileId}/photos`
}

/**
 * Загрузка одного файла: `POST /api/barber-profiles/{id}/photos/upload`.
 *
 * `file` уже подготовлен `preparePortfolioImage` (JPEG, ≤ 1600 px, ≤ 5 МиБ), но
 * транспорт не перепроверяет размер: это дело подготовки и сервера. Подпись
 * обрезается, пустая уходит как пустая строка — домен сам превратит её в `null`.
 * Возвращает id созданной фотографии.
 */
export async function uploadPortfolioPhoto(
  profileId: string,
  file: File,
  description: string | null = null
): Promise<string> {
  const form = new FormData()
  form.append('file', file)
  form.append('description', description?.trim() ?? '')

  const created = await mutateWithCsrf<CreatedResource>(
    `${photosPath(profileId)}/upload`,
    {
      method: 'POST',
      body: form,
      validate: isCreatedResource,
    }
  )

  return created.id
}

/** Правка подписи: `PUT .../photos/{photoId}/description`, пустая строка → `null`. */
export async function updatePortfolioPhotoDescription(
  profileId: string,
  photoId: string,
  description: string
): Promise<void> {
  const body: PortfolioPhotoDescriptionInput = {
    description: description.trim() || null,
  }

  await mutateWithCsrf<void>(`${photosPath(profileId)}/${photoId}/description`, {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}

/** Удаление физическое: после `204` фотографии в карточке больше нет. */
export async function removePortfolioPhoto(
  profileId: string,
  photoId: string
): Promise<void> {
  await mutateWithCsrf<void>(`${photosPath(profileId)}/${photoId}`, {
    method: 'DELETE',
  })
}

/** «Вернуть»: владелец публикует скрытую фотографию (`POST .../publication`). */
export async function publishPortfolioPhoto(
  profileId: string,
  photoId: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `${photosPath(profileId)}/${photoId}/publication`,
    { method: 'POST' }
  )
}

/** «Скрыть»: владелец снимает фотографию с публикации (`DELETE .../publication`). */
export async function unpublishPortfolioPhoto(
  profileId: string,
  photoId: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `${photosPath(profileId)}/${photoId}/publication`,
    { method: 'DELETE' }
  )
}

/**
 * Порядок меняется одним запросом с полным массивом id.
 *
 * Одиночный `sort-order` не используется: обмен местами двумя `PUT` при частичном
 * сбое дал бы дубли `SortOrder`.
 */
export async function reorderPortfolioPhotos(
  profileId: string,
  photoIds: readonly string[]
): Promise<void> {
  await mutateWithCsrf<void>(`${photosPath(profileId)}/order`, {
    method: 'PUT',
    body: JSON.stringify({ photoIds }),
  })
}

/** Админская блокировка: фотография исчезает из публичной карточки и раздачи. */
export async function blockPortfolioPhoto(
  profileId: string,
  photoId: string
): Promise<void> {
  await mutateWithCsrf<void>(`${photosPath(profileId)}/${photoId}/block`, {
    method: 'POST',
  })
}

/** Админская разблокировка; владелец этого сделать не может. */
export async function unblockPortfolioPhoto(
  profileId: string,
  photoId: string
): Promise<void> {
  await mutateWithCsrf<void>(`${photosPath(profileId)}/${photoId}/block`, {
    method: 'DELETE',
  })
}

/** Поле формы, у которого показывается ошибка; `null` — общая строка. */
export type PortfolioPhotoField = 'file' | 'description' | null

/** Разбор отказа: что показать, где и стоит ли повторять запрос. */
export type PortfolioPhotoFailure = {
  field: PortfolioPhotoField
  message: string
  /** `0`/`429`/`5xx`: данные не изменились, повтор безопасен. */
  canRetry: boolean
}

const FALLBACK = 'Не удалось выполнить действие с фото. Попробуйте ещё раз.'

const FILE_ERRORS: readonly {
  code: string
  message: string
  field: Exclude<PortfolioPhotoField, null>
}[] = [
  {
    code: 'barberProfile.uploadPhoto.fileRequired',
    message: 'Выберите файл.',
    field: 'file',
  },
  {
    code: 'barberProfile.uploadPhoto.tooLarge',
    message: 'Файл слишком большой. Максимум — 5 МБ.',
    field: 'file',
  },
  {
    code: 'barberProfile.uploadPhoto.unsupportedType',
    message: 'Поддерживаются только JPEG, PNG и WebP.',
    field: 'file',
  },
  {
    code: 'barberProfile.uploadPhoto.invalidImage',
    message: 'Не удалось прочитать изображение. Выберите другой файл.',
    field: 'file',
  },
  {
    code: 'barberProfile.uploadPhoto.dimensions',
    message: 'Стороны фото должны быть от 300 до 6000 px.',
    field: 'file',
  },
  {
    code: 'barberProfile.addPhoto.description',
    message: 'Подпись не длиннее 2000 символов.',
    field: 'description',
  },
]

/**
 * Отказ действия с фотографией.
 *
 * Порядок важен: коды полей `file`/`description` (их показывают у поля), затем
 * лимит и порядок (общая строка), затем `403`/`404`, `413` без ProblemDetails,
 * `401`/`429`/сеть через `describeApiFailure`. Английский `detail` не показываем.
 */
export function describePortfolioPhotoError(
  error: unknown
): PortfolioPhotoFailure {
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

  const fieldError = FILE_ERRORS.find((entry) => error.hasCode(entry.code))

  if (fieldError) {
    return {
      field: fieldError.field,
      message: t(fieldError.message),
      canRetry: false,
    }
  }

  if (error.hasCode('barberProfile.addPhoto.limit')) {
    return {
      field: null,
      message: t(
        'В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.'
      ),
      canRetry: false,
    }
  }

  if (error.hasCode('barberProfile.reorderPhotos.invalid')) {
    return {
      field: null,
      message: t('Не удалось сохранить порядок. Обновите страницу.'),
      canRetry: false,
    }
  }

  if (
    error.hasCode('portfolioPhoto.block.notFound') ||
    error.hasCode('portfolioPhoto.unblock.notFound') ||
    error.status === 404
  ) {
    return {
      field: null,
      message: t('Фото не найдено. Обновите страницу.'),
      canRetry: false,
    }
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return {
      field: null,
      message: t('Нет доступа к портфолио этого барбера.'),
      canRetry: false,
    }
  }

  if (error.hasCode('barberProfile.uploadPhoto.storage')) {
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
