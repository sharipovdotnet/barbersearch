import { Loader2 } from 'lucide-react'
import { useMemo, useState, type FormEvent } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { ApiError } from '@/lib/api'
import {
  createSalon,
  describeAdminCatalogueError,
  salonFieldErrors,
  updateSalonContactInfo,
  updateSalonLocation,
  type AdminSalon,
} from '@/lib/admin-catalogue'

/** IANA-зоны: свободный ввод часового пояса ломает расчёт слотов. */
function supportedTimeZones(): readonly string[] {
  try {
    return Intl.supportedValuesOf('timeZone')
  } catch {
    return []
  }
}

const TIME_ZONES = supportedTimeZones()

const DEFAULT_TIME_ZONE =
  TIME_ZONES.find(
    (zone) => zone === new Intl.DateTimeFormat().resolvedOptions().timeZone
  ) ?? 'Europe/Moscow'

const MAX_PHONE_LENGTH = 32
const MAX_URL_LENGTH = 2048
const MIN_LATITUDE = -90
const MAX_LATITUDE = 90
const MIN_LONGITUDE = -180
const MAX_LONGITUDE = 180

type SalonForm = {
  name: string
  address: string
  city: string
  country: string
  postalCode: string
  timeZoneId: string
  latitude: string
  longitude: string
  phoneNumber: string
  websiteUrl: string
}

type SalonErrors = Partial<Record<keyof SalonForm, string>>

/**
 * Создание и правка салона.
 *
 * Название задаётся только при создании: в домене нет переименования, и редактируемое поле
 * молча не сохранялось бы. Правка отправляет два независимых запроса — адрес и контакты:
 * если второй упадёт, первый уже применён, поэтому диалог не пишет «Сохранено», а просит
 * список перечитаться.
 */
