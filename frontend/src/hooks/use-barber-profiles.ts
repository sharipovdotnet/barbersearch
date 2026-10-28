import { useEffect, useMemo, useState } from 'react'

import {
  loadBarberProfile,
  readCachedBarberProfile,
  type BarberProfile,
} from '@/lib/catalog'

/**
 * Карточки барберов для списка броней.
 *
 * Брони приходят с идентификаторами, а не с именами, поэтому кабинет догружает
 * профили пачкой: по одному запросу на уникального барбера, с общим кэшем модуля.
 * Уже загруженное читается из кэша прямо во время рендера, поэтому после возврата
 * на страницу не бывает вспышки «Барбер / Услуга» до ответа сети.
 */
export function useBarberProfiles(ids: readonly string[]) {
  const key = [...new Set(ids)].sort().join('|')

  const cached = useMemo(() => {
    const known: Record<string, BarberProfile> = {}

    if (!key) {
      return known
    }

    for (const id of key.split('|')) {
      const profile = readCachedBarberProfile(id)

      if (profile) {
        known[id] = profile
      }
    }

    return known
  }, [key])

  const [loaded, setLoaded] = useState<Record<string, BarberProfile>>({})
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!key) {
      return
    }

    const missing = key
      .split('|')
      .filter((id) => readCachedBarberProfile(id) === null)

    if (missing.length === 0) {
      return
    }

    let isActive = true

    void Promise.allSettled(missing.map((id) => loadBarberProfile(id))).then(
      (results) => {
        if (!isActive) {
          return
        }

        const next: Record<string, BarberProfile> = {}
        let hasFailure = false

        results.forEach((result, index) => {
          if (result.status === 'fulfilled') {
            next[missing[index]] = result.value
          } else {
            hasFailure = true
          }
        })

        setLoaded((previous) => ({ ...previous, ...next }))
        setFailed(hasFailure)
      }
    )

    return () => {
      isActive = false
    }
  }, [key])

  const profiles = useMemo(
    () => ({ ...cached, ...loaded }),
    [cached, loaded]
  )

  return { profiles, failed }
}
