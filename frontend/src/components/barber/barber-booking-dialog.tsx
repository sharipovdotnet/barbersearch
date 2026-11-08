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
import {
  createBarberBooking,
  formatGuestName,
  MAX_GUEST_FIRST_NAME_LENGTH,
  MAX_GUEST_LAST_NAME_LENGTH,
  MAX_GUEST_PHONE_LENGTH,
  type CreateBarberBookingInput,
} from '@/lib/barber-booking-create'
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
import { useI18n } from '@/i18n'

/** UI-лимит комментария совпадает с публичной записью. */
const MAX_NOTE_LENGTH = 500

/** Горизонт выбора даты: как на публичной карточке, 30 дней. */
const DATE_HORIZON_DAYS = 30

const SELECT_CLASSES =
  'h-11 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 text-base transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-9 md:text-sm'

const CLIENT_SKELETON_KEYS = ['client-skeleton-1', 'client-skeleton-2']

/**
 * Кто записывается: найденный в сервисе клиент или новый человек без аккаунта.
 * Режим меняет только блок контактов; услуга, салон, дата и время общие.
 */
type ClientMode = 'registered' | 'guest'

type BarberBookingDialogProps = {
  /** Профиль из сессии: чужой профиль сервер не примет. */
  barberProfileId: string
  /** Локальная дата салона `YYYY-MM-DD`; без неё — сегодня, как раньше. */
  initialDate?: string
  /** Желаемое начало (ISO): ближайший слот к нему выбирается заранее. */
  preferredStartAt?: string
  /** Вызывается после `201`: список записей и объявление обновляет экран. */
  onCreated: (clientName: string) => void
  /** Внешнее управление: окно дня и пустое состояние открывают диалог сами. */
  open?: boolean
  onOpenChange?: (open: boolean) => void
}

/**
 * Диалог «Записать клиента» в кабинете барбера.
 *
 * Барбер выбирает услугу, салон, дату и свободное время, затем указывает клиента:
 * находит зарегистрированного по email или телефону либо заполняет имя и телефон
 * нового человека. Второй вариант не создаёт аккаунт — сервер сохраняет контакты
 * в самой брони. Отправляется один `POST api/barber/bookings`; сервер создаёт бронь
 * сразу подтверждённой. Зарегистрированный клиент увидит её в своём кабинете.
 * Пока запрос в полёте, диалог нельзя закрыть: иначе повтор после закрытия создал бы
 * вторую попытку с новым ключом идемпотентности.
 *
 * `initialDate` и `preferredStartAt` приходят из ленты дня: окно между визитами
 * открывает диалог с уже нужной датой и ближайшим к началу окна слотом. Предвыбор
 * ничего не отправляет — барбер выбирает любую другую радиокнопку.
 */
