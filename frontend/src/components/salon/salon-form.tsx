import { Loader2 } from 'lucide-react'
import {
  memo,
  useCallback,
  useMemo,
  useState,
  type FormEvent,
  type ReactNode,
} from 'react'

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
import {
  createSalon,
  updateSalonContactInfo,
  updateSalonLocation,
} from '@/lib/admin-catalogue'
import { createOwnSalon } from '@/lib/barber-salons'
import {
  describeSalonSaveError,
  salonContactInput,
  salonCreateInput,
  salonFieldErrors,
  salonFormValues,
  salonLocationInput,
  salonTimeZones,
  validateSalonForm,
  type SalonFormErrors,
  type SalonFormMode,
  type SalonFormSalon,
  type SalonFormValues,
  type SalonScope,
} from '@/lib/salon-form'

/**
 * Общая форма салона для админского каталога и кабинета барбера.
 *
 * Поля, валидация и тела запросов одни и те же, отличаются только путь создания
 * (`POST /api/salons` у админа, `POST /api/salons/own` у барбера), заголовки и
 * подсказки. Правка отправляет два независимых запроса — адрес и контакты: если
 * второй упадёт, первый уже применён, поэтому диалог не пишет «Сохранено», а просит
 * список перечитаться. Название задаётся только при создании: в домене нет
 * переименования, и редактируемое поле молча не сохранялось бы.
 *
 * Список «Мои салоны» отдаёт полный набор полей салона, поэтому правка владельца
 * предзаполняет адрес, координаты и контакты, а не просит ввести их заново.
 */
