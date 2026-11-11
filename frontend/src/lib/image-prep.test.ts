import { describe, expect, it, vi } from 'vitest'

import {
  ImagePrepError,
  preparePortfolioImage,
} from '@/lib/image-prep'
import {
  MAX_PHOTO_BYTES,
  MAX_SOURCE_PHOTO_BYTES,
} from '@/lib/portfolio-photos'

type FakeBitmap = {
  width: number
  height: number
  close: ReturnType<typeof vi.fn>
}

type FakeContext = {
  fillStyle: string
  fillRect: ReturnType<typeof vi.fn>
  drawImage: ReturnType<typeof vi.fn>
}

function fakeBitmap(width: number, height: number): FakeBitmap {
  return { width, height, close: vi.fn() }
}

/** jsdom не рисует: подменяем ровно те члены контекста, которые читает библиотека. */
function stubCanvas(blobs: Blob[]): {
  qualities: number[]
  context: FakeContext
} {
  const context: FakeContext = {
    fillStyle: '',
    fillRect: vi.fn(),
    drawImage: vi.fn(),
  }
  const qualities: number[] = []

  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
    () => context as unknown as CanvasRenderingContext2D
  )

  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(
    (callback, _type, quality) => {
      qualities.push(Number(quality))
      callback(blobs.shift() ?? new Blob(['prepared'], { type: 'image/jpeg' }))
    }
  )

  return { qualities, context }
}

/** Размер без пятимегабайтной аллокации: свойство `size` перекрывается на экземпляре. */
function blobOfSize(size: number): Blob {
  const blob = new Blob(['bytes'], { type: 'image/jpeg' })
  Object.defineProperty(blob, 'size', { value: size })

  return blob
}

function photoFile(type = 'image/jpeg', size?: number): File {
  const file = new File(['photo-bytes'], 'source', { type })

  if (size !== undefined) {
    Object.defineProperty(file, 'size', { value: size })
  }

  return file
}

function stubBitmap(
  impl: (file: File, options?: ImageBitmapOptions) => Promise<ImageBitmap>
) {
  const mock = vi.fn(impl)
  vi.stubGlobal('createImageBitmap', mock)

  return mock
}

async function prepErrorOf(promise: Promise<File>): Promise<ImagePrepError> {
  try {
    await promise
  } catch (error) {
    if (error instanceof ImagePrepError) {
      return error
    }

    throw error
  }

  throw new Error('ожидался ImagePrepError, но файл подготовился')
}

describe('preparePortfolioImage, проверки до декодирования', () => {
  it('отвергает неподдерживаемый тип, не вызывая createImageBitmap', async () => {
    const create = stubBitmap(async () => fakeBitmap(800, 600) as ImageBitmap)

    const error = await prepErrorOf(
      preparePortfolioImage(photoFile('image/gif'))
    )

    expect(error.code).toBe('unsupportedType')
    expect(error.message).toBe('Поддерживаются только JPEG, PNG и WebP.')
    expect(create).not.toHaveBeenCalled()
  })

  it('отвергает пустой MIME: тип должен быть известен, а не «повезёт»', async () => {
    stubBitmap(async () => fakeBitmap(800, 600) as ImageBitmap)

    const error = await prepErrorOf(preparePortfolioImage(photoFile('')))

    expect(error.code).toBe('unsupportedType')
  })

  it('отбрасывает исходник больше 15 МБ до чтения в память', async () => {
    const create = stubBitmap(async () => fakeBitmap(800, 600) as ImageBitmap)

    const error = await prepErrorOf(
      preparePortfolioImage(photoFile('image/png', MAX_SOURCE_PHOTO_BYTES + 1))
    )

    expect(error.code).toBe('tooLarge')
    expect(error.message).toBe('Файл больше 15 МБ. Выберите фото поменьше.')
    expect(create).not.toHaveBeenCalled()
  })

  it('принимает ровно 15 МБ: лимит включительный', async () => {
    stubBitmap(async () => fakeBitmap(800, 600) as ImageBitmap)
    stubCanvas([new Blob(['prepared'], { type: 'image/jpeg' })])

    await expect(
      preparePortfolioImage(photoFile('image/png', MAX_SOURCE_PHOTO_BYTES))
    ).resolves.toBeInstanceOf(File)
  })
})

