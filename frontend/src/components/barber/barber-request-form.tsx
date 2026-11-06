import { useRef, useState, type FormEvent, type RefObject } from 'react'
import { Loader2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import {
  BARBER_REQUEST_LIMITS,
  createBarberRequest,
  describeBarberRequestCreateFailure,
  type BarberRequest,
  type BarberRequestCreateFailure,
} from '@/lib/barber-requests'
import {
  BARBER_REQUEST_FIELD_ORDER,
  validateBarberRequestForm,
  type BarberRequestField,
  type BarberRequestFormErrors,
} from '@/lib/barber-request-form'

/**
 * Форма заявки «Стать барбером».
 *
 * Роль в теле не передаётся: `POST /api/barber-requests` создаёт только заявку,
 * а `Barber` выдаёт администратор. `201` возвращает саму заявку, поэтому она
 * сразу отдаётся наверх: `CustomerSection` заменяет всю секцию формой одного
 * статуса, без второго чтения и без надежды на то, что `GET /mine` уже успел
 * обновиться.
 */
export function BarberRequestForm({
  onCreated,
}: {
  /** Созданная заявка: родитель показывает её вместо формы. */
  onCreated: (request: BarberRequest) => void
}) {
  const [displayName, setDisplayName] = useState('')
  const [yearsOfExperience, setYearsOfExperience] = useState('')
  const [city, setCity] = useState('')
  const [about, setAbout] = useState('')
  const [hasSalon, setHasSalon] = useState(false)
  const [errors, setErrors] = useState<BarberRequestFormErrors>({})
  const [failure, setFailure] = useState<BarberRequestCreateFailure | null>(null)
  const [isPending, setIsPending] = useState(false)

  const displayNameRef = useRef<HTMLInputElement>(null)
  const yearsRef = useRef<HTMLInputElement>(null)
  const cityRef = useRef<HTMLInputElement>(null)
  const aboutRef = useRef<HTMLTextAreaElement>(null)

  const fieldRefs: Record<
    BarberRequestField,
    RefObject<HTMLInputElement | HTMLTextAreaElement | null>
  > = {
    displayName: displayNameRef,
    yearsOfExperience: yearsRef,
    city: cityRef,
    about: aboutRef,
  }

  function focusFirstError(nextErrors: BarberRequestFormErrors) {
    for (const field of BARBER_REQUEST_FIELD_ORDER) {
      if (!nextErrors[field]) {
        continue
      }

      fieldRefs[field].current?.focus()
      return
    }
  }

  async function submit() {
    // Повторный клик и Enter, пока идёт запрос, не должны создать вторую заявку.
    if (isPending) {
      return
    }

    const trimmedAbout = about.trim()
    const nextErrors = validateBarberRequestForm({
      displayName,
      yearsOfExperience,
      city,
      about,
    })

    setErrors(nextErrors)
    setFailure(null)

    if (Object.keys(nextErrors).length > 0) {
      focusFirstError(nextErrors)
      return
    }

    setIsPending(true)

    try {
      const request = await createBarberRequest({
        displayName: displayName.trim(),
        yearsOfExperience: Number(yearsOfExperience.trim()),
        city: city.trim(),
        about: trimmedAbout === '' ? null : trimmedAbout,
        hasSalon,
      })

      onCreated(request)
    } catch (cause) {
      setFailure(describeBarberRequestCreateFailure(cause))
    } finally {
      setIsPending(false)
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    void submit()
  }

  return (
    <form
      className="flex flex-col gap-4"
      noValidate
      aria-busy={isPending}
      onSubmit={handleSubmit}
    >
      <FieldGroup>
        <Field data-invalid={Boolean(errors.displayName)}>
          <FieldLabel htmlFor="barber-request-display-name">
            Как вас представить
          </FieldLabel>
          <Input
            ref={displayNameRef}
            id="barber-request-display-name"
            name="displayName"
            type="text"
            autoComplete="name"
            maxLength={BARBER_REQUEST_LIMITS.displayName}
            value={displayName}
            disabled={isPending}
            aria-invalid={Boolean(errors.displayName)}
            aria-describedby={
              errors.displayName
                ? 'barber-request-display-name-error'
                : undefined
            }
            onChange={(event) => {
              setDisplayName(event.target.value)
              setErrors((current) => ({ ...current, displayName: undefined }))
              setFailure(null)
            }}
          />
          <FieldDescription>
            Имя увидят клиенты в поиске и на карточке барбера.
          </FieldDescription>
          <FieldError id="barber-request-display-name-error">
            {errors.displayName}
          </FieldError>
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field data-invalid={Boolean(errors.yearsOfExperience)}>
            <FieldLabel htmlFor="barber-request-years">Опыт, лет</FieldLabel>
            <Input
              ref={yearsRef}
              id="barber-request-years"
              name="yearsOfExperience"
              type="number"
              inputMode="numeric"
              min={1}
              max={BARBER_REQUEST_LIMITS.yearsOfExperience}
              step={1}
              value={yearsOfExperience}
              disabled={isPending}
              aria-invalid={Boolean(errors.yearsOfExperience)}
              aria-describedby={
                errors.yearsOfExperience
                  ? 'barber-request-years-error'
                  : undefined
              }
              onChange={(event) => {
                setYearsOfExperience(event.target.value)
                setErrors((current) => ({
                  ...current,
                  yearsOfExperience: undefined,
                }))
                setFailure(null)
              }}
            />
            <FieldError id="barber-request-years-error">
              {errors.yearsOfExperience}
            </FieldError>
          </Field>

          <Field data-invalid={Boolean(errors.city)}>
            <FieldLabel htmlFor="barber-request-city">Город</FieldLabel>
            <Input
              ref={cityRef}
              id="barber-request-city"
              name="city"
              type="text"
              autoComplete="address-level2"
              maxLength={BARBER_REQUEST_LIMITS.city}
              value={city}
              disabled={isPending}
              aria-invalid={Boolean(errors.city)}
              aria-describedby={
                errors.city ? 'barber-request-city-error' : undefined
              }
              onChange={(event) => {
                setCity(event.target.value)
                setErrors((current) => ({ ...current, city: undefined }))
                setFailure(null)
              }}
            />
            <FieldError id="barber-request-city-error">{errors.city}</FieldError>
          </Field>
        </div>

        <Field data-invalid={Boolean(errors.about)}>
          <FieldLabel htmlFor="barber-request-about">
            О себе — необязательно
          </FieldLabel>
          <Textarea
            ref={aboutRef}
            id="barber-request-about"
            name="about"
            rows={4}
            maxLength={BARBER_REQUEST_LIMITS.about}
            value={about}
            disabled={isPending}
            aria-invalid={Boolean(errors.about)}
            aria-describedby={
              errors.about
                ? 'barber-request-about-hint barber-request-about-error'
                : 'barber-request-about-hint'
            }
            onChange={(event) => {
              setAbout(event.target.value)
              setErrors((current) => ({ ...current, about: undefined }))
              setFailure(null)
            }}
          />
          <FieldDescription id="barber-request-about-hint">
            Опыт, специализации, любимые услуги. До{' '}
            {BARBER_REQUEST_LIMITS.about} символов.
          </FieldDescription>
          <FieldError id="barber-request-about-error">{errors.about}</FieldError>
        </Field>

        <Field orientation="horizontal">
          <Checkbox
            id="barber-request-has-salon"
            name="hasSalon"
            checked={hasSalon}
            disabled={isPending}
            onCheckedChange={(checked) => {
              setHasSalon(checked)
              setFailure(null)
            }}
          />
          <FieldContent>
            <FieldLabel
              htmlFor="barber-request-has-salon"
              className="font-normal"
            >
              Уже работаю в салоне
            </FieldLabel>
            <FieldDescription>
              Отметьте, если у вас есть место, где вы принимаете клиентов.
            </FieldDescription>
          </FieldContent>
        </Field>
      </FieldGroup>

      {failure && (
        <div role="alert" className="space-y-2 text-sm text-destructive">
          <p>{failure.message}</p>
          {failure.canRetry && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              disabled={isPending}
              onClick={() => {
                void submit()
              }}
            >
              Повторить
            </Button>
          )}
        </div>
      )}

      <Button
        type="submit"
        className="h-11 w-full sm:h-9"
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
        {isPending ? 'Отправляем…' : 'Отправить заявку'}
      </Button>
    </form>
  )
}
