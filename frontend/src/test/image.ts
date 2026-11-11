import { vi } from 'vitest'

/**
 * jsdom не загружает изображения, поэтому Base UI `AvatarImage` никогда не
 * переходит в состояние `loaded` и не монтирует `<img>`. Подменяем только
 * `window.Image`: тесту аватара нужен реальный `src` в разметке, а не сеть.
 *
 * `useImageLoadingStatus` создаёт `new Image()`, вешает `onload`/`onerror` и сразу
 * проверяет `complete`; фейк отвечает `false`, а `onload` зовёт следующим микрозаданием.
 */
export function stubImageLoading(): void {
  class LoadedImage {
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    complete = false
    naturalWidth = 1
    naturalHeight = 1
    crossOrigin: string | null = null
    referrerPolicy = ''
    sizes = ''
    srcset = ''
    private source = ''

    get src(): string {
      return this.source
    }

    set src(value: string) {
      this.source = value

      queueMicrotask(() => {
        if (this.source === value) {
          this.onload?.()
        }
      })
    }
  }

  vi.stubGlobal('Image', LoadedImage)
}
