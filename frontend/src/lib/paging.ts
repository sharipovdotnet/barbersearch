/**
 * Курсорная страница списка.
 *
 * `nextCursor` — непрозрачная строка из ответа: клиент передаёт её обратно как есть
 * и не разбирает. `null` означает, что следующей страницы нет.
 */
export type Page<T> = {
  items: T[]
  nextCursor: string | null
}

/** Тире в диапазоне — среднее, как в макете и в остальных подписях страниц. */
const RANGE_DASH = '–'

/**
 * Подпись диапазона видимых строк: «11–20 из 32», а без общего числа — «21–40».
 *
 * Общее число приходит только там, где сервер его отдаёт (`reviewsCount` профиля).
 * Выдумывать total из длины страницы нельзя: последняя страница неполная.
 */
export function formatPageRange(
  offset: number,
  count: number,
  total?: number
): string {
  if (count <= 0) {
    return total === undefined ? '' : `0 из ${total}`
  }

  const range = `${offset + 1}${RANGE_DASH}${offset + count}`

  return total === undefined ? range : `${range} из ${total}`
}
