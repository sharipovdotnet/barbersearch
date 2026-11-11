/**
 * Доставка медиа: `srcset` для собственных `/api/media/...` файлов.
 *
 * Сервер отдаёт WebP-вариант по query-параметру `size` (D1): путь и имя файла
 * не меняются, меняется только представление. Поэтому `src` всегда остаётся
 * исходником — это фолбэк для старых браузеров и для `onError`-заглушки, — а
 * `srcSet` добавляется только там, где сервер действительно умеет вариант.
 *
 * Внешние легаси-URL (`https://…`), `data:` и `blob:` сервер не обслуживает:
 * для них возвращается один `src`. Строку с уже существующим `?` или `#` тоже
 * не трогаем: контракт такого не производит, а дописывать query в чужой URL
 * нельзя.
 *
 * Ширины в этом файле — часть контракта с backend: менять их можно только
 * вместе с `PhotoVariantWidths` (портфолио 256/512/1024, аватар 96/192/384).
 */

/** Набор атрибутов для `<img>`: исходник и, если возможно, варианты. */
export type MediaSource = { src: string; srcSet?: string; sizes?: string }

/** Ширины вариантов портфолио в пикселях; часть контракта с backend. */
export const PORTFOLIO_WIDTHS = { sm: 256, md: 512, lg: 1024 } as const

/** Ширины вариантов аватара в пикселях; часть контракта с backend. */
export const AVATAR_WIDTHS = { sm: 96, md: 192, lg: 384 } as const

/** Префиксы собственной раздачи: только они получают `srcSet`. */
const PORTFOLIO_PREFIX = '/api/media/portfolio/'
const AVATAR_PREFIX = '/api/media/avatars/'

/**
 * Размеры ячейки публичной галереи: 2/3/4 колонки (`grid-cols-2 sm:grid-cols-3
 * lg:grid-cols-4`) — это 50/33/25 vw соответственно.
 */
const GALLERY_SIZES =
  '(min-width: 1024px) 25vw, (min-width: 640px) 33vw, 50vw'

/**
 * `src`/`srcSet`/`sizes` для кадра портфолио.
 *
 * `sizes` по умолчанию описывает публичную сетку «Работы»; кабинет и админка
 * передают свои значения (ширина карточки или 80/96 px). Дескрипторы ширины —
 * `256w/512w/1024w`.
 */
export function portfolioPhotoSource(
  url: string,
  sizes: string = GALLERY_SIZES
): MediaSource {
  if (!supportsVariants(url, PORTFOLIO_PREFIX)) {
    return { src: url }
  }

  return {
    src: url,
    srcSet: [
      `${url}?size=sm ${PORTFOLIO_WIDTHS.sm}w`,
      `${url}?size=md ${PORTFOLIO_WIDTHS.md}w`,
      `${url}?size=lg ${PORTFOLIO_WIDTHS.lg}w`,
    ].join(', '),
    sizes,
  }
}

/**
 * `src`/`srcSet` для аватара.
 *
 * Размер задан CSS (`Avatar`), поэтому `sizes` не нужен: дескрипторы плотности
 * `1x/2x/3x` покрывают 32 px в шапке и 96 px в кабинете на любом DPR.
 */
export function avatarImageSource(url: string): MediaSource {
  if (!supportsVariants(url, AVATAR_PREFIX)) {
    return { src: url }
  }

  return {
    src: url,
    srcSet: [
      `${url}?size=sm 1x`,
      `${url}?size=md 2x`,
      `${url}?size=lg 3x`,
    ].join(', '),
  }
}

/** Наш URL без готового query/fragment: только у такого есть варианты. */
function supportsVariants(url: string, prefix: string): boolean {
  return (
    url.startsWith(prefix) && !url.includes('?') && !url.includes('#')
  )
}
