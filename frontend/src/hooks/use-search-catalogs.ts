import { useCallback, useEffect, useRef, useState } from 'react'

import { fetchAdditionalServices } from '@/lib/additional-services'
import { t } from '@/i18n'
import { describeApiFailure } from '@/lib/errors'
import {
  fetchHairServices,
  type HairServiceOption,
} from '@/lib/hair-services'
import {
  fetchSpecializations,
  type SpecializationOption,
} from '@/lib/specializations'

export type SearchCatalogsStatus = 'loading' | 'ready' | 'error'

export type SearchCatalogsController = {
  status: SearchCatalogsStatus
  specializations: SpecializationOption[]
  hairServices: HairServiceOption[]
  /** Подсказки свободного поля «дополнительная услуга»; ошибка запроса их просто скрывает. */
  additionalServices: string[]
  /** Русский текст ошибки; английский `detail` наружу не выходит. */
  error: string | null
  retry: () => void
}

type CatalogsState = {
  status: SearchCatalogsStatus
  specializations: SpecializationOption[]
  hairServices: HairServiceOption[]
  additionalServices: string[]
  error: string | null
}

const CATALOGS_FALLBACK = 'Не удалось загрузить фильтры.'
const EMPTY: CatalogsState = {
  status: 'loading',
  specializations: [],
  hairServices: [],
  additionalServices: [],
  error: null,
}

/**
 * Справочники для фильтров витрины: специализации, услуги и подсказки
 * дополнительных услуг.
 *
 * Все три запроса уходят одним `Promise.all` на монтирование и отменяются вместе.
 * Подсказки дополнительные: свободное поле работает и без них, поэтому их сбой
 * превращается в пустой список, а не в общую ошибку фильтров. Номер поколения
 * защищает от StrictMode: первый, уже отменённый запрос не может оставить ошибку
 * или перезаписать результат второго.
 */
export function useSearchCatalogs(): SearchCatalogsController {
  const [state, setState] = useState<CatalogsState>(EMPTY)
  const [attempt, setAttempt] = useState(0)
  const versionRef = useRef(0)

  useEffect(() => {
    const version = versionRef.current + 1
    versionRef.current = version
    const controller = new AbortController()

    Promise.all([
      fetchSpecializations(controller.signal),
      fetchHairServices(controller.signal),
      fetchAdditionalServices(controller.signal).catch(() => [] as string[]),
    ])
      .then(([specializations, hairServices, additionalServices]) => {
        if (versionRef.current !== version) {
          return
        }

        setState({
          status: 'ready',
          specializations,
          hairServices,
          additionalServices,
          error: null,
        })
      })
      .catch((cause: unknown) => {
        if (versionRef.current !== version || controller.signal.aborted) {
          return
        }

        setState({
          status: 'error',
          specializations: [],
          hairServices: [],
          additionalServices: [],
          error: describeApiFailure(cause, t(CATALOGS_FALLBACK)),
        })
      })

    return () => {
      // Поздний ответ отменённого запроса не пройдёт проверку поколения.
      versionRef.current += 1
      controller.abort()
    }
  }, [attempt])

  const retry = useCallback(() => {
    // Пока идёт повтор, поля показывают «Загрузка…», а не старую ошибку.
    setState((previous) => ({ ...previous, status: 'loading', error: null }))
    setAttempt((previous) => previous + 1)
  }, [])

  return {
    status: state.status,
    specializations: state.specializations,
    hairServices: state.hairServices,
    additionalServices: state.additionalServices,
    error: state.error,
    retry,
  }
}
