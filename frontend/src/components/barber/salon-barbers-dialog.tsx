import { useEffect, useState } from 'react'
import { Loader2, Search, Users } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'
import type { BarberSalonSummary } from '@/lib/barber-salons'
import { describeApiFailure } from '@/lib/errors'
import {
  SALON_BARBER_SEARCH_MAX_LENGTH,
  SALON_BARBER_SEARCH_MIN_LENGTH,
  attachSalonBarber,
  describeSalonBarberError,
  detachSalonBarber,
  fetchSalonBarbers,
  isSalonBarberActionStale,
  searchSalonBarberCandidates,
  type SalonBarber,
} from '@/lib/salon-barbers'

const DEBOUNCE_MS = 300
const LOAD_FALLBACK = 'Не удалось загрузить барберов салона.'
const SEARCH_FALLBACK = 'Не удалось найти барберов.'

type LoadStatus = 'loading' | 'ready' | 'error'
type SearchStatus = 'idle' | 'loading' | 'ready' | 'error'

/**
 * «Барберы салона»: владелец видит прикреплённых мастеров, ищет и добавляет новых,
 * убирает ошибочно добавленных.
 *
 * Список и подсказка поиска — два независимых чтения. Поиск идёт только с трёх
 * символов, с задержкой 300 мс и отменой предыдущего запроса: ответ на старую строку
 * не должен перетереть свежий. Владелец салона помечен бейджем и не имеет кнопки
 * «Убрать» — сервер всё равно отвечает `409`, но мёртвую кнопку UI не показывает.
 * После привязки или открепления список перечитывается: ответ `204` не несёт строк.
 */
