import { t } from '@/i18n'
import {
  AVATAR_MAX_BYTES,
  AVATAR_SIDE,
  AVATAR_SOURCE_MAX_BYTES,
} from '@/lib/avatars'
import {
  MAX_PHOTO_BYTES,
  MAX_SOURCE_PHOTO_BYTES,
} from '@/lib/portfolio-photos'

/**
 * Клиентская подготовка изображений.
 *
 * Браузер сам уменьшает и перекодирует выбранный файл: портфолио — длинная сторона
 * не больше 1600 px, аватар — квадрат 512×512. Прозрачность заливается белым,
 * результат — JPEG quality 0.85 (при весе больше 5 МиБ — повтор с 0.7). Это
 * уменьшает трафик, убирает EXIF вместе с GPS и держит файл в пределах серверного
 * лимита; серверная генерация миниатюр в этом срезе не делается (§3.8).
 *
 * Ошибки несут машинный код: UI переводит его в текст у поля, а не показывает
 * английский `detail`. JSX-превью и `URL.createObjectURL` — забота компонента.
 */

export type ImagePrepErrorCode = 'unsupportedType' | 'tooLarge' | 'decodeFailed'

export class ImagePrepError extends Error {
  readonly code: ImagePrepErrorCode

  constructor(code: ImagePrepErrorCode, message: string) {
    super(message)
    this.name = 'ImagePrepError'
    this.code = code
  }
}

/** Длинная сторона результата: больше не растягиваем и не уменьшаем сверх этого. */
const MAX_SIDE = 1600

/** Качество первого прохода и запасной проход для тяжёлых кадров. */
const QUALITY = 0.85
const FALLBACK_QUALITY = 0.7

/** Только растровые форматы: SVG (XSS) и GIF (анимация) сервер не принимает. */
export const SUPPORTED_IMAGE_TYPES: readonly string[] = [
  'image/jpeg',
  'image/png',
  'image/webp',
]

/**
 * Готовит выбранный файл к загрузке в портфолио.
 *
 * До декодирования отбрасывает неподдерживаемый тип и файл больше 15 МиБ — такой
 * кадр незачем читать в память. После перекодирования, если JPEG всё ещё больше
 * 5 МиБ, пробует quality 0.7, иначе отдаёт `tooLarge`. Возвращает новый `File`
 * с именем `portfolio.jpg`: исходное имя на сервер не попадает.
 */
export async function preparePortfolioImage(file: File): Promise<File> {
  assertSupportedImage(file, MAX_SOURCE_PHOTO_BYTES)

  const bitmap = await decode(file)

  try {
    const canvas = draw(bitmap)
    const blob = await encodeWithinLimit(canvas, MAX_PHOTO_BYTES)

    return new File([blob], 'portfolio.jpg', { type: 'image/jpeg' })
  } finally {
    closeBitmap(bitmap)
  }
}

/**
 * Готовит аватар: квадратный центр-кроп, ровно 512×512, JPEG.
 *
 * Сторона квадрата — `min(width, height)`, смещения по центру. Масштаб всегда
 * доводит до `AVATAR_SIDE`, в том числе вверх: сервер принимает стороны от 300 px,
 * поэтому маленький исходник должен вырасти, а не остаться 180 px. Имя результата —
 * `avatar.jpg`: исходное имя файла на сервер не попадает.
 */
export async function prepareAvatarImage(file: File): Promise<File> {
  assertSupportedImage(file, AVATAR_SOURCE_MAX_BYTES)

  const bitmap = await decode(file)

  try {
    const canvas = drawAvatar(bitmap)
    const blob = await encodeWithinLimit(canvas, AVATAR_MAX_BYTES)

    return new File([blob], 'avatar.jpg', { type: 'image/jpeg' })
  } finally {
    closeBitmap(bitmap)
  }
}

/**
 * Проверки до декодирования, общие для портфолио и аватара.
 *
 * Различается только предел исходного файла; сообщение про 15 МБ остаётся
 * общим, потому что оба предела равны.
 */
function assertSupportedImage(file: File, maxSourceBytes: number): void {
  if (!SUPPORTED_IMAGE_TYPES.includes(file.type)) {
    throw new ImagePrepError(
      'unsupportedType',
      t('Поддерживаются только JPEG, PNG и WebP.')
    )
  }

  if (file.size > maxSourceBytes) {
    throw new ImagePrepError(
      'tooLarge',
      t('Файл больше 15 МБ. Выберите фото поменьше.')
    )
  }
}

