import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { Loader2, MapPin, Search } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import {
  attachSalon,
  describeSalonAttachError,
  isSalonAttachStale,
} from '@/lib/barber-salons'
import { describeApiFailure } from '@/lib/errors'
import {
  fetchSalonDirectory,
  SALON_SEARCH_MIN_LENGTH,
  type SalonDirectoryItem,
} from '@/lib/salons'

const DEBOUNCE_MS = 300
const CATALOG_FALLBACK = 'Не удалось загрузить каталог.'

type CatalogStatus = 'idle' | 'loading' | 'ready' | 'error'

/**
 * Диалог выбора салона: поиск по публичному каталогу и привязка к профилю.
 *
 * Один компонент на два входа — «Мои салоны» и шаг онбординга. Поиск идёт с задержкой
 * 300 мс и только с двух символов; каждый новый запрос отменяет предыдущий
 * `AbortController`, чтобы ответ на старую строку не перетёр свежий.
 */
export function SalonPickerDialog({
  barberProfileId,
  hasSalons,
  trigger,
  onAttached,
  onStale,
}: {
  barberProfileId: string
  /** В профиле уже есть салон: тогда «сделать основным» — выбор пользователя. */
  hasSalons: boolean
  trigger?: ReactNode
  /** Привязка прошла: родитель перечитывает список и объявляет результат. */
  onAttached: () => void
  /** `409`/`404`: выбор устарел, список «Мои салоны» надо перечитать. */
  onStale: () => void
}) {
  const [isOpen, setIsOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [city, setCity] = useState('')
  const [status, setStatus] = useState<CatalogStatus>('idle')
  const [loadedQueryKey, setLoadedQueryKey] = useState<string | null>(null)
  const [results, setResults] = useState<SalonDirectoryItem[]>([])
  const [catalogError, setCatalogError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [makePrimary, setMakePrimary] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const isFirstSalon = !hasSalons
  const isPrimary = isFirstSalon || makePrimary
  const trimmedSearch = search.trim()
  const canSearch = trimmedSearch.length >= SALON_SEARCH_MIN_LENGTH
  const queryKey = `${city.trim()}\u0000${trimmedSearch}`
  // Пока новый запрос не ответил, показываем скелетон, а не прошлую выдачу:
  // производное значение не требует setState из эффекта.
  const effectiveStatus: CatalogStatus = !canSearch
    ? 'idle'
    : loadedQueryKey === queryKey
      ? status
      : 'loading'

  function resetForm() {
    setSearch('')
    setCity('')
    setStatus('idle')
    setLoadedQueryKey(null)
    setResults([])
    setCatalogError(null)
    setAttempt(0)
    setSelectedId(null)
    setMakePrimary(false)
    setSubmitError(null)
    setIsSubmitting(false)
  }

  function handleOpenChange(open: boolean) {
    setIsOpen(open)

    if (!open) {
      resetForm()
    }
  }

  useEffect(() => {
    if (!isOpen || !canSearch) {
      return
    }

    const controller = new AbortController()

    const timer = setTimeout(() => {
      setStatus('loading')
      setCatalogError(null)

      fetchSalonDirectory({ city, search: trimmedSearch, signal: controller.signal })
        .then((rows) => {
          if (controller.signal.aborted) {
            return
          }

          setResults(rows)
          setStatus('ready')
          setLoadedQueryKey(queryKey)
        })
        .catch((cause: unknown) => {
          if (controller.signal.aborted) {
            return
          }

          setResults([])
          setCatalogError(describeApiFailure(cause, CATALOG_FALLBACK))
          setStatus('error')
          setLoadedQueryKey(queryKey)
        })
    }, DEBOUNCE_MS)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [isOpen, canSearch, queryKey, city, trimmedSearch, attempt])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (selectedId === null) {
      return
    }

    setIsSubmitting(true)
    setSubmitError(null)

    try {
      await attachSalon(barberProfileId, {
        salonId: selectedId,
        isPrimary,
      })
      onAttached()
      setIsOpen(false)
      resetForm()
    } catch (cause) {
      setSubmitError(describeSalonAttachError(cause))

      // Дубликат и исчезнувший салон лечатся перечитыванием «Моих салонов»:
      // диалог остаётся открытым, чтобы выбрать другую строку.
      if (isSalonAttachStale(cause)) {
        onStale()
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  const announcement =
    effectiveStatus === 'loading'
      ? 'Ищем салоны…'
      : effectiveStatus === 'ready'
        ? results.length === 0
          ? 'Ничего не найдено'
          : `Найдено салонов: ${results.length}`
        : ''

  return (
    <Dialog open={isOpen} onOpenChange={handleOpenChange}>
      <DialogTrigger
        render={
          <Button type="button" className="h-11 gap-2 sm:h-8" />
        }
      >
        {trigger ?? (
          <>
            <MapPin data-icon="inline-start" aria-hidden />
            Добавить салон
          </>
        )}
      </DialogTrigger>

      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Добавить салон</DialogTitle>
          <DialogDescription>
            Найдите салон в каталоге и выберите его. Без салона расписание и
            запись не работают.
          </DialogDescription>
        </DialogHeader>

        <form className="flex flex-col gap-4" onSubmit={handleSubmit}>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field className="sm:col-span-2">
              <FieldLabel htmlFor="salon-picker-search">Поиск</FieldLabel>
              <div className="relative">
                <Search
                  className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
                  aria-hidden
                />
                <Input
                  id="salon-picker-search"
                  value={search}
                  autoFocus
                  placeholder="Название или адрес"
                  className="h-11 pl-9 sm:h-8"
                  onChange={(event) => {
                    setSearch(event.target.value)
                    setSelectedId(null)
                    setSubmitError(null)
                  }}
                />
              </div>
            </Field>

            <Field className="sm:col-span-2">
              <FieldLabel htmlFor="salon-picker-city">
                Город <span className="font-normal text-muted-foreground">— необязательно</span>
              </FieldLabel>
              <Input
                id="salon-picker-city"
                value={city}
                placeholder="Например, Екатеринбург"
                className="h-11 sm:h-8"
                onChange={(event) => {
                  setCity(event.target.value)
                  setSelectedId(null)
                  setSubmitError(null)
                }}
              />
            </Field>
          </div>

          <p role="status" aria-live="polite" className="sr-only">
            {announcement}
          </p>

          <fieldset className="min-w-0">
            <legend className="mb-2 text-sm font-medium">Результаты поиска</legend>

            {effectiveStatus === 'idle' && !canSearch && (
              <p className="text-sm text-muted-foreground">
                Начните вводить название или адрес — минимум{' '}
                {SALON_SEARCH_MIN_LENGTH} символа.
              </p>
            )}

            {effectiveStatus === 'loading' && (
              <div aria-busy="true">
                <span className="sr-only">Загружаем каталог…</span>
                <ul className="space-y-2">
                  {SKELETON_KEYS.map((key) => (
                    <li key={key} className="rounded-lg border p-3">
                      <Skeleton className="h-4 w-40" />
                      <Skeleton className="mt-2 h-3 w-56" />
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {effectiveStatus === 'error' && (
              <div className="space-y-3">
                <p role="alert" className="text-sm text-destructive">
                  {catalogError}
                </p>
                <Button
                  type="button"
                  variant="outline"
                  className="h-11 sm:h-8"
                  onClick={() => {
                    setLoadedQueryKey(null)
                    setAttempt((current) => current + 1)
                  }}
                >
                  Повторить
                </Button>
              </div>
            )}

            {effectiveStatus === 'ready' && results.length === 0 && (
              <p className="text-sm text-muted-foreground">Ничего не найдено.</p>
            )}

            {effectiveStatus === 'ready' && results.length > 0 && (
              <ul className="max-h-64 space-y-2 overflow-y-auto pr-1">
                {results.map((salon) => (
                  <li key={salon.id}>
                    <label
                      className={
                        'flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors focus-within:ring-3 focus-within:ring-ring/50 has-[:checked]:border-primary has-[:checked]:bg-primary/5'
                      }
                    >
                      <input
                        type="radio"
                        name="salon-picker-result"
                        value={salon.id}
                        checked={selectedId === salon.id}
                        className="mt-1 size-4 shrink-0 accent-primary"
                        onChange={() => {
                          setSelectedId(salon.id)
                          setSubmitError(null)
                        }}
                      />
                      <span className="min-w-0">
                        <span className="block font-medium break-words">
                          {salon.name}
                        </span>
                        <span className="mt-0.5 block text-sm break-words text-muted-foreground">
                          {salon.city} · {salon.address}
                        </span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            )}
          </fieldset>

          <Field orientation="horizontal">
            <Checkbox
              id="salon-picker-primary"
              checked={isPrimary}
              disabled={isFirstSalon || isSubmitting}
              onCheckedChange={(checked) => {
                setMakePrimary(checked)
              }}
            />
            <FieldLabel
              htmlFor="salon-picker-primary"
              className="font-normal text-muted-foreground"
            >
              {isFirstSalon
                ? 'Первый салон станет основным'
                : 'Сделать основным'}
            </FieldLabel>
          </Field>

          {submitError && (
            <p role="alert" className="text-sm text-destructive">
              {submitError}
            </p>
          )}

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              className="h-11 sm:h-8"
              onClick={() => {
                handleOpenChange(false)
              }}
            >
              Отмена
            </Button>
            <Button
              type="submit"
              className="h-11 sm:h-8"
              disabled={selectedId === null || isSubmitting}
            >
              {isSubmitting && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {isSubmitting ? 'Добавляем…' : 'Добавить'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

const SKELETON_KEYS = [
  'salon-catalog-skeleton-1',
  'salon-catalog-skeleton-2',
  'salon-catalog-skeleton-3',
]