export function SalonBarbersDialog({
  salon,
  onClose,
}: {
  salon: BarberSalonSummary
  onClose: () => void
}) {
  const [status, setStatus] = useState<LoadStatus>('loading')
  const [barbers, setBarbers] = useState<SalonBarber[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)

  const [search, setSearch] = useState('')
  const [searchStatus, setSearchStatus] = useState<SearchStatus>('idle')
  const [searchError, setSearchError] = useState<string | null>(null)
  const [candidates, setCandidates] = useState<SalonBarber[]>([])
  const [loadedQuery, setLoadedQuery] = useState<string | null>(null)
  const [searchAttempt, setSearchAttempt] = useState(0)

  const [addingId, setAddingId] = useState<string | null>(null)
  const [removingId, setRemovingId] = useState<string | null>(null)
  const [pendingRemovalId, setPendingRemovalId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState<string | null>(null)

  const isBusy = addingId !== null || removingId !== null

  const trimmedSearch = search.trim()
  const canSearch =
    trimmedSearch.length >= SALON_BARBER_SEARCH_MIN_LENGTH &&
    trimmedSearch.length <= SALON_BARBER_SEARCH_MAX_LENGTH
  const effectiveSearchStatus: SearchStatus = !canSearch
    ? 'idle'
    : loadedQuery === trimmedSearch
      ? searchStatus
      : 'loading'

  useEffect(() => {
    const controller = new AbortController()

    fetchSalonBarbers(salon.salonId, controller.signal)
      .then((rows) => {
        if (controller.signal.aborted) {
          return
        }

        setBarbers(rows)
        setStatus('ready')
        setIsRefreshing(false)
      })
      .catch((cause: unknown) => {
        if (controller.signal.aborted) {
          return
        }

        setLoadError(describeApiFailure(cause, LOAD_FALLBACK))
        setStatus((current) => (current === 'loading' ? 'error' : 'ready'))
        setIsRefreshing(false)
      })

    return () => {
      controller.abort()
    }
  }, [salon.salonId, reloadKey])

  useEffect(() => {
    if (!canSearch) {
      return
    }

    const controller = new AbortController()

    const timer = setTimeout(() => {
      setSearchStatus('loading')
      setSearchError(null)

      searchSalonBarberCandidates(salon.salonId, trimmedSearch, controller.signal)
        .then((rows) => {
          if (controller.signal.aborted) {
            return
          }

          setCandidates(rows)
          setSearchStatus('ready')
          setLoadedQuery(trimmedSearch)
        })
        .catch((cause: unknown) => {
          if (controller.signal.aborted) {
            return
          }

          setCandidates([])
          setSearchError(describeApiFailure(cause, SEARCH_FALLBACK))
          setSearchStatus('error')
          setLoadedQuery(trimmedSearch)
        })
    }, DEBOUNCE_MS)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [salon.salonId, canSearch, trimmedSearch, searchAttempt])

  function reload() {
    setLoadError(null)
    setIsRefreshing(true)
    setReloadKey((current) => current + 1)
  }

  function retryLoad() {
    setLoadError(null)
    setStatus('loading')
    setIsRefreshing(false)
    setReloadKey((current) => current + 1)
  }

  function resetSearch() {
    setSearch('')
    setCandidates([])
    setSearchError(null)
    setSearchStatus('idle')
    setLoadedQuery(null)
  }

  async function handleAdd(barber: SalonBarber) {
    if (addingId !== null) {
      return
    }

    setAddingId(barber.barberProfileId)
    setActionError(null)
    setAnnouncement(null)

    try {
      await attachSalonBarber(salon.salonId, barber.barberProfileId)
      setAnnouncement(`Барбер добавлен: ${barber.displayName}.`)
      resetSearch()
      reload()
    } catch (cause) {
      setActionError(
        describeSalonBarberError(
          cause,
          'Не удалось прикрепить барбера. Попробуйте ещё раз.'
        )
      )

      if (isSalonBarberActionStale(cause)) {
        reload()
        setSearchAttempt((current) => current + 1)
      }
    } finally {
      setAddingId(null)
    }
  }

  async function handleRemove(barber: SalonBarber) {
    if (removingId !== null) {
      return
    }

    setRemovingId(barber.barberProfileId)
    setActionError(null)
    setAnnouncement(null)

    try {
      await detachSalonBarber(salon.salonId, barber.barberProfileId)
      setAnnouncement(`Барбер убран из салона: ${barber.displayName}.`)
      setPendingRemovalId(null)
      reload()

      if (canSearch) {
        setSearchAttempt((current) => current + 1)
      }
    } catch (cause) {
      setActionError(
        describeSalonBarberError(
          cause,
          'Не удалось убрать барбера. Попробуйте ещё раз.'
        )
      )

      if (isSalonBarberActionStale(cause)) {
        reload()
      }
    } finally {
      setRemovingId(null)
    }
  }

  const searchAnnouncement =
    effectiveSearchStatus === 'loading'
      ? 'Ищем барберов…'
      : effectiveSearchStatus === 'ready'
        ? candidates.length === 0
          ? 'Ничего не найдено'
          : `Найдено барберов: ${candidates.length}`
        : ''

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !isBusy) {
          onClose()
        }
      }}
    >
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Барберы салона</DialogTitle>
          <DialogDescription>
            {salon.name}. Добавьте мастера, который работает в этом салоне, или
            уберите его из состава.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <section aria-labelledby="salon-barbers-current-heading" className="min-w-0">
            <h3
              id="salon-barbers-current-heading"
              className="text-sm font-medium"
            >
              Работают в салоне
            </h3>

            {status === 'loading' && (
              <div aria-busy="true" className="mt-2">
                <span className="sr-only">Загружаем барберов…</span>
                <ul className="space-y-2">
                  {['salon-barbers-skeleton-1', 'salon-barbers-skeleton-2'].map(
                    (key) => (
                      <li key={key} aria-hidden className="rounded-lg border p-3">
                        <Skeleton className="h-4 w-40" />
                        <Skeleton className="mt-2 h-3 w-56" />
                      </li>
                    )
                  )}
                </ul>
              </div>
            )}

            {status === 'error' && (
              <div className="mt-2 space-y-3">
                <p role="alert" className="text-sm text-destructive">
                  {loadError ?? LOAD_FALLBACK}
                </p>
                <Button
                  type="button"
                  variant="outline"
                  className="h-11 sm:h-8"
                  onClick={retryLoad}
                >
                  Повторить
                </Button>
              </div>
            )}

            {status === 'ready' && (
              <>
                {isRefreshing && (
                  <p
                    role="status"
                    className="mt-2 flex items-center gap-2 text-sm text-muted-foreground"
                  >
                    <Loader2 className="size-4 animate-spin" aria-hidden />
                    Обновляем список…
                  </p>
                )}

                {loadError && (
                  <p role="alert" className="mt-2 text-sm text-destructive">
                    {loadError}
                  </p>
                )}

                {barbers.length === 0 ? (
                  <p className="mt-2 text-sm text-muted-foreground">
                    В салоне пока нет барберов.
                  </p>
                ) : (
                  <ul className="mt-2 space-y-2">
                    {barbers.map((barber) => (
                      <li
                        key={barber.barberProfileId}
                        className="rounded-lg border p-3"
                      >
                        <BarberSummary
                          barber={barber}
                          isOwner={
                            salon.ownerUserId !== null &&
                            barber.userId === salon.ownerUserId
                          }
                        />

                        {salon.ownerUserId !== null &&
                          barber.userId !== salon.ownerUserId &&
                          (pendingRemovalId === barber.barberProfileId ? (
                            <div
                              role="alert"
                              className="mt-3 rounded-lg bg-destructive/10 p-3"
                            >
                              <p className="text-sm">
                                Убрать {barber.displayName} из салона? Его
                                расписание в этом салоне останется историей, но
                                новые записи станут недоступны.
                              </p>
                              <div className="mt-2 flex flex-wrap gap-2">
                                <Button
                                  type="button"
                                  variant="destructive"
                                  size="sm"
                                  className="h-11 sm:h-8"
                                  disabled={isBusy}
                                  onClick={() => {
                                    void handleRemove(barber)
                                  }}
                                >
                                  {removingId === barber.barberProfileId && (
                                    <Loader2
                                      data-icon="inline-start"
                                      aria-hidden
                                      className="animate-spin"
                                    />
                                  )}
                                  Убрать
                                </Button>
                                <Button
                                  type="button"
                                  variant="outline"
                                  size="sm"
                                  className="h-11 sm:h-8"
                                  disabled={isBusy}
                                  onClick={() => {
                                    setPendingRemovalId(null)
                                  }}
                                >
                                  Отмена
                                </Button>
                              </div>
                            </div>
                          ) : (
                            <Button
                              type="button"
                              variant="outline"
                              size="sm"
                              className="mt-3 h-11 sm:h-8"
                              disabled={isBusy}
                              aria-label={`Убрать ${barber.displayName} из салона`}
                              onClick={() => {
                                setPendingRemovalId(barber.barberProfileId)
                                setActionError(null)
                              }}
                            >
                              Убрать
                            </Button>
                          ))}
                      </li>
                    ))}
                  </ul>
                )}
              </>
            )}
          </section>

          <Separator />

          <section aria-labelledby="salon-barbers-add-heading" className="min-w-0">
            <h3 id="salon-barbers-add-heading" className="text-sm font-medium">
              Добавить барбера
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Найдите мастера по имени или email. Минимум{' '}
              {SALON_BARBER_SEARCH_MIN_LENGTH} символа.
            </p>

            <div className="mt-3 flex flex-col gap-3">
              <Field>
                <FieldLabel htmlFor="salon-barbers-search">
                  Поиск барбера
                </FieldLabel>
                <div className="relative">
                  <Search
                    className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
                    aria-hidden
                  />
                  <Input
                    id="salon-barbers-search"
                    value={search}
                    maxLength={SALON_BARBER_SEARCH_MAX_LENGTH}
                    placeholder="Имя или email"
                    className="h-11 pl-9 sm:h-8"
                    onChange={(event) => {
                      setSearch(event.target.value)
                      setActionError(null)
                    }}
                  />
                </div>
              </Field>

              <p role="status" aria-live="polite" className="sr-only">
                {searchAnnouncement}
              </p>

              {!canSearch && (
                <p className="text-sm text-muted-foreground">
                  Введите хотя бы {SALON_BARBER_SEARCH_MIN_LENGTH} символа.
                </p>
              )}

              {effectiveSearchStatus === 'loading' && (
                <div aria-busy="true">
                  <span className="sr-only">Загружаем результаты…</span>
                  <ul className="space-y-2">
                    {['salon-barber-search-1', 'salon-barber-search-2'].map(
                      (key) => (
                        <li key={key} aria-hidden className="rounded-lg border p-3">
                          <Skeleton className="h-4 w-40" />
                          <Skeleton className="mt-2 h-3 w-56" />
                        </li>
                      )
                    )}
                  </ul>
                </div>
              )}

              {effectiveSearchStatus === 'error' && (
                <div className="space-y-2">
                  <p role="alert" className="text-sm text-destructive">
                    {searchError ?? SEARCH_FALLBACK}
                  </p>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="h-11 sm:h-8"
                    onClick={() => {
                      setLoadedQuery(null)
                      setSearchAttempt((current) => current + 1)
                    }}
                  >
                    Повторить
                  </Button>
                </div>
              )}

              {effectiveSearchStatus === 'ready' && candidates.length === 0 && (
                <p className="text-sm text-muted-foreground">
                  Ничего не найдено. Уточните имя или email.
                </p>
              )}

              {effectiveSearchStatus === 'ready' && candidates.length > 0 && (
                <ul className="space-y-2">
                  {candidates.map((barber) => (
                    <li
                      key={barber.barberProfileId}
                      className="flex flex-wrap items-start justify-between gap-3 rounded-lg border p-3"
                    >
                      <BarberSummary barber={barber} />
                      <Button
                        type="button"
                        size="sm"
                        className="h-11 sm:h-8"
                        disabled={isBusy}
                        aria-label={`Добавить барбера ${barber.displayName}`}
                        onClick={() => {
                          void handleAdd(barber)
                        }}
                      >
                        {addingId === barber.barberProfileId && (
                          <Loader2
                            data-icon="inline-start"
                            aria-hidden
                            className="animate-spin"
                          />
                        )}
                        {addingId === barber.barberProfileId
                          ? 'Добавляем…'
                          : 'Добавить'}
                      </Button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </section>

          {actionError && (
            <p role="alert" className="text-sm text-destructive">
              {actionError}
            </p>
          )}

          {announcement && (
            <p role="status" className="text-sm text-muted-foreground">
              {announcement}
            </p>
          )}
        </div>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-8"
            disabled={isBusy}
            onClick={onClose}
          >
            Готово
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function BarberSummary({
  barber,
  isOwner = false,
}: {
  barber: SalonBarber
  isOwner?: boolean
}) {
  return (
    <div className="min-w-0">
      <div className="flex flex-wrap items-center gap-2">
        <Users className="size-4 shrink-0 text-muted-foreground" aria-hidden />
        <span className="font-medium break-words">{barber.displayName}</span>
        {isOwner && <Badge variant="secondary">Владелец</Badge>}
        {!barber.isVerified && <Badge variant="outline">На проверке</Badge>}
        {!barber.isActive && <Badge variant="destructive">Выключен</Badge>}
      </div>
      <p className="mt-0.5 text-sm break-words text-muted-foreground">
        {barber.email}
      </p>
    </div>
  )
}