export function BarberBookingDialog({
  barberProfileId,
  initialDate,
  preferredStartAt,
  onCreated,
  open,
  onOpenChange,
}: BarberBookingDialogProps) {
  const { t } = useI18n()
  const isControlled = open !== undefined
  const [internalOpen, setInternalOpen] = useState(false)
  const [isPending, setIsPending] = useState(false)
  const isOpen = isControlled ? open : internalOpen

  const changeOpen = (next: boolean) => {
    if (isControlled) {
      onOpenChange?.(next)
    } else {
      setInternalOpen(next)
    }
  }

  return (
    <Dialog
      open={isOpen}
      onOpenChange={(next) => {
        if (!next && isPending) {
          return
        }

        changeOpen(next)
      }}
    >
      <DialogTrigger
        render={<Button className="h-11 w-full sm:h-8 sm:w-auto" />}
      >
        <Plus data-icon="inline-start" aria-hidden />
        {t('Записать клиента')}
      </DialogTrigger>

      <DialogContent
        className="max-h-[calc(100svh-2rem)] overflow-y-auto sm:max-w-lg"
        showCloseButton={!isPending}
      >
        {isOpen && (
          <BarberBookingForm
            barberProfileId={barberProfileId}
            initialDate={initialDate}
            preferredStartAt={preferredStartAt}
            isPending={isPending}
            onPendingChange={setIsPending}
            onClose={() => {
              changeOpen(false)
            }}
            onCreated={(clientName) => {
              changeOpen(false)
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
  initialDate?: string
  preferredStartAt?: string
  isPending: boolean
  onPendingChange: (isPending: boolean) => void
  onClose: () => void
  onCreated: (clientName: string) => void
}

function BarberBookingForm({
  barberProfileId,
  initialDate,
  preferredStartAt,
  isPending,
  onPendingChange,
  onClose,
  onCreated,
}: BarberBookingFormProps) {
  const { t } = useI18n()
  const options = useBarberBookingOptions()
  const [clientMode, setClientMode] = useState<ClientMode>('registered')
  const [search, setSearch] = useState('')
  // В режиме нового клиента поиск по базе не нужен: старый запрос не должен
  // продолжать ходить за контактами, пока барбер заполняет гостевые поля.
  const clients = useBarberClients(clientMode === 'registered' ? search : '')
  const [client, setClient] = useState<BarberClient | null>(null)
  const [guestFirstName, setGuestFirstName] = useState('')
  const [guestLastName, setGuestLastName] = useState('')
  const [guestPhone, setGuestPhone] = useState('')
  const [serviceId, setServiceId] = useState('')
  const [salonId, setSalonId] = useState('')
  // Желаемая дата приходит из ленты дня. Если она не `YYYY-MM-DD`, работает
  // прежнее поведение: пустое поле означает «сегодня в поясе салона».
  const [date, setDate] = useState(() =>
    initialDate !== undefined && isSalonDate(initialDate) ? initialDate : ''
  )
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

  /**
   * Предвыбор слота из ленты дня — производное значение, а не эффект.
   *
   * Пока барбер не нажал радиокнопку, выбран «ближайший к желаемому» слот; ручной
   * выбор всегда сильнее. Смена услуги, салона или даты сбрасывает выбор, и предвыбор
   * пересчитывается на новой сетке.
   */
  const preferredSlot =
    preferredStartAt === undefined
      ? null
      : pickPreferredSlot(slots.slots?.slots ?? [], preferredStartAt)
  const effectiveSlotStartAt = slotStartAt ?? preferredSlot?.startAt ?? null

  const selectedSlot =
    slots.slots?.slots.find((item) => item.startAt === effectiveSlotStartAt) ??
    null

  const guestFirstNameValue = guestFirstName.trim()
  const guestLastNameValue = guestLastName.trim()
  const guestPhoneValue = guestPhone.trim()
  const hasGuestContacts =
    guestFirstNameValue !== '' && guestPhoneValue !== ''
  const hasClient =
    clientMode === 'registered' ? client !== null : hasGuestContacts

  const canSubmit =
    hasClient &&
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

  function handleModeChange(next: ClientMode) {
    if (next === clientMode) {
      return
    }

    // Режим меняет состав тела запроса, поэтому старый ключ идемпотентности
    // вернул бы уже созданную бронь с прежними контактами.
    setClientMode(next)
    setClient(null)
    setFailure(null)
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

  function handleGuestFirstNameChange(value: string) {
    setGuestFirstName(value)
    setFailure(null)
  }

  function handleGuestLastNameChange(value: string) {
    setGuestLastName(value)
    setFailure(null)
  }

  function handleGuestPhoneChange(value: string) {
    setGuestPhone(value)
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
      service === null ||
      salon === null ||
      selectedSlot === null
    ) {
      return
    }

    const common = {
      barberServiceId: service.barberServiceId,
      salonId: salon.salonId,
      startAt: selectedSlot.startAt,
      endAt: selectedSlot.endAt,
      idempotencyKey,
      customerNote: note.trim() === '' ? null : note.trim(),
    }

    let input: CreateBarberBookingInput
    let createdName: string

    if (clientMode === 'registered') {
      if (client === null) {
        return
      }

      input = { ...common, customerId: client.id }
      createdName = formatBarberClientName(client)
    } else {
      if (!hasGuestContacts) {
        return
      }

      const guestLastNameOrNull =
        guestLastNameValue === '' ? null : guestLastNameValue

      input = {
        ...common,
        customerId: null,
        guestFirstName: guestFirstNameValue,
        guestLastName: guestLastNameOrNull,
        guestPhone: guestPhoneValue,
      }
      createdName = formatGuestName(guestFirstNameValue, guestLastNameOrNull)
    }

    submittingRef.current = true
    onPendingChange(true)
    setFailure(null)

    try {
      await createBarberBooking(input)

      onPendingChange(false)
      onCreated(createdName)
    } catch (cause) {
      const next = describeBarberBookingCreateFailure(
        cause,
        t('Не удалось записать клиента.')
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
        <DialogTitle>{t('Записать клиента')}</DialogTitle>
        <DialogDescription>
          {t(
            'Запись создаётся сразу подтверждённой. Найти можно любого активного пользователя — клиента, администратора или барбера; для нового человека достаточно имени и телефона — аккаунт не создаётся.'
          )}
        </DialogDescription>
      </DialogHeader>

      {options.status === 'loading' && <OptionsSkeleton />}

      {options.status === 'error' && (
        <OptionsError
          message={options.error ?? t('Не удалось загрузить услуги и салоны.')}
          onRetry={options.retry}
        />
      )}

      {options.status === 'ready' && (
        <>
          <ClientModeSwitch
            mode={clientMode}
            disabled={isPending}
            onChange={handleModeChange}
          />

          {clientMode === 'registered' ? (
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
          ) : (
            <GuestFields
              firstName={guestFirstName}
              lastName={guestLastName}
              phone={guestPhone}
              disabled={isPending}
              onFirstNameChange={handleGuestFirstNameChange}
              onLastNameChange={handleGuestLastNameChange}
              onPhoneChange={handleGuestPhoneChange}
            />
          )}

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
            <Label htmlFor="barber-booking-date">{t('Дата')}</Label>
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
                {t('Время — местное для салона «{name}».', {
                  name: salon.name,
                })}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <p className="text-sm font-medium">{t('Свободное время')}</p>

            {service === null && (
              <p className="text-sm text-muted-foreground">
                {t('Сначала выберите услугу — от её длительности строится сетка.')}
              </p>
            )}

            {service !== null && !isDateValid && (
              <p className="text-sm text-muted-foreground">
                {t('Выберите дату.')}
              </p>
            )}

            {service !== null && isDateValid && (
              <>
                {slots.status === 'loading' && (
                  <div aria-busy="true" className="space-y-2">
                    <p role="status" className="sr-only">
                      {t('Загружаем свободное время…')}
                    </p>
                    <SlotsSkeleton />
                  </div>
                )}

                {slots.status === 'ready' && slots.slots !== null && (
                  <BarberSlotOptions
                    slots={slots.slots}
                    selectable
                    selectedStartAt={effectiveSlotStartAt}
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
                        {slots.error ?? t('Не удалось загрузить свободное время.')}
                      </span>
                    </p>
                    <Button
                      type="button"
                      variant="outline"
                      className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
                      onClick={slots.retry}
                    >
                      {t('Повторить')}
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
              {t('Комментарий')}{' '}
              <span className="font-normal text-muted-foreground">
                {t('— необязательно')}
              </span>
            </Label>
            <Textarea
              id="barber-booking-note"
              name="customerNote"
              value={note}
              maxLength={MAX_NOTE_LENGTH}
              disabled={isPending}
              placeholder={t('Пожелания клиента или заметка для визита')}
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
          {t('Отмена')}
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
          {isPending ? t('Записываем…') : t('Записать')}
        </Button>
      </DialogFooter>
    </form>
  )
}

/**
 * Переключатель «зарегистрированный / новый».
 *
 * Радиокнопки вместо вкладок: это выбор одного значения, а не навигация по панелям,
 * и он остаётся доступным с клавиатуры стрелками.
 */
function ClientModeSwitch({
  mode,
  disabled,
  onChange,
}: {
  mode: ClientMode
  disabled: boolean
  onChange: (mode: ClientMode) => void
}) {
  const { t } = useI18n()

  return (
    <fieldset className="space-y-2" disabled={disabled}>
      <legend className="text-sm font-medium">{t('Кто клиент')}</legend>
      <div className="grid grid-cols-2 gap-2">
        <ClientModeOption
          value="registered"
          label={t('Зарегистрированный')}
          checked={mode === 'registered'}
          onChange={onChange}
        />
        <ClientModeOption
          value="guest"
          label={t('Новый клиент')}
          checked={mode === 'guest'}
          onChange={onChange}
        />
      </div>
      <p className="text-xs text-muted-foreground">
        {t('«Новый клиент» — человек без аккаунта в сервисе. Аккаунт не создаётся.')}
      </p>
    </fieldset>
  )
}

function ClientModeOption({
  value,
  label,
  checked,
  onChange,
}: {
  value: ClientMode
  label: string
  checked: boolean
  onChange: (mode: ClientMode) => void
}) {
  return (
    <label className="cursor-pointer">
      <input
        type="radio"
        name="barber-client-mode"
        value={value}
        checked={checked}
        className="peer sr-only"
        onChange={() => onChange(value)}
      />
      <span className="flex min-h-11 items-center justify-center rounded-lg border px-3 py-2 text-center text-sm transition-colors peer-checked:border-primary peer-checked:bg-primary/5 peer-focus-visible:ring-3 peer-focus-visible:ring-ring/50 peer-disabled:cursor-not-allowed peer-disabled:opacity-50 sm:min-h-9">
        {label}
      </span>
    </label>
  )
}

/**
 * Контакты нового клиента.
 *
 * Имя и телефон обязательны: по ним барбер узнаёт человека и может позвонить.
 * Фамилия необязательна, email не спрашиваем — аккаунта и рассылки нет.
 */
function GuestFields({
  firstName,
  lastName,
  phone,
  disabled,
  onFirstNameChange,
  onLastNameChange,
  onPhoneChange,
}: {
  firstName: string
  lastName: string
  phone: string
  disabled: boolean
  onFirstNameChange: (value: string) => void
  onLastNameChange: (value: string) => void
  onPhoneChange: (value: string) => void
}) {
  const { t } = useI18n()

  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label htmlFor="barber-guest-first-name">
            {t('Имя')}{' '}
            <span className="font-normal text-muted-foreground">
              {t('— обязательно')}
            </span>
          </Label>
          <Input
            id="barber-guest-first-name"
            type="text"
            value={firstName}
            autoComplete="given-name"
            maxLength={MAX_GUEST_FIRST_NAME_LENGTH}
            disabled={disabled}
            required
            aria-required="true"
            onChange={(event) => onFirstNameChange(event.target.value)}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="barber-guest-last-name">
            {t('Фамилия')}{' '}
            <span className="font-normal text-muted-foreground">
              {t('— необязательно')}
            </span>
          </Label>
          <Input
            id="barber-guest-last-name"
            type="text"
            value={lastName}
            autoComplete="family-name"
            maxLength={MAX_GUEST_LAST_NAME_LENGTH}
            disabled={disabled}
            onChange={(event) => onLastNameChange(event.target.value)}
          />
        </div>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="barber-guest-phone">
          {t('Телефон')}{' '}
          <span className="font-normal text-muted-foreground">
            {t('— обязательно')}
          </span>
        </Label>
        <Input
          id="barber-guest-phone"
          type="tel"
          value={phone}
          autoComplete="tel"
          inputMode="tel"
          maxLength={MAX_GUEST_PHONE_LENGTH}
          placeholder="+7 900 000-00-00"
          disabled={disabled}
          required
          aria-required="true"
          onChange={(event) => onPhoneChange(event.target.value)}
        />
      </div>

      <p className="text-xs text-muted-foreground">
        {t('Контакты сохранятся в записи, чтобы вы могли связаться с клиентом.')}
      </p>
    </div>
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
  const { t } = useI18n()
  const hintId = 'barber-client-search-hint'
  const isTooShort = search.trim().length < MIN_BARBER_CLIENT_SEARCH

  return (
    <div className="space-y-2">
      <Label htmlFor="barber-client-search">{t('Клиент')}</Label>
      <Input
        id="barber-client-search"
        type="search"
        value={search}
        autoComplete="off"
        maxLength={MAX_BARBER_CLIENT_SEARCH}
        placeholder={t('Email или телефон')}
        aria-describedby={hintId}
        onChange={(event) => onSearchChange(event.target.value)}
      />
      <p id={hintId} className="text-xs text-muted-foreground">
        {t('Минимум {count} символа. Нет клиента в сервисе? Выберите «Новый клиент».', {
          count: MIN_BARBER_CLIENT_SEARCH,
        })}
      </p>

      {selected !== null && (
        <p role="status" className="text-sm">
          {t('Выбран:')}{' '}
          <span className="font-medium">{formatBarberClientName(selected)}</span>
        </p>
      )}

      {isTooShort && (
        <p className="text-sm text-muted-foreground">
          {t('Введите начало email или номер телефона.')}
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
            {error ?? t('Не удалось найти клиентов.')}
          </p>
          <Button
            type="button"
            variant="outline"
            className="h-11 w-full shrink-0 sm:h-9 sm:w-auto"
            onClick={onRetry}
          >
            {t('Повторить')}
          </Button>
        </div>
      )}

      {!isTooShort && status === 'ready' && clients.length === 0 && (
        <p className="text-sm text-muted-foreground">
          {t('Клиенты не найдены. Уточните email или телефон.')}
        </p>
      )}

      {!isTooShort && status === 'ready' && clients.length > 0 && (
        <ul
          aria-label={t('Найденные клиенты')}
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
  const { t } = useI18n()

  if (services.length === 0) {
    return (
      <div className="space-y-1.5">
        <p className="text-sm font-medium">{t('Услуга')}</p>
        <p className="text-sm text-muted-foreground">
          {t('У вас нет активных услуг — добавьте их в профиле.')}
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-1.5">
      <Label htmlFor="barber-booking-service">{t('Услуга')}</Label>
      <select
        id="barber-booking-service"
        value={value}
        disabled={disabled}
        className={SELECT_CLASSES}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">{t('Выберите услугу')}</option>
        {services.map((service) => (
          <option key={service.barberServiceId} value={service.barberServiceId}>
            {service.name} · {service.durationMinutes} {t('мин')} ·{' '}
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
  const { t } = useI18n()

  if (salons.length === 0) {
    return (
      <div className="space-y-1.5">
        <p className="text-sm font-medium">{t('Салон')}</p>
        <p className="text-sm text-muted-foreground">
          {t('Нет активных салонов — обратитесь к администратору.')}
        </p>
      </div>
    )
  }

  if (salons.length === 1) {
    return (
      <div className="space-y-1.5">
        <p className="text-sm font-medium">{t('Салон')}</p>
        <p className="flex h-11 items-center text-sm text-muted-foreground sm:h-9">
          {salons[0].name}
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-1.5">
      <Label htmlFor="barber-booking-salon">{t('Салон')}</Label>
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

/**
 * Слот, ближайший к желаемому началу.
 *
 * 1. Кандидаты — слоты не раньше `preferredStartAt`, побеждает минимальная разница
 *    `startAt - preferredStartAt`.
 * 2. Если таких нет — ближайший вообще по модулю разницы.
 * 3. Желаемое время в прошлом попадает в первый пункт: сервер уже вырезал
 *    прошедшие слоты, поэтому «кандидат» — первый будущий.
 *
 * Предвыбор ничего не отправляет и не блокирует ручной выбор радиокнопки.
 */
function pickPreferredSlot(
  slots: readonly BarberSlot[],
  preferredStartAt: string
): BarberSlot | null {
  const first = slots[0]

  if (first === undefined) {
    return null
  }

  const preferred = Date.parse(preferredStartAt)

  if (Number.isNaN(preferred)) {
    return first
  }

  const later = slots.filter((slot) => Date.parse(slot.startAt) >= preferred)
  const pool = later.length > 0 ? later : slots

  let best = pool[0]
  let distance = Math.abs(Date.parse(best.startAt) - preferred)

  for (const slot of pool.slice(1)) {
    const candidate = Math.abs(Date.parse(slot.startAt) - preferred)

    if (candidate < distance) {
      best = slot
      distance = candidate
    }
  }

  return best
}

function OptionsSkeleton() {
  const { t } = useI18n()

  return (
    <div aria-busy="true" className="space-y-2">
      <p role="status" className="sr-only">
        {t('Загружаем услуги и салоны…')}
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
  const { t } = useI18n()

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
        {t('Повторить')}
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
