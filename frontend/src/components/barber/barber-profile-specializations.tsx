import { useEffect, useRef, useState } from 'react'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { useI18n } from '@/i18n'
import {
  addBarberSpecialization,
  describeBarberProfileError,
  removeBarberSpecialization,
} from '@/lib/barber-profile'
import type { BarberProfileSpecialization } from '@/lib/catalog'
import { describeApiFailure } from '@/lib/errors'
import {
  fetchSpecializations,
  type SpecializationOption,
} from '@/lib/specializations'

/**
 * Специализации барбера: бейджи с «Убрать» и выбор из публичного справочника.
 *
 * Справочник ленивый (§8): он не запрашивается на монтировании страницы, а только
 * когда барбер впервые трогает выбор. Удаление специализации физическое — истории
 * за ней нет, в отличие от услуги.
 */

type CatalogueStatus = 'idle' | 'loading' | 'ready' | 'error'

export function BarberProfileSpecializations({
  barberProfileId,
  specializations,
  onChanged,
}: {
  barberProfileId: string
  specializations: readonly BarberProfileSpecialization[]
  onChanged: (message: string) => void
}) {
  const { t } = useI18n()
  const [status, setStatus] = useState<CatalogueStatus>('idle')
  const [options, setOptions] = useState<SpecializationOption[]>([])
  const [catalogueError, setCatalogueError] = useState<string | null>(null)
  const [selected, setSelected] = useState('')
  const [isAdding, setIsAdding] = useState(false)
  const [removingId, setRemovingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const version = useRef(0)

  useEffect(() => {
    return () => {
      version.current += 1
    }
  }, [])

  async function loadCatalogue(): Promise<void> {
    if (status === 'loading') {
      return
    }

    const current = version.current + 1
    version.current = current
    setStatus('loading')
    setCatalogueError(null)

    try {
      const items = await fetchSpecializations()

      if (version.current !== current) {
        return
      }

      setOptions(items)
      setStatus('ready')
    } catch (cause) {
      if (version.current !== current) {
        return
      }

      setCatalogueError(
        describeApiFailure(
          cause,
          t('Не удалось загрузить справочник специализаций.')
        )
      )
      setStatus('error')
    }
  }

  function ensureCatalogue(): void {
    if (status === 'idle') {
      void loadCatalogue()
    }
  }

  async function handleAdd(): Promise<void> {
    if (selected === '' || isAdding) {
      return
    }

    setIsAdding(true)
    setError(null)

    try {
      await addBarberSpecialization(barberProfileId, selected)
      setSelected('')
      onChanged(t('Специализация добавлена.'))
    } catch (cause) {
      setError(describeBarberProfileError(cause).message)
    } finally {
      setIsAdding(false)
    }
  }

  async function handleRemove(
    specialization: BarberProfileSpecialization
  ): Promise<void> {
    if (removingId !== null) {
      return
    }

    setRemovingId(specialization.specializationId)
    setError(null)

    try {
      await removeBarberSpecialization(
        barberProfileId,
        specialization.specializationId
      )
      onChanged(t('Специализация убрана.'))
    } catch (cause) {
      setError(describeBarberProfileError(cause).message)
    } finally {
      setRemovingId(null)
    }
  }

  const available = options.filter(
    (option) =>
      !specializations.some(
        (specialization) => specialization.specializationId === option.id
      )
  )

  return (
    <section aria-labelledby="barber-profile-specializations-heading">
      <Card>
        <CardHeader>
          <h2
            id="barber-profile-specializations-heading"
            className="font-heading text-base leading-snug font-medium tracking-tight"
          >
            {t('Специализации')}
          </h2>
        </CardHeader>

        <CardContent className="space-y-4">
          {specializations.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {t('Специализаций нет. По тегам вас не найдут в фильтре поиска.')}
            </p>
          ) : (
            <ul className="flex flex-wrap gap-2">
              {specializations.map((specialization) => (
                <li
                  key={specialization.id}
                  className="flex items-center gap-2 rounded-lg border py-0.5 pr-0.5 pl-3"
                >
                  <span className="text-sm">{specialization.name}</span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="h-11 text-destructive hover:text-destructive sm:h-8"
                    disabled={removingId !== null}
                    aria-busy={
                      removingId === specialization.specializationId
                    }
                    aria-label={t('Убрать специализацию {name}', {
                      name: specialization.name,
                    })}
                    onClick={() => {
                      void handleRemove(specialization)
                    }}
                  >
                    {t('Убрать')}
                  </Button>
                </li>
              ))}
            </ul>
          )}

          <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
            <div className="min-w-0 flex-1 space-y-1.5">
              <Label htmlFor="barber-profile-specialization">
                {t('Специализация')}
              </Label>
              <select
                id="barber-profile-specialization"
                value={selected}
                className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8"
                onFocus={ensureCatalogue}
                onClick={ensureCatalogue}
                onChange={(event) => {
                  setSelected(event.target.value)
                  setError(null)
                }}
              >
                <option value="" disabled>
                  {status === 'loading'
                    ? t('Загружаем справочник…')
                    : t('Выберите специализацию')}
                </option>
                {available.map((option) => (
                  <option key={option.id} value={option.id}>
                    {option.name}
                  </option>
                ))}
              </select>
            </div>

            <Button
              type="button"
              size="sm"
              className="h-11 sm:h-8"
              disabled={selected === '' || isAdding}
              aria-busy={isAdding}
              onClick={() => {
                void handleAdd()
              }}
            >
              {t('Добавить')}
            </Button>
          </div>

          {status === 'loading' && (
            <p role="status" className="text-sm text-muted-foreground">
              {t('Загружаем справочник…')}
            </p>
          )}

          {status === 'error' && (
            <div className="space-y-2">
              <p role="alert" className="text-sm text-destructive">
                {catalogueError}
              </p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-11 sm:h-8"
                onClick={() => {
                  void loadCatalogue()
                }}
              >
                {t('Повторить')}
              </Button>
            </div>
          )}

          {status === 'ready' && options.length === 0 && (
            <p className="text-sm text-muted-foreground">
              {t('Справочник специализаций пуст.')}
            </p>
          )}

          {status === 'ready' &&
            options.length > 0 &&
            available.length === 0 && (
              <p className="text-sm text-muted-foreground">
                {t('Все специализации из справочника уже выбраны.')}
              </p>
            )}

          {error && (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          )}
        </CardContent>
      </Card>
    </section>
  )
}
