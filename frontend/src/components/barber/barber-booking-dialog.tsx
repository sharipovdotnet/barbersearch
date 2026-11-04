import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Check, Loader2, Plus, TriangleAlert } from 'lucide-react'

import { BarberSlotOptions } from '@/components/barber/barber-slot-options'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { useBarberBookingOptions } from '@/hooks/use-barber-booking-options'
import {
  useBarberClients,
  type BarberClientsStatus,
} from '@/hooks/use-barber-clients'
import { useBarberSlots } from '@/hooks/use-barber-slots'
import { createBarberBooking } from '@/lib/barber-booking-create'
import {
  formatBarberClientName,
  MAX_BARBER_CLIENT_SEARCH,
  MIN_BARBER_CLIENT_SEARCH,
  type BarberClient,
} from '@/lib/barber-clients'
import {
  addDays,
  isSalonDate,
  todayInTimeZone,
  type BarberSlot,
} from '@/lib/barber-slots'
import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'
import { describeBarberBookingCreateFailure } from '@/lib/errors'
import {
  formatPrice,
  formatSalonDate,
  formatTime,
} from '@/lib/format'
import { cn } from '@/lib/utils'

/** UI-лимит комментария совпадает с публичной записью. */
const MAX_NOTE_LENGTH = 500

/** Горизонт выбора даты: как на публичной карточке, 30 дней. */
const DATE_HORIZON_DAYS = 30

const SELECT_CLASSES =
  'h-11 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 text-base transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-9 md:text-sm'

const CLIENT_SKELETON_KEYS = ['client-skeleton-1', 'client-skeleton-2']

type BarberBookingDialogProps = {
  /** Профиль из сессии: чужой профиль сервер не примет. */
  barberProfileId: string
  /** Вызывается после `201`: список записей и объявление обновляет экран. */
  onCreated: (clientName: string) => void
}

/**
 * Диалог «Записать клиента» в кабинете барбера.
 *
 * Барбер ищет зарегистрированного клиента по email или телефону, выбирает услугу,
 * салон, дату и свободное время, затем отправляет `POST api/barber/bookings`.
 * Сервер создаёт бронь сразу подтверждённой; клиент увидит её в своём кабинете.
 * Пока запрос в полёте, диалог нельзя закрыть: иначе повтор после закрытия создал бы
 * вторую попытку с новым ключом идемпотентности.
 */