describe('preparePortfolioImage, уменьшение и кодирование', () => {
  it('уменьшает длинную сторону до 1600 px и рисует на белой подложке', async () => {
    const bitmap = fakeBitmap(3200, 2400)
    const file = photoFile()
    const create = stubBitmap(async () => bitmap as unknown as ImageBitmap)
    const { context } = stubCanvas([
      new Blob(['prepared'], { type: 'image/jpeg' }),
    ])

    const result = await preparePortfolioImage(file)

    expect(create).toHaveBeenCalledWith(file, {
      imageOrientation: 'from-image',
    })
    expect(context.fillStyle).toBe('#ffffff')
    expect(context.fillRect).toHaveBeenCalledWith(0, 0, 1600, 1200)
    expect(context.drawImage).toHaveBeenCalledWith(bitmap, 0, 0, 1600, 1200)
    expect(bitmap.close).toHaveBeenCalledTimes(1)
    expect(result.name).toBe('portfolio.jpg')
    expect(result.type).toBe('image/jpeg')
  })

  it('не увеличивает маленькое фото: 800×600 остаётся 800×600', async () => {
    const bitmap = fakeBitmap(800, 600)
    stubBitmap(async () => bitmap as unknown as ImageBitmap)
    const { context } = stubCanvas([
      new Blob(['prepared'], { type: 'image/jpeg' }),
    ])

    await preparePortfolioImage(photoFile())

    expect(context.drawImage).toHaveBeenCalledWith(bitmap, 0, 0, 800, 600)
    expect(context.fillRect).toHaveBeenCalledWith(0, 0, 800, 600)
  })

  it('не оставляет нулевую сторону у экстремального соотношения', async () => {
    const bitmap = fakeBitmap(4000, 2)
    stubBitmap(async () => bitmap as unknown as ImageBitmap)
    const { context } = stubCanvas([
      new Blob(['prepared'], { type: 'image/jpeg' }),
    ])

    await preparePortfolioImage(photoFile())

    expect(context.drawImage).toHaveBeenCalledWith(bitmap, 0, 0, 1600, 1)
  })

  it('кодирует JPEG quality 0.85 и не повторяет, если файл лёгок', async () => {
    stubBitmap(async () => fakeBitmap(1600, 1200) as ImageBitmap)
    const { qualities } = stubCanvas([
      new Blob(['prepared'], { type: 'image/jpeg' }),
    ])

    await preparePortfolioImage(photoFile())

    expect(qualities).toStrictEqual([0.85])
  })

  it('если JPEG больше 5 МБ — повторяет с quality 0.7', async () => {
    stubBitmap(async () => fakeBitmap(1600, 1200) as ImageBitmap)
    const { qualities } = stubCanvas([
      blobOfSize(MAX_PHOTO_BYTES + 1),
      new Blob(['prepared'], { type: 'image/jpeg' }),
    ])

    const result = await preparePortfolioImage(photoFile())

    expect(qualities).toStrictEqual([0.85, 0.7])
    expect(result.size).toBeLessThanOrEqual(MAX_PHOTO_BYTES)
  })

  it('если и 0.7 больше 5 МБ — отказ tooLarge', async () => {
    stubBitmap(async () => fakeBitmap(1600, 1200) as ImageBitmap)
    const { qualities } = stubCanvas([
      blobOfSize(MAX_PHOTO_BYTES + 1),
      blobOfSize(MAX_PHOTO_BYTES + 1),
    ])

    const error = await prepErrorOf(preparePortfolioImage(photoFile()))

    expect(error.code).toBe('tooLarge')
    expect(error.message).toBe(
      'Не удалось уменьшить фото до 5 МБ. Выберите другое.'
    )
    expect(qualities).toStrictEqual([0.85, 0.7])
  })
})

describe('preparePortfolioImage, декодирование', () => {
  it('при отказе с опцией пробует без неё', async () => {
    const bitmap = fakeBitmap(800, 600)
    const create = stubBitmap(async (_file, options) => {
      if (options) {
        throw new Error('imageOrientation is not supported')
      }

      return bitmap as unknown as ImageBitmap
    })
    stubCanvas([new Blob(['prepared'], { type: 'image/jpeg' })])

    await expect(preparePortfolioImage(photoFile())).resolves.toBeInstanceOf(
      File
    )

    expect(create).toHaveBeenCalledTimes(2)
    expect(create.mock.calls[0][1]).toStrictEqual({
      imageOrientation: 'from-image',
    })
    expect(create.mock.calls[1]).toHaveLength(1)
  })

  it('если оба вызова упали — decodeFailed и закрытие не нужно', async () => {
    const create = stubBitmap(async () => {
      throw new Error('decode failed')
    })

    const error = await prepErrorOf(preparePortfolioImage(photoFile()))

    expect(error.code).toBe('decodeFailed')
    expect(error.message).toBe(
      'Не удалось прочитать изображение. Выберите другой файл.'
    )
    expect(create).toHaveBeenCalledTimes(2)
  })

  it('отсутствие createImageBitmap — тоже decodeFailed, а не падение наружу', async () => {
    vi.stubGlobal('createImageBitmap', undefined)

    const error = await prepErrorOf(preparePortfolioImage(photoFile()))

    expect(error.code).toBe('decodeFailed')
  })

  it('нулевые размеры битмапа не делят на ноль', async () => {
    stubBitmap(async () => fakeBitmap(0, 600) as unknown as ImageBitmap)

    const error = await prepErrorOf(preparePortfolioImage(photoFile()))

    expect(error.code).toBe('decodeFailed')
  })

  it('canvas без 2d-контекста — decodeFailed, битмап закрывается', async () => {
    const bitmap = fakeBitmap(800, 600)
    stubBitmap(async () => bitmap as unknown as ImageBitmap)
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)

    const error = await prepErrorOf(preparePortfolioImage(photoFile()))

    expect(error.code).toBe('decodeFailed')
    expect(bitmap.close).toHaveBeenCalledTimes(1)
  })

  it('toBlob с null — decodeFailed, а не пустой файл', async () => {
    const bitmap = fakeBitmap(800, 600)
    stubBitmap(async () => bitmap as unknown as ImageBitmap)
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
      fillStyle: '',
      fillRect: vi.fn(),
      drawImage: vi.fn(),
    } as unknown as CanvasRenderingContext2D)
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(
      (callback) => callback(null)
    )

    const error = await prepErrorOf(preparePortfolioImage(photoFile()))

    expect(error.code).toBe('decodeFailed')
    expect(bitmap.close).toHaveBeenCalledTimes(1)
  })
})
