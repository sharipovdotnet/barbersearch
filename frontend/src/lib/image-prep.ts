import { t } from '@/i18n'
import {
  MAX_PHOTO_BYTES,
  MAX_SOURCE_PHOTO_BYTES,
} from '@/lib/portfolio-photos'

/**
 * Клиентская подготовка фотографии портфолио.
 *
 * Браузер сам уменьшает и перекодирует выбранный файл: длинная сторона — не больше
 * 1600 px, прозрачность заливается белым, результат — JPEG quality 0.85 (при весе
 * больше 5 МиБ — повтор с 0.7). Это уменьшает трафик, убирает EXIF вместе с GPS и
 * держит файл в пределах серверного лимита; серверная генерация миниатюр в этом
 * срезе не делается (§3.8).
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
const SUPPORTED_TYPES: readonly string[] = [
  'image/jpeg',
  'image/png',
  'image/webp',
]

/**
 * Готовит выбранный файл к загрузке.
 *
 * До декодирования отбрасывает неподдерживаемый тип и файл больше 15 МиБ — такой
 * кадр незачем читать в память. После перекодирования, если JPEG всё ещё больше
 * 5 МиБ, пробует quality 0.7, иначе отдаёт `tooLarge`. Возвращает новый `File`
 * с именем `portfolio.jpg`: исходное имя на сервер не попадает.
 */
export async function preparePortfolioImage(file: File): Promise<File> {
  if (!SUPPORTED_TYPES.includes(file.type)) {
    throw new ImagePrepError(
      'unsupportedType',
      t('Поддерживаются только JPEG, PNG и WebP.')
    )
  }

  if (file.size > MAX_SOURCE_PHOTO_BYTES) {
    throw new ImagePrepError(
      'tooLarge',
      t('Файл больше 15 МБ. Выберите фото поменьше.')
    )
  }

  const bitmap = await decode(file)

  try {
    const canvas = draw(bitmap)
    const first = await encodeJpeg(canvas, QUALITY)
    const blob =
      first.size > MAX_PHOTO_BYTES
        ? await encodeJpeg(canvas, FALLBACK_QUALITY)
        : first

    if (blob.size > MAX_PHOTO_BYTES) {
      throw new ImagePrepError(
        'tooLarge',
        t('Не удалось уменьшить фото до 5 МБ. Выберите другое.')
      )
    }

    return new File([blob], 'portfolio.jpg', { type: 'image/jpeg' })
  } finally {
    if (typeof bitmap.close === 'function') {
      bitmap.close()
    }
  }
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