export function BarberBookingDialog({
  barberProfileId,
  onCreated,
}: BarberBookingDialogProps) {
  const [open, setOpen] = useState(false)
  const [isPending, setIsPending] = useState(false)

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && isPending) {
          return
        }

        setOpen(next)
      }}
    >
      <DialogTrigger
        render={<Button className="h-11 w-full sm:h-8 sm:w-auto" />}
      >
        <Plus data-icon="inline-start" aria-hidden />
        Записать клиента
      </DialogTrigger>

      <DialogContent
        className="max-h-[calc(100svh-2rem)] overflow-y-auto sm:max-w-lg"
        showCloseButton={!isPending}
      >
        {open && (
          <BarberBookingForm
            barberProfileId={barberProfileId}
            isPending={isPending}
            onPendingChange={setIsPending}
            onClose={() => {
              setOpen(false)
            }}
            onCreated={(clientName) => {
              setOpen(false)
              onCreated(clientName)
            }}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

type BarberBookingFormProps = {
  barberProfileId: string
  isPending: boolean
  onPendingChange: (isPending: boolean) => void
  onClose: () => void
  onCreated: (clientName: string) => void
}

function BarberBookingForm({
  barberProfileId,
  isPending,
  onPendingChange,
  onClose,
  onCreated,
}: BarberBookingFormProps) {
  const options = useBarberBookingOptions()
  const [search, setSearch] = useState('')
  const clients = useBarberClients(search)
  const [client, setClient] = useState<BarberClient | null>(null)
  const [serviceId, setServiceId] = useState('')
  const [salonId, setSalonId] = useState('')
  const [date, setDate] = useState('')
  const [slotStartAt, setSlotStartAt] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [failure, setFailure] = useState<string | null>(null)
  const [idempotencyKey, setIdempotencyKey] = useState(() =>
    crypto.randomUUID()
  )
  const submittingRef = useRef(false)
  const errorRef = useRef<HTMLDivElement>(null)

  const services = options.options?.services ?? []
  const salons = options.options?.salons ?? []
  const service =
    services.find((item) => item.barberServiceId === serviceId) ?? null
  const salon =
    salons.find((item) => item.salonId === salonId) ??
    salons.find((item) => item.isPrimary) ??
    salons[0] ??
    null
  const today = salon ? todayInTimeZone(salon.timeZoneId) : ''
  const maxDate = today !== '' ? addDays(today, DATE_HORIZON_DAYS) : ''
  const dateValue = date !== '' ? date : today
  const isDateValid =
    today !== '' &&
    maxDate !== '' &&
    isSalonDate(dateValue) &&
    dateValue >= today &&
    dateValue <= maxDate
  const canLoadSlots =
    options.status === 'ready' &&
    salon !== null &&
    service !== null &&
    isDateValid

  const slots = useBarberSlots({
    barberProfileId,
    date: canLoadSlots ? dateValue : '',
    hairServiceId: service?.hairServiceId ?? null,
    salonId: salon?.salonId ?? '',
    own: true,
  })

  const selectedSlot =
    slots.slots?.slots.find((item) => item.startAt === slotStartAt) ?? null
  const canSubmit =
    client !== null &&
    service !== null &&
    salon !== null &&
    selectedSlot !== null &&
    !isPending

  useEffect(() => {
    if (failure !== null) {
      errorRef.current?.focus()
    }
  }, [failure])

  /**
   * Любая смена параметров после ошибки начинает новую попытку: старый ключ
   * идемпотентности вернул бы уже созданную бронь с прежними полями.
   */
  function resetAttempt() {
    setFailure(null)
    setSlotStartAt(null)
    setIdempotencyKey(crypto.randomUUID())
  }

  function handleSearchChange(value: string) {
    setSearch(value)
    setClient(null)
    setFailure(null)
  }

  function handleSelectClient(next: BarberClient) {
    setClient(next)
    setFailure(null)
  }

  function handleServiceChange(value: string) {
    setServiceId(value)
    resetAttempt()
  }

  function handleSalonChange(value: string) {
    const nextSalon = salons.find((item) => item.salonId === value) ?? salons[0]
    const nextToday = nextSalon ? todayInTimeZone(nextSalon.timeZoneId) : ''

    setSalonId(value)
    setDate((current) =>
      current !== '' && nextToday !== '' && current >= nextToday
        ? current
        : nextToday
    )
    resetAttempt()
  }

  function handleDateChange(value: string) {
    setDate(value)
    resetAttempt()
  }

  function handleSelectSlot(slot: BarberSlot) {
    setSlotStartAt(slot.startAt)
    setFailure(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (
      submittingRef.current ||
      !canSubmit ||
      client === null ||
      service === null ||
      salon === null ||
      selectedSlot === null
    ) {
      return
    }

    submittingRef.current = true
    onPendingChange(true)
    setFailure(null)

    try {
      await createBarberBooking({
        customerId: client.id,
        barberServiceId: service.barberServiceId,
        salonId: salon.salonId,
        startAt: selectedSlot.startAt,
        endAt: selectedSlot.endAt,
        idempotencyKey,
        customerNote: note.trim() === '' ? null : note.trim(),
      })

      onPendingChange(false)
      onCreated(formatBarberClientName(client))
    } catch (cause) {
      const next = describeBarberBookingCreateFailure(
        cause,
        'Не удалось записать клиента.'
      )

      setFailure(next.message)
      onPendingChange(false)

      if (next.reloadSlots) {
        setSlotStartAt(null)
        slots.reload()
      }
    } finally {
      submittingRef.current = false
    }
  }

  return (
    <form
      noValidate
      aria-busy={isPending}
      className="space-y-4"
      onSubmit={handleSubmit}
    >
      <DialogHeader>
        <DialogTitle>Записать клиента</DialogTitle>
        <DialogDescription>
          Запись создаётся сразу подтверждённой и появится в кабинете клиента.
        </DialogDescription>
      </DialogHeader>

      {options.status === 'loading' && <OptionsSkeleton />}

      {options.status === 'error' && (
        <OptionsError
          message={options.error ?? 'Не удалось загрузить услуги и салоны.'}
          onRetry={options.retry}
        />
      )}

      {options.status === 'ready' && (
        <>
          <ClientPicker
            search={search}
            status={clients.status}
            clients={clients.clients}
            error={clients.error}
            selected={client}
            onSearchChange={handleSearchChange}
            onSelect={handleSelectClient}
            onRetry={clients.retry}
          />

          <div className="grid gap-3 sm:grid-cols-2">
            <ServiceField
              services={services}
              value={serviceId}
              disabled={isPending}
              onChange={handleServiceChange}
            />

            <SalonField
              salons={salons}
              value={salon?.salonId ?? ''}
              disabled={isPending}
              onChange={handleSalonChange}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="barber-booking-date">Дата</Label>
            <Input
              id="barber-booking-date"
              type="date"
              min={today}
              max={maxDate}
              value={dateValue}
              disabled={isPending || salon === null}
              className="h-11 sm:h-9"
              onChange={(event) => handleDateChange(event.target.value)}
            />
            {salon !== null && (
              <p className="text-xs text-muted-foreground">
                Время — местное для салона «{salon.name}».
              </p>
            )}
          </div>

          <div className="space-y-2">
            <p className="text-sm font-medium">Свободное время</p>

            {service === null && (
              <p className="text-sm text-muted-foreground">
                Сначала выберите услугу — от её длительности строится сетка.
              </p>
            )}

            {service !== null && !isDateValid && (
              <p className="text-sm text-muted-foreground">Выберите дату.</p>
            )}

            {service !== null && isDateValid && (
              <>
                {slots.status === 'loading' && (
                  <div aria-busy="true" className="space-y-2">
                    <p role="status" className="sr-only">
                      Загружаем свободное время…
                    </p>
                    <SlotsSkeleton />
                  </div>
                )}

                {slots.status === 'ready' && slots.slots !== null && (
                  <BarberSlotOptions
                    slots={slots.slots}
                    selectable
                    selectedStartAt={slotStartAt}
                    onSelect={handleSelectSlot}
                    name="barber-booking-slot"
                  />
                )}

                {slots.status === 'error' && (
                  <div
                    role="alert"
                    className="flex flex-col gap-3 rounded-lg border border-destructive/30 p-3 sm:flex-row sm:items-center sm:justify-between"
                  >
                    <p className="flex items-start gap-2 text-sm text-muted-foreground">
                      <TriangleAlert
                        className="mt-0.5 size-4 shrink-0 text-destructive"
                        aria-hidden
                      />
                      <span>
                        {slots.error ?? 'Не удалось загрузить свободное время.'}
                      </span>
                    </p>
                    <Button
                      type="button"
                      variant="outline"
                      className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
                      onClick={slots.retry}
                    >
                      Повторить
                    </Button>
                  </div>
                )}

                {slots.status === 'unavailable' && (
                  <p role="status" className="text-sm text-muted-foreground">
                    {slots.error}
                  </p>
                )}
              </>
            )}
          </div>

          {selectedSlot !== null && service !== null && salon !== null && (
            <div className="space-y-1 rounded-lg border bg-muted/30 p-3 text-sm">
              <p className="font-medium">
                {formatSalonDate(dateValue)},{' '}
                <span className="tabular-nums">
                  {formatTime(selectedSlot.startAt, salon.timeZoneId)}–
                  {formatTime(selectedSlot.endAt, salon.timeZoneId)}
                </span>
              </p>
              <p className="text-muted-foreground">
                {service.name} · {salon.name} · {formatPrice(service.price)}
              </p>
            </div>
          )}

          <div className="space-y-1.5">
            <Label htmlFor="barber-booking-note">
              Комментарий{' '}
              <span className="font-normal text-muted-foreground">
                — необязательно
              </span>
            </Label>
            <Textarea
              id="barber-booking-note"
              name="customerNote"
              value={note}
              maxLength={MAX_NOTE_LENGTH}
              disabled={isPending}
              placeholder="Пожелания клиента или заметка для визита"
              onChange={(event) => setNote(event.target.value)}
            />
            <p className="text-right text-xs text-muted-foreground tabular-nums">
              {note.length} / {MAX_NOTE_LENGTH}
            </p>
          </div>
        </>
      )}

      {failure !== null && (
        <div
          ref={errorRef}
          role="alert"
          tabIndex={-1}
          className="flex items-start gap-2 rounded-lg border border-destructive/30 p-3 text-sm outline-none"
        >
          <TriangleAlert
            className="mt-0.5 size-4 shrink-0 text-destructive"
            aria-hidden
          />
          <span>{failure}</span>
        </div>
      )}

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          disabled={isPending}
          onClick={onClose}
        >
          Отмена
        </Button>
        <Button
          type="submit"
          className="h-11 sm:h-8"
          disabled={!canSubmit || options.status !== 'ready'}
          aria-busy={isPending}
        >
          {isPending && (
            <Loader2
              data-icon="inline-start"
              aria-hidden
              className="animate-spin"
            />
          )}
          {isPending ? 'Записываем…' : 'Записать'}
        </Button>
      </DialogFooter>
    </form>
  )
}

function ClientPicker({
  search,
  status,
  clients,
  error,
  selected,
  onSearchChange,
  onSelect,
  onRetry,
}: {
  search: string
  status: BarberClientsStatus
  clients: BarberClient[]
  error: string | null
  selected: BarberClient | null
  onSearchChange: (value: string) => void
  onSelect: (client: BarberClient) => void
  onRetry: () => void
}) {
  const hintId = 'barber-client-search-hint'
  const isTooShort = search.trim().length < MIN_BARBER_CLIENT_SEARCH

  return (
    <div className="space-y-2">
      <Label htmlFor="barber-client-search">Клиент</Label>
      <Input
        id="barber-client-search"
        type="search"
        value={search}
        autoComplete="off"
        maxLength={MAX_BARBER_CLIENT_SEARCH}
        placeholder="Email или телефон"
        aria-describedby={hintId}
        onChange={(event) => onSearchChange(event.target.value)}
      />
      <p id={hintId} className="text-xs text-muted-foreground">
        Минимум {MIN_BARBER_CLIENT_SEARCH} символа. Записать можно только
        зарегистрированного клиента.
      </p>

      {selected !== null && (
        <p role="status" className="text-sm">
          Выбран:{' '}
          <span className="font-medium">{formatBarberClientName(selected)}</span>
        </p>
      )}

      {isTooShort && (
        <p className="text-sm text-muted-foreground">
          Введите начало email или номер телефона.
        </p>
      )}

      {!isTooShort && status === 'loading' && (
        <ul aria-hidden="true" className="space-y-1">
          {CLIENT_SKELETON_KEYS.map((key) => (
            <li key={key}>
              <Skeleton className="h-14 w-full rounded-lg" />
            </li>
          ))}
        </ul>
      )}

      {!isTooShort && status === 'error' && (
        <div
          role="alert"
          className="flex flex-col gap-2 rounded-lg border border-destructive/30 p-3 sm:flex-row sm:items-center sm:justify-between"
        >
          <p className="text-sm text-muted-foreground">
            {error ?? 'Не удалось найти клиентов.'}
          </p>
          <Button
            type="button"
            variant="outline"
            className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
            onClick={onRetry}
          >
            Повторить
          </Button>
        </div>
      )}

      {!isTooShort && status === 'ready' && clients.length === 0 && (
        <p className="text-sm text-muted-foreground">
          Клиенты не найдены. Уточните email или телефон.
        </p>
      )}

      {!isTooShort && status === 'ready' && clients.length > 0 && (
        <ul
          aria-label="Найденные клиенты"
          className="max-h-44 space-y-1 overflow-y-auto rounded-lg border p-1"
        >
          {clients.map((client) => {
            const isSelected = selected?.id === client.id

            return (
              <li key={client.id}>
                <button
                  type="button"
                  aria-pressed={isSelected}
                  className={cn(
                    'flex w-full items-start justify-between gap-2 rounded-md px-2 py-2 text-left transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50',
                    isSelected ? 'bg-primary/5' : 'hover:bg-muted/60'
                  )}
                  onClick={() => onSelect(client)}
                >
                  <span className="min-w-0 space-y-0.5">
                    <span className="block text-sm font-medium">
                      {formatBarberClientName(client)}
                    </span>
                    <span className="block truncate text-xs text-muted-foreground">
                      {client.email}
                    </span>
                    {client.phoneNumber !== null && (
                      <span className="block text-xs text-muted-foreground">
                        {client.phoneNumber}
                      </span>
                    )}
                  </span>
                  {isSelected && (
                    <Check
                      className="mt-0.5 size-4 shrink-0 text-primary"
                      aria-hidden
                    />
                  )}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

function ServiceField({
  services,
  value,
  disabled,
  onChange,
}: {
  services: BarberProfileService[]
  value: string
  disabled: boolean
  onChange: (value: string) => void
}) {
  if (services.length === 0) {
    return (
      <div className="space-y-1.5">
        <p className="text-sm font-medium">Услуга</p>
        <p className="text-sm text-muted-foreground">
          У вас нет активных услуг — добавьте их в профиле.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-1.5">
      <Label htmlFor="barber-booking-service">Услуга</Label>
      <select
        id="barber-booking-service"
        value={value}
        disabled={disabled}
        className={SELECT_CLASSES}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">Выберите услугу</option>
        {services.map((service) => (
          <option key={service.barberServiceId} value={service.barberServiceId}>
            {service.name} · {service.durationMinutes} мин ·{' '}
            {formatPrice(service.price)}
          </option>
        ))}
      </select>
    </div>
  )
}

function SalonField({
  salons,
  value,
  disabled,
  onChange,
}: {
  salons: BarberProfileSalon[]
  value: string
  disabled: boolean
  onChange: (value: string) => void
}) {
  if (salons.length === 0) {
    return (
      <div className="space-y-1.5">
        <p className="text-sm font-medium">Салон</p>
        <p className="text-sm text-muted-foreground">
          Нет активных салонов — обратитесь к администратору.
        </p>
      </div>
    )
  }

  if (salons.length === 1) {
    return (
      <div className="space-y-1.5">
        <p className="text-sm font-medium">Салон</p>
        <p className="flex h-11 items-center text-sm text-muted-foreground sm:h-9">
          {salons[0].name}
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-1.5">
      <Label htmlFor="barber-booking-salon">Салон</Label>
      <select
        id="barber-booking-salon"
        value={value}
        disabled={disabled}
        className={SELECT_CLASSES}
        onChange={(event) => onChange(event.target.value)}
      >
        {salons.map((salon) => (
          <option key={salon.salonId} value={salon.salonId}>
            {salon.name} · {salon.city}
          </option>
        ))}
      </select>
    </div>
  )
}

function OptionsSkeleton() {
  return (
    <div aria-busy="true" className="space-y-2">
      <p role="status" className="sr-only">
        Загружаем услуги и салоны…
      </p>
      <Skeleton className="h-9 w-full rounded-lg" />
      <Skeleton className="h-9 w-full rounded-lg" />
      <Skeleton className="h-24 w-full rounded-lg" />
    </div>
  )
}

function OptionsError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  return (
    <div
      role="alert"
      className="flex flex-col gap-3 rounded-lg border border-destructive/30 p-3 sm:flex-row sm:items-center sm:justify-between"
    >
      <p className="flex items-start gap-2 text-sm text-muted-foreground">
        <TriangleAlert
          className="mt-0.5 size-4 shrink-0 text-destructive"
          aria-hidden
        />
        <span>{message}</span>
      </p>
      <Button
        type="button"
        variant="outline"
        className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
        onClick={onRetry}
      >
        Повторить
      </Button>
    </div>
  )
}

function SlotsSkeleton() {
  return (
    <ul aria-hidden="true" className="flex flex-wrap gap-2">
      {['slot-skeleton-1', 'slot-skeleton-2', 'slot-skeleton-3'].map((key) => (
        <li key={key}>
          <Skeleton className="h-9 w-16 rounded-lg" />
        </li>
      ))}
    </ul>
  )
}
