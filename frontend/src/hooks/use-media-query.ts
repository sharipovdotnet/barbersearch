import { useEffect, useState } from 'react'

/**
 * `matchMedia` как состояние.
 *
 * Нужен ровно для одного: на широком экране сразу открыть первый отзыв, а на телефоне
 * оставить список — иначе выбор карточки в URL мгновенно заменил бы список деталью.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(() => window.matchMedia(query).matches)

  useEffect(() => {
    const media = window.matchMedia(query)
    const update = () => setMatches(media.matches)

    update()
    media.addEventListener('change', update)

    return () => media.removeEventListener('change', update)
  }, [query])

  return matches
}
