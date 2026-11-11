import { useState, type FormEvent } from 'react'
import { CheckCircle2, Loader2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useAccountProfile } from '@/components/account/account-profile-context'
import { t, useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import type { MyProfile } from '@/lib/my-profile'

/**
 * Личные данные профиля.
 *
 * Редактируются только имя, фамилия и телефон: email — это логин, и он меняется
 * в отдельной карточке «Email» с подтверждением по ссылке. Кнопка сохранения
 * включается только при изменении, чтобы «Сохранить» не выглядело обязательным.
 */

type ProfileField = 'firstName' | 'lastName' | 'phoneNumber'

type ProfileValues = Record<ProfileField, string>

type ProfileErrors = Partial<Record<ProfileField, string>>

function toValues(profile: MyProfile): ProfileValues {
  return {
    firstName: profile.firstName,
    lastName: profile.lastName,
    phoneNumber: profile.phoneNumber ?? '',
  }
}

function validate(values: ProfileValues): ProfileErrors {
  const errors: ProfileErrors = {}

  if (!values.firstName.trim()) {
    errors.firstName = t('Введите имя.')
  }

  if (!values.lastName.trim()) {
    errors.lastName = t('Введите фамилию.')
  }

  return errors
}

function describeProfileError(error: unknown): {
  field?: ProfileField
  message: string
} {
  if (error instanceof ApiError) {
    if (error.hasCode('user.updateProfile.firstName')) {
      return { field: 'firstName', message: t('Введите имя.') }
    }

    if (error.hasCode('user.updateProfile.lastName')) {
      return { field: 'lastName', message: t('Введите фамилию.') }
    }
  }

  return { message: describeApiFailure(error, 'Не удалось сохранить профиль.') }
}

export function ProfileForm({ profile }: { profile: MyProfile }) {
  const { t } = useI18n()
  const { save } = useAccountProfile()
  const [baseline, setBaseline] = useState(() => toValues(profile))
  const [values, setValues] = useState(baseline)
  const [errors, setErrors] = useState<ProfileErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  const isDirty =
    values.firstName !== baseline.firstName ||
    values.lastName !== baseline.lastName ||
    values.phoneNumber !== baseline.phoneNumber

  function update(field: ProfileField, value: string) {
    setValues((current) => ({ ...current, [field]: value }))
    setErrors((current) => ({ ...current, [field]: undefined }))
    setFormError(null)
    setAnnouncement(null)
  }

  function handleReset() {
    setValues(baseline)
    setErrors({})
    setFormError(null)
    setAnnouncement(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmed: ProfileValues = {
      firstName: values.firstName.trim(),
      lastName: values.lastName.trim(),
      phoneNumber: values.phoneNumber.trim(),
    }
    const nextErrors = validate(trimmed)

    setErrors(nextErrors)
    setFormError(null)
    setAnnouncement(null)

    if (Object.keys(nextErrors).length > 0) {
      return
    }

    setIsPending(true)

    try {
      const saved = await save({
        firstName: trimmed.firstName,
        lastName: trimmed.lastName,
        phoneNumber: trimmed.phoneNumber || null,
        avatarUrl: profile.avatarUrl,
      })
      const nextValues = toValues(saved)

      setValues(nextValues)
      setBaseline(nextValues)
      setAnnouncement(t('Изменения сохранены.'))
    } catch (error) {
      const described = describeProfileError(error)
      const field = described.field

      if (field) {
        setErrors((current) => ({ ...current, [field]: described.message }))
      } else {
        setFormError(described.message)
      }
    } finally {
      setIsPending(false)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('Личные данные')}</CardTitle>
        <CardDescription>
          {t(
            'Имя и телефон видны вам и администратору. Барбер увидит имя в записи.'
          )}
        </CardDescription>
      </CardHeader>

      <CardContent>
        <form className="flex flex-col gap-5" onSubmit={handleSubmit} noValidate>
          <FieldGroup>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field data-invalid={Boolean(errors.firstName)}>
                <FieldLabel htmlFor="profile-first-name">{t('Имя')}</FieldLabel>
                <Input
                  id="profile-first-name"
                  name="firstName"
                  autoComplete="given-name"
                  maxLength={100}
                  value={values.firstName}
                  disabled={isPending}
                  aria-invalid={Boolean(errors.firstName)}
                  aria-describedby={
                    errors.firstName ? 'profile-first-name-error' : undefined
                  }
                  onChange={(event) => update('firstName', event.target.value)}
                />
                <FieldError id="profile-first-name-error">
                  {errors.firstName}
                </FieldError>
              </Field>

              <Field data-invalid={Boolean(errors.lastName)}>
                <FieldLabel htmlFor="profile-last-name">
                  {t('Фамилия')}
                </FieldLabel>
                <Input
                  id="profile-last-name"
                  name="lastName"
                  autoComplete="family-name"
                  maxLength={100}
                  value={values.lastName}
                  disabled={isPending}
                  aria-invalid={Boolean(errors.lastName)}
                  aria-describedby={
                    errors.lastName ? 'profile-last-name-error' : undefined
                  }
                  onChange={(event) => update('lastName', event.target.value)}
                />
                <FieldError id="profile-last-name-error">
                  {errors.lastName}
                </FieldError>
              </Field>
            </div>

            <Field>
              <FieldLabel htmlFor="profile-phone">{t('Телефон')}</FieldLabel>
              <Input
                id="profile-phone"
                name="phoneNumber"
                type="tel"
                autoComplete="tel"
                inputMode="tel"
                maxLength={40}
                placeholder="+7 900 000-00-00"
                value={values.phoneNumber}
                disabled={isPending}
                onChange={(event) => update('phoneNumber', event.target.value)}
              />
              <FieldDescription>
                {t(
                  'Необязательно. По этому номеру с вами может связаться салон.'
                )}
              </FieldDescription>
            </Field>
          </FieldGroup>

          {formError && (
            <p role="alert" className="text-sm text-destructive">
              {formError}
            </p>
          )}

          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="submit"
              className="h-11 sm:h-8"
              disabled={!isDirty || isPending}
            >
              {isPending && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {isPending ? t('Сохраняем…') : t('Сохранить')}
            </Button>
            <Button
              type="button"
              variant="outline"
              className="h-11 sm:h-8"
              disabled={!isDirty || isPending}
              onClick={handleReset}
            >
              {t('Отменить')}
            </Button>
          </div>

          {announcement && (
            <p
              role="status"
              className="flex items-center gap-1.5 text-sm text-success"
            >
              <CheckCircle2 className="size-4" aria-hidden />
              {announcement}
            </p>
          )}
        </form>
      </CardContent>
    </Card>
  )
}