function closeBitmap(bitmap: ImageBitmap): void {
  if (typeof bitmap.close === 'function') {
    bitmap.close()
  }
}

/**
 * Кодирует canvas в JPEG и держит результат в пределах `maxBytes`.
 *
 * Первый проход — 0.85; если файл тяжелее лимита, повторяем тот же canvas с 0.7.
 * Если и второй проход не уложился, отдаём `tooLarge`: уменьшать больше нечего.
 */
async function encodeWithinLimit(
  canvas: HTMLCanvasElement,
  maxBytes: number
): Promise<Blob> {
  const first = await encodeJpeg(canvas, QUALITY)
  const blob =
    first.size > maxBytes ? await encodeJpeg(canvas, FALLBACK_QUALITY) : first

  if (blob.size > maxBytes) {
    throw new ImagePrepError(
      'tooLarge',
      t('Не удалось уменьшить фото до 5 МБ. Выберите другое.')
    )
  }

  return blob
}

/** `createImageBitmap` с учётом EXIF; старые движки не знают опцию — пробуем без неё. */
async function decode(file: File): Promise<ImageBitmap> {
  try {
    return await createImageBitmap(file, { imageOrientation: 'from-image' })
  } catch {
    // Отказ по опции (или её отсутствие) — второй шанс без параметров.
  }

  try {
    return await createImageBitmap(file)
  } catch {
    throw decodeFailed()
  }
}

/** Рисует уменьшенную копию на белой подложке: прозрачный PNG не станет чёрным. */
function draw(bitmap: ImageBitmap): HTMLCanvasElement {
  const { width, height } = fitInside(bitmap.width, bitmap.height)
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height

  const context = canvas.getContext('2d')

  if (!context) {
    throw decodeFailed()
  }

  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, width, height)
  context.drawImage(bitmap, 0, 0, width, height)

  return canvas
}

/**
 * Квадратный центр-кроп и масштаб ровно до `AVATAR_SIDE`×`AVATAR_SIDE`.
 *
 * Сторона источника — `min(width, height)`; смещения считаются от центра, чтобы
 * обрезать поровну слева и справа (сверху и снизу). `drawImage` с девятью
 * аргументами одновременно кропает и масштабирует, в том числе вверх.
 */
function drawAvatar(bitmap: ImageBitmap): HTMLCanvasElement {
  const side = Math.min(bitmap.width, bitmap.height)

  if (side <= 0) {
    throw decodeFailed()
  }

  const sourceX = Math.round((bitmap.width - side) / 2)
  const sourceY = Math.round((bitmap.height - side) / 2)
  const canvas = document.createElement('canvas')
  canvas.width = AVATAR_SIDE
  canvas.height = AVATAR_SIDE

  const context = canvas.getContext('2d')

  if (!context) {
    throw decodeFailed()
  }

  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, AVATAR_SIDE, AVATAR_SIDE)
  context.drawImage(
    bitmap,
    sourceX,
    sourceY,
    side,
    side,
    0,
    0,
    AVATAR_SIDE,
    AVATAR_SIDE
  )

  return canvas
}

/** Не увеличиваем: сторона меньше 1600 px остаётся как есть (с минимумом 1 px). */
function fitInside(
  sourceWidth: number,
  sourceHeight: number
): { width: number; height: number } {
  if (sourceWidth <= 0 || sourceHeight <= 0) {
    throw decodeFailed()
  }

  const longest = Math.max(sourceWidth, sourceHeight)

  if (longest <= MAX_SIDE) {
    return { width: sourceWidth, height: sourceHeight }
  }

  const scale = MAX_SIDE / longest

  return {
    width: Math.max(1, Math.round(sourceWidth * scale)),
    height: Math.max(1, Math.round(sourceHeight * scale)),
  }
}

/** `canvas.toBlob` в промис; `null` и исключение одинаково означают «не прочитали». */
function encodeJpeg(
  canvas: HTMLCanvasElement,
  quality: number
): Promise<Blob> {
  return new Promise((resolve, reject) => {
    try {
      canvas.toBlob(
        (blob) => {
          if (blob) {
            resolve(blob)
            return
          }

          reject(decodeFailed())
        },
        'image/jpeg',
        quality
      )
    } catch {
      reject(decodeFailed())
    }
  })
}

function decodeFailed(): ImagePrepError {
  return new ImagePrepError(
    'decodeFailed',
    t('Не удалось прочитать изображение. Выберите другой файл.')
  )
}