export function SalonDialogForm({
  scope,
  mode,
  salon,
  salonId,
  initialFocus,
  onClose,
  onSaved,
  onContactsFailed,
}: {
  scope: SalonScope
  mode: SalonFormMode
  salon: SalonFormSalon | null
  salonId: string | null
  initialFocus?: 'location' | 'contacts'
  onClose: () => void
  onSaved: (message: string) => void
  onContactsFailed?: () => void
}) {
  const [form, setForm] = useState<SalonFormValues>(() => salonFormValues(salon))
  const [errors, setErrors] = useState<SalonFormErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  const isOwner = scope === 'owner'
  const isEdit = mode === 'edit'
  // У барбера почтовый индекс скрыт только при создании: правка сохраняет
  // существующее значение и даёт его изменить.
  const showPostalCode = !isOwner || isEdit

  const selectTimeZone = useCallback((value: string) => {
    setForm((current) => ({ ...current, timeZoneId: value }))
    setErrors((current) => ({ ...current, timeZoneId: undefined }))
    setFormError(null)
  }, [])

  function update<K extends keyof SalonFormValues>(
    key: K,
    value: SalonFormValues[K]
  ) {
    setForm((current) => ({ ...current, [key]: value }))
    setErrors((current) => ({ ...current, [key]: undefined }))
    setFormError(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const nextErrors = validateSalonForm(form)
    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      return
    }

    const location = salonLocationInput(form)
    const contacts = salonContactInput(form)

    setIsPending(true)

    try {
      if (mode === 'create') {
        if (scope === 'owner') {
          await createOwnSalon(salonCreateInput(form))
          onSaved('Салон отправлен на проверку.')
          return
        }

        await createSalon(salonCreateInput(form))
        onSaved('Салон создан')
        return
      }

      if (salonId === null) {
        return
      }

      await updateSalonLocation(salonId, location)

      try {
        await updateSalonContactInfo(salonId, contacts)
      } catch (cause) {
        // Первый запрос уже применён: «Сохранено» было бы враньём.
        setErrors(salonFieldErrors(cause))
        setFormError(
          'Адрес сохранён, контакты — нет. Обновите список и попробуйте ещё раз.'
        )
        onContactsFailed?.()
        return
      }

      onSaved('Изменения сохранены')
    } catch (cause) {
      setErrors(salonFieldErrors(cause))
      setFormError(describeSalonSaveError(cause, scope, mode))
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
            <DialogTitle>{titleFor(scope, mode)}</DialogTitle>
            <DialogDescription>{descriptionFor(scope, mode)}</DialogDescription>
          </DialogHeader>

          <FieldGroup className="grid min-h-0 gap-4 overflow-y-auto sm:grid-cols-6">
            <Field className="sm:col-span-6">
              <FieldLabel htmlFor="salon-name">Название</FieldLabel>
              <Input
                id="salon-name"
                name="name"
                value={form.name}
                readOnly={isEdit}
                disabled={isPending || isEdit}
                aria-invalid={Boolean(errors.name)}
                aria-describedby={
                  errors.name
                    ? 'salon-name-error'
                    : isEdit
                      ? 'salon-name-hint'
                      : isOwner
                        ? 'salon-name-warning'
                        : undefined
                }
                onChange={(event) => update('name', event.target.value)}
              />
              {isEdit ? (
                <FieldDescription id="salon-name-hint">
                  Название задаётся при создании.
                </FieldDescription>
              ) : isOwner ? (
                <FieldDescription id="salon-name-warning">
                  Название нельзя изменить после создания салона.
                </FieldDescription>
              ) : null}
              <FieldError id="salon-name-error">{errors.name}</FieldError>
            </Field>

            <Field
              className="sm:col-span-4"
              data-invalid={Boolean(errors.address)}
            >
              <FieldLabel htmlFor="salon-address">Адрес</FieldLabel>
              <Input
                id="salon-address"
                name="address"
                value={form.address}
                autoFocus={initialFocus === 'location'}
                disabled={isPending}
                aria-invalid={Boolean(errors.address)}
                aria-describedby={
                  errors.address ? 'salon-address-error' : undefined
                }
                onChange={(event) => update('address', event.target.value)}
              />
              <FieldError id="salon-address-error">{errors.address}</FieldError>
            </Field>

            <Field className="sm:col-span-2" data-invalid={Boolean(errors.city)}>
              <FieldLabel htmlFor="salon-city">Город</FieldLabel>
              <Input
                id="salon-city"
                name="city"
                value={form.city}
                disabled={isPending}
                aria-invalid={Boolean(errors.city)}
                aria-describedby={errors.city ? 'salon-city-error' : undefined}
                onChange={(event) => update('city', event.target.value)}
              />
              <FieldError id="salon-city-error">{errors.city}</FieldError>
            </Field>

            <Field
              className="sm:col-span-2"
              data-invalid={Boolean(errors.country)}
            >
              <FieldLabel htmlFor="salon-country">Страна</FieldLabel>
              <Input
                id="salon-country"
                name="country"
                value={form.country}
                disabled={isPending}
                aria-invalid={Boolean(errors.country)}
                aria-describedby={
                  errors.country ? 'salon-country-error' : undefined
                }
                onChange={(event) => update('country', event.target.value)}
              />
              <FieldError id="salon-country-error">{errors.country}</FieldError>
            </Field>

            {showPostalCode && (
              <Field className="sm:col-span-2">
                <FieldLabel htmlFor="salon-postal-code">
                  Почтовый индекс
                </FieldLabel>
                <Input
                  id="salon-postal-code"
                  name="postalCode"
                  value={form.postalCode}
                  disabled={isPending}
                  onChange={(event) => update('postalCode', event.target.value)}
                />
              </Field>
            )}

            <Field
              className="sm:col-span-2"
              data-invalid={Boolean(errors.timeZoneId)}
            >
              <FieldLabel htmlFor="salon-time-zone">Часовой пояс</FieldLabel>
              <TimeZoneSelect
                value={form.timeZoneId}
                disabled={isPending}
                invalid={Boolean(errors.timeZoneId)}
                describedBy={
                  errors.timeZoneId ? 'salon-time-zone-error' : undefined
                }
                onChange={selectTimeZone}
              />
              <FieldError id="salon-time-zone-error">
                {errors.timeZoneId}
              </FieldError>
            </Field>

            <Field
              className="sm:col-span-3"
              data-invalid={Boolean(errors.latitude)}
            >
              <FieldLabel htmlFor="salon-latitude">Широта</FieldLabel>
              <Input
                id="salon-latitude"
                name="latitude"
                inputMode="decimal"
                value={form.latitude}
                disabled={isPending}
                aria-invalid={Boolean(errors.latitude)}
                aria-describedby={
                  errors.latitude
                    ? 'salon-latitude-error salon-coordinates-hint'
                    : 'salon-coordinates-hint'
                }
                onChange={(event) => update('latitude', event.target.value)}
              />
              <FieldError id="salon-latitude-error">
                {errors.latitude}
              </FieldError>
            </Field>

            <Field
              className="sm:col-span-3"
              data-invalid={Boolean(errors.longitude)}
            >
              <FieldLabel htmlFor="salon-longitude">Долгота</FieldLabel>
              <Input
                id="salon-longitude"
                name="longitude"
                inputMode="decimal"
                value={form.longitude}
                disabled={isPending}
                aria-invalid={Boolean(errors.longitude)}
                aria-describedby={
                  errors.longitude
                    ? 'salon-longitude-error salon-coordinates-hint'
                    : 'salon-coordinates-hint'
                }
                onChange={(event) => update('longitude', event.target.value)}
              />
              <FieldError id="salon-longitude-error">
                {errors.longitude}
              </FieldError>
            </Field>

            {isOwner && (
              <FieldDescription
                id="salon-coordinates-hint"
                className="sm:col-span-6"
              >
                Скопируйте координаты из карт — встроенной карты и геокодера нет.
              </FieldDescription>
            )}

            <Field
              className="sm:col-span-3"
              data-invalid={Boolean(errors.phoneNumber)}
            >
              <FieldLabel htmlFor="salon-phone">Телефон</FieldLabel>
              <Input
                id="salon-phone"
                name="phoneNumber"
                inputMode="tel"
                value={form.phoneNumber}
                autoFocus={initialFocus === 'contacts'}
                disabled={isPending}
                aria-invalid={Boolean(errors.phoneNumber)}
                aria-describedby={
                  errors.phoneNumber ? 'salon-phone-error' : undefined
                }
                onChange={(event) => update('phoneNumber', event.target.value)}
              />
              <FieldError id="salon-phone-error">
                {errors.phoneNumber}
              </FieldError>
            </Field>

            <Field
              className="sm:col-span-3"
              data-invalid={Boolean(errors.websiteUrl)}
            >
              <FieldLabel htmlFor="salon-website">Сайт</FieldLabel>
              <Input
                id="salon-website"
                name="websiteUrl"
                inputMode="url"
                placeholder="https://example.com"
                spellCheck={false}
                value={form.websiteUrl}
                disabled={isPending}
                aria-invalid={Boolean(errors.websiteUrl)}
                aria-describedby={
                  errors.websiteUrl ? 'salon-website-error' : undefined
                }
                onChange={(event) => update('websiteUrl', event.target.value)}
              />
              <FieldError id="salon-website-error">
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
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {submitLabelFor(scope, mode)}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * Список часовых поясов — сотни `<option>`.
 *
 * Мемоизирован отдельно: ввод в любое другое поле перерисовывает форму, и без этого
 * каждый символ пересобирал бы весь список, что заметно в браузере и в тестах.
 */
const TimeZoneSelect = memo(function TimeZoneSelect({
  value,
  disabled,
  invalid,
  describedBy,
  onChange,
}: {
  value: string
  disabled: boolean
  invalid: boolean
  describedBy: string | undefined
  onChange: (value: string) => void
}) {
  const timeZones = useMemo(() => salonTimeZones(value), [value])

  return (
    <select
      id="salon-time-zone"
      name="timeZoneId"
      value={value}
      disabled={disabled}
      aria-invalid={invalid}
      aria-describedby={describedBy}
      onChange={(event) => onChange(event.target.value)}
      className="h-11 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-8"
    >
      {timeZones.map((zone) => (
        <option key={zone} value={zone}>
          {zone}
        </option>
      ))}
    </select>
  )
})

function titleFor(scope: SalonScope, mode: SalonFormMode): string {
  if (mode === 'edit') {
    return 'Правка салона'
  }

  return scope === 'owner' ? 'Создать салон' : 'Новый салон'
}

function descriptionFor(scope: SalonScope, mode: SalonFormMode): ReactNode {
  if (scope === 'owner') {
    return mode === 'create'
      ? 'Салоны барбера проверяет администратор: после одобрения салон появится в поиске.'
      : 'Измените адрес и контакты. Название и владельца изменить нельзя.'
  }

  return 'Выключенный салон исчезает из поиска и с карточек, но связи барберов сохраняются.'
}

function submitLabelFor(scope: SalonScope, mode: SalonFormMode): string {
  if (mode === 'edit') {
    return 'Сохранить'
  }

  return scope === 'owner' ? 'Отправить на проверку' : 'Создать'
}