export function SalonDialog({
  mode,
  salon,
  onClose,
  onSaved,
  onContactsFailed,
}: {
  mode: 'create' | 'edit'
  salon: AdminSalon | null
  onClose: () => void
  onSaved: (message: string) => void
  onContactsFailed: () => void
}) {
  const [form, setForm] = useState<SalonForm>(() => ({
    name: salon?.name ?? '',
    address: salon?.address ?? '',
    city: salon?.city ?? '',
    country: salon?.country ?? '',
    postalCode: salon?.postalCode ?? '',
    timeZoneId: salon?.timeZoneId ?? DEFAULT_TIME_ZONE,
    latitude: salon ? String(salon.latitude) : '',
    longitude: salon ? String(salon.longitude) : '',
    phoneNumber: salon?.phoneNumber ?? '',
    websiteUrl: salon?.websiteUrl ?? '',
  }))
  const [errors, setErrors] = useState<SalonErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  // Текущее значение всегда есть в списке, даже если система его не знает.
  const timeZones = useMemo(
    () =>
      TIME_ZONES.includes(form.timeZoneId)
        ? TIME_ZONES
        : [form.timeZoneId, ...TIME_ZONES],
    [form.timeZoneId]
  )

  function update<K extends keyof SalonForm>(key: K, value: SalonForm[K]) {
    setForm((current) => ({ ...current, [key]: value }))
    setErrors((current) => ({ ...current, [key]: undefined }))
    setFormError(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const nextErrors = validate(form)
    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      return
    }

    const location = {
      address: form.address.trim(),
      city: form.city.trim(),
      country: form.country.trim(),
      timeZoneId: form.timeZoneId,
      latitude: Number(form.latitude),
      longitude: Number(form.longitude),
      postalCode: optional(form.postalCode),
    }
    const contacts = {
      phoneNumber: optional(form.phoneNumber),
      websiteUrl: optional(form.websiteUrl),
    }

    setIsPending(true)

    try {
      if (mode === 'create') {
        await createSalon({ name: form.name.trim(), ...location, ...contacts })
        onSaved('Салон создан')
        return
      }

      if (!salon) {
        return
      }

      await updateSalonLocation(salon.id, location)

      try {
        await updateSalonContactInfo(salon.id, contacts)
      } catch (cause) {
        // Первый запрос уже применён: «Сохранено» было бы враньём.
        setErrors(salonFieldErrors(cause))
        setFormError(
          describeAdminCatalogueError(
            cause,
            'Адрес сохранён, контакты — нет. Обновите список и попробуйте ещё раз.'
          )
        )
        onContactsFailed()
        return
      }

      onSaved('Изменения сохранены')
    } catch (cause) {
      if (cause instanceof ApiError) {
        setErrors(salonFieldErrors(cause))
      }

      setFormError(
        describeAdminCatalogueError(
          cause,
          mode === 'create'
            ? 'Не удалось создать салон. Попробуйте ещё раз.'
            : 'Не удалось сохранить салон. Попробуйте ещё раз.'
        )
      )
    } finally {
      setIsPending(false)
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !isPending) {
          onClose()
        }
      }}
    >
      <DialogContent className="flex max-h-[calc(100dvh-2rem)] flex-col overflow-hidden sm:w-[48rem] sm:max-w-[calc(100%_-_2rem)]">
        <form
          className="flex min-h-0 flex-col gap-4"
          noValidate
          aria-busy={isPending}
          onSubmit={handleSubmit}
        >
          <DialogHeader className="shrink-0">
            <DialogTitle>
              {mode === 'create' ? 'Новый салон' : 'Правка салона'}
            </DialogTitle>
            <DialogDescription>
              Выключенный салон исчезает из поиска и с карточек, но связи барберов
              сохраняются.
            </DialogDescription>
          </DialogHeader>

          <FieldGroup className="grid min-h-0 gap-4 overflow-y-auto sm:grid-cols-6">
            <Field className="sm:col-span-6">
              <FieldLabel htmlFor="catalogue-salon-name">Название</FieldLabel>
              <Input
                id="catalogue-salon-name"
                name="name"
                value={form.name}
                readOnly={mode === 'edit'}
                disabled={isPending || mode === 'edit'}
                aria-describedby={
                  mode === 'edit' ? 'catalogue-salon-name-hint' : undefined
                }
                onChange={(event) => update('name', event.target.value)}
              />
              {mode === 'edit' && (
                <FieldDescription id="catalogue-salon-name-hint">
                  Название задаётся при создании.
                </FieldDescription>
              )}
            </Field>

            <Field
              className="sm:col-span-4"
              data-invalid={Boolean(errors.address)}
            >
              <FieldLabel htmlFor="catalogue-salon-address">Адрес</FieldLabel>
              <Input
                id="catalogue-salon-address"
                name="address"
                value={form.address}
                disabled={isPending}
                aria-invalid={Boolean(errors.address)}
                aria-describedby={
                  errors.address ? 'catalogue-salon-address-error' : undefined
                }
                onChange={(event) => update('address', event.target.value)}
              />
              <FieldError id="catalogue-salon-address-error">
                {errors.address}
              </FieldError>
            </Field>

            <Field className="sm:col-span-2" data-invalid={Boolean(errors.city)}>
              <FieldLabel htmlFor="catalogue-salon-city">Город</FieldLabel>
              <Input
                id="catalogue-salon-city"
                name="city"
                value={form.city}
                disabled={isPending}
                aria-invalid={Boolean(errors.city)}
                aria-describedby={
                  errors.city ? 'catalogue-salon-city-error' : undefined
                }
                onChange={(event) => update('city', event.target.value)}
              />
              <FieldError id="catalogue-salon-city-error">
                {errors.city}
              </FieldError>
            </Field>

            <Field
              className="sm:col-span-2"
              data-invalid={Boolean(errors.country)}
            >
              <FieldLabel htmlFor="catalogue-salon-country">Страна</FieldLabel>
              <Input
                id="catalogue-salon-country"
                name="country"
                value={form.country}
                disabled={isPending}
                aria-invalid={Boolean(errors.country)}
                aria-describedby={
                  errors.country ? 'catalogue-salon-country-error' : undefined
                }
                onChange={(event) => update('country', event.target.value)}
              />
              <FieldError id="catalogue-salon-country-error">
                {errors.country}
              </FieldError>
            </Field>

            <Field className="sm:col-span-2">
              <FieldLabel htmlFor="catalogue-salon-postal-code">
                Почтовый индекс
              </FieldLabel>
              <Input
                id="catalogue-salon-postal-code"
                name="postalCode"
                value={form.postalCode}
                disabled={isPending}
                onChange={(event) => update('postalCode', event.target.value)}
              />
            </Field>

            <Field
              className="sm:col-span-2"
              data-invalid={Boolean(errors.timeZoneId)}
            >
              <FieldLabel htmlFor="catalogue-salon-time-zone">
                Часовой пояс
              </FieldLabel>
              <select
                id="catalogue-salon-time-zone"
                name="timeZoneId"
                value={form.timeZoneId}
                disabled={isPending}
                aria-invalid={Boolean(errors.timeZoneId)}
                aria-describedby={
                  errors.timeZoneId
                    ? 'catalogue-salon-time-zone-error'
                    : undefined
                }
                onChange={(event) => update('timeZoneId', event.target.value)}
                className="h-11 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-8"
              >
                {timeZones.map((zone) => (
                  <option key={zone} value={zone}>
                    {zone}
                  </option>
                ))}
              </select>
              <FieldError id="catalogue-salon-time-zone-error">
                {errors.timeZoneId}
              </FieldError>
            </Field>

            <Field
              className="sm:col-span-3"
              data-invalid={Boolean(errors.latitude)}
            >
              <FieldLabel htmlFor="catalogue-salon-latitude">
                Широта
              </FieldLabel>
              <Input
                id="catalogue-salon-latitude"
                name="latitude"
                inputMode="decimal"
                value={form.latitude}
                disabled={isPending}
                aria-invalid={Boolean(errors.latitude)}
                aria-describedby={
                  errors.latitude ? 'catalogue-salon-latitude-error' : undefined
                }
                onChange={(event) => update('latitude', event.target.value)}
              />
              <FieldError id="catalogue-salon-latitude-error">
                {errors.latitude}
              </FieldError>
            </Field>

            <Field
              className="sm:col-span-3"
              data-invalid={Boolean(errors.longitude)}
            >
              <FieldLabel htmlFor="catalogue-salon-longitude">
                Долгота
              </FieldLabel>
              <Input
                id="catalogue-salon-longitude"
                name="longitude"
                inputMode="decimal"
                value={form.longitude}
                disabled={isPending}
                aria-invalid={Boolean(errors.longitude)}
                aria-describedby={
                  errors.longitude ? 'catalogue-salon-longitude-error' : undefined
                }
                onChange={(event) => update('longitude', event.target.value)}
              />
              <FieldError id="catalogue-salon-longitude-error">
                {errors.longitude}
              </FieldError>
            </Field>

            <Field
              className="sm:col-span-3"
              data-invalid={Boolean(errors.phoneNumber)}
            >
              <FieldLabel htmlFor="catalogue-salon-phone">Телефон</FieldLabel>
              <Input
                id="catalogue-salon-phone"
                name="phoneNumber"
                inputMode="tel"
                value={form.phoneNumber}
                disabled={isPending}
                aria-invalid={Boolean(errors.phoneNumber)}
                aria-describedby={
                  errors.phoneNumber
                    ? 'catalogue-salon-phone-error'
                    : undefined
                }
                onChange={(event) => update('phoneNumber', event.target.value)}
              />
              <FieldError id="catalogue-salon-phone-error">
                {errors.phoneNumber}
              </FieldError>
            </Field>

            <Field
              className="sm:col-span-3"
              data-invalid={Boolean(errors.websiteUrl)}
            >
              <FieldLabel htmlFor="catalogue-salon-website">Сайт</FieldLabel>
              <Input
                id="catalogue-salon-website"
                name="websiteUrl"
                inputMode="url"
                placeholder="https://example.com"
                spellCheck={false}
                value={form.websiteUrl}
                disabled={isPending}
                aria-invalid={Boolean(errors.websiteUrl)}
                aria-describedby={
                  errors.websiteUrl
                    ? 'catalogue-salon-website-error'
                    : undefined
                }
                onChange={(event) => update('websiteUrl', event.target.value)}
              />
              <FieldError id="catalogue-salon-website-error">
                {errors.websiteUrl}
              </FieldError>
            </Field>
          </FieldGroup>

          {formError && (
            <p role="alert" className="shrink-0 text-sm text-destructive">
              {formError}
            </p>
          )}

          <DialogFooter className="shrink-0">
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
              disabled={isPending}
              aria-busy={isPending}
            >
              {isPending && (
                <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
              )}
              {mode === 'create' ? 'Создать' : 'Сохранить'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/** Пустая строка не отправляется: сервер понимает `null` как «поля нет». */
function optional(value: string): string | null {
  const trimmed = value.trim()

  return trimmed === '' ? null : trimmed
}

function validate(form: SalonForm): SalonErrors {
  const errors: SalonErrors = {}

  if (form.address.trim() === '') {
    errors.address = 'Введите адрес.'
  }

  if (form.city.trim() === '') {
    errors.city = 'Введите город.'
  }

  if (form.country.trim() === '') {
    errors.country = 'Введите страну.'
  }

  if (form.timeZoneId.trim() === '') {
    errors.timeZoneId = 'Выберите часовой пояс из списка.'
  }

  const latitude = Number(form.latitude)
  if (!Number.isFinite(latitude) || latitude < MIN_LATITUDE || latitude > MAX_LATITUDE) {
    errors.latitude = `От ${MIN_LATITUDE} до ${MAX_LATITUDE}.`
  }

  const longitude = Number(form.longitude)
  if (
    !Number.isFinite(longitude)
    || longitude < MIN_LONGITUDE
    || longitude > MAX_LONGITUDE
  ) {
    errors.longitude = `От ${MIN_LONGITUDE} до ${MAX_LONGITUDE}.`
  }

  const website = form.websiteUrl.trim()
  if (website !== '' && !isHttpUrl(website)) {
    errors.websiteUrl = 'Адрес сайта начинается с http:// или https://.'
  }

  if (form.phoneNumber.trim().length > MAX_PHONE_LENGTH) {
    errors.phoneNumber = `Не длиннее ${MAX_PHONE_LENGTH} символов.`
  }

  return errors
}

function isHttpUrl(value: string): boolean {
  if (value.length > MAX_URL_LENGTH) {
    return false
  }

  try {
    const url = new URL(value)

    return url.protocol === 'http:' || url.protocol === 'https:'
  } catch {
    return false
  }
}
