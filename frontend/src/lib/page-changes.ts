/**
 * Изменения одной страницы списка после тихого обновления.
 *
 * Кабинеты опрашивают сервер сами, но баннер должен появляться только тогда, когда
 * список действительно изменился: подпись «Обновлено в 14:30» обновляется молча.
 */
export type PageChange<T> = {
  /** Строки, которых на странице не было. */
  added: T[]
  /** Строки, которые со страницы исчезли. */
  removed: T[]
  /** Строки, оставшиеся на месте, но изменившиеся. */
  updated: { before: T; after: T }[]
}

/**
 * Сравнивает две версии страницы по стабильному id.
 *
 * Порядок строк — часть ответа сервера и здесь не сравнивается: перестановка без
 * новых id не считается изменением, иначе автопоиск новой записи врал бы на каждой
 * смене страницы. `hasChanged` задаёт экран: кабинет сравнивает только статус,
 * потому что остальные поля строки сервер не меняет.
 */
export function diffPageItems<T>(
  previous: readonly T[],
  next: readonly T[],
  getId: (item: T) => string,
  hasChanged: (before: T, after: T) => boolean
): PageChange<T> {
  const previousById = new Map(previous.map((item) => [getId(item), item]))
  const nextIds = new Set(next.map((item) => getId(item)))
  const added: T[] = []
  const updated: { before: T; after: T }[] = []

  for (const after of next) {
    const before = previousById.get(getId(after))

    if (before === undefined) {
      added.push(after)
    } else if (hasChanged(before, after)) {
      updated.push({ before, after })
    }
  }

  const removed = previous.filter((item) => !nextIds.has(getId(item)))

  return { added, removed, updated }
}
