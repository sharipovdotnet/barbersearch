import { useEffect, useRef, useState, type FormEvent } from 'react'
import { CheckCircle2, Loader2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
} from '@/components/ui/card'
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
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { t, useI18n } from '@/i18n'
import {
  describeEmailChangeFailure,
  requestEmailChange,
  type EmailChangeField,
} from '@/lib/email-change'
import type { MyProfile } from '@/lib/my-profile'

/**
 * Карточка «Email» в `/account/profile`.
 *
 * Текущий адрес меняется только после перехода по ссылке из письма: запрос
 * отправляет ссылку на новый адрес и уведомление на старый, а `GET users/me`
 * до подтверждения по-прежнему отдаёт прежний email. Поэтому успех — это
 * `role="status"` «Письмо отправлено на …» и локальный кулдаун 60 секунд,
 * а не догадка о новом адресе в профиле.
 *
 * Кулдаун оставляет кнопку фокусируемой (`aria-disabled`, а не `disabled`):
 * после закрытия диалога фокус обязан вернуться на неё, а `disabled`-элемент
 * фокус не принимает.
 */

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

// Верхняя граница адреса — из серверного валидатора (3..254).
const MAX_EMAIL_LENGTH = 254

const RESEND_COOLDOWN_SECONDS = 60

const FALLBACK_ERROR = 'Не удалось изменить email. Попробуйте позже.'

type EmailChangeValues = Record<EmailChangeField, string>

type EmailChangeErrors = Partial<Record<EmailChangeField, string>>

const EMPTY_VALUES: EmailChangeValues = {
  newEmail: '',
  currentPassword: '',
}

export function EmailCard({ profile }: { profile: MyProfile }) {
  const { t } = useI18n()
  const [isOpen, setIsOpen] = useState(false)
  const [values, setValues] = useState<EmailChangeValues>(EMPTY_VALUES)
  const [errors, setErrors] = useState<EmailChangeErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)
  const [cooldownEndsAt, setCooldownEndsAt] = useState<number | null>(null)
  const [cooldownSeconds, setCooldownSeconds] = useState(0)

  const triggerRef = useRef<HTMLButtonElement>(null)
  const newEmailRef = useRef<HTMLInputElement>(null)
  const currentPasswordRef = useRef<HTMLInputElement>(null)
  const inFlightRef = useRef(false)
  const focusTriggerAfterClose = useRef(false)

  useEffect(() => {
    if (cooldownEndsAt === null) {
      return
    }

    const tick = () => {
      const remaining = Math.max(
        0,
        Math.ceil((cooldownEndsAt - Date.now()) / 1000)
      )

      setCooldownSeconds(remaining)

      if (remaining === 0) {
        setCooldownEndsAt(null)
      }
    }

    tick()
    const timer = window.setInterval(tick, 1000)

    return () => {
      window.clearInterval(timer)
    }
  }, [cooldownEndsAt])

  function startCooldown() {
    setCooldownSeconds(RESEND_COOLDOWN_SECONDS)
    setCooldownEndsAt(Date.now() + RESEND_COOLDOWN_SECONDS * 1000)
  }

  function openDialog() {
    if (isPending || cooldownSeconds > 0) {
      return
    }

    setValues(EMPTY_VALUES)
    setErrors({})
    setFormError(null)
    setAnnouncement(null)
    setIsOpen(true)
  }

  function update(field: EmailChangeField, value: string) {
    setValues((current) => ({ ...current, [field]: value }))
    setErrors((current) => ({ ...current, [field]: undefined }))
    setFormError(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (inFlightRef.current) {
      return
    }

    const newEmail = values.newEmail.trim()
    const nextErrors = validate(newEmail, values.currentPassword)

    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      if (nextErrors.newEmail) {
        newEmailRef.current?.focus()
      } else {
        currentPasswordRef.current?.focus()
      }

      return
    }

    inFlightRef.current = true
    setIsPending(true)

    try {
      await requestEmailChange(newEmail, values.currentPassword)
      setAnnouncement(t('Письмо отправлено на {email}', { email: newEmail }))
      setValues(EMPTY_VALUES)
      setErrors({})
      startCooldown()
      focusTriggerAfterClose.current = true
      setIsOpen(false)
    } catch (error) {
      const failure = describeEmailChangeFailure(error, FALLBACK_ERROR)
      const field = failure.field

      if (field) {
        setErrors((current) => ({ ...current, [field]: failure.message }))

        if (field === 'newEmail') {
          newEmailRef.current?.focus()
        } else {
          currentPasswordRef.current?.focus()
        }
      } else {
        setFormError(failure.message)
      }
    } finally {
      inFlightRef.current = false
      setIsPending(false)
    }
  }

  return (
    <section
      aria-labelledby="account-email-heading"
      aria-busy={isPending || undefined}
    >
      <Card>
        <CardHeader>
          <h2
            id="account-email-heading"
            className="font-heading text-base leading-snug font-medium tracking-tight"
          >
            {t('Email')}
          </h2>
          <CardDescription>
            {t(
              'Email используется для входа. Новый адрес подтверждается ссылкой из письма.'
            )}
          </CardDescription>
        </CardHeader>

        <CardContent className="space-y-3">
          <div className="space-y-1">
            <p className="text-sm font-medium break-all">{profile.email}</p>
            <p
              className={
                profile.isEmailVerified
                  ? 'text-sm text-success'
                  : 'text-sm text-warning'
              }
            >
              {profile.isEmailVerified
                ? t('Адрес подтверждён.')
                : t('Адрес не подтверждён.')}
            </p>
          </div>

          <Button
            ref={triggerRef}
            type="button"
            className="h-11 sm:h-8 aria-disabled:pointer-events-none aria-disabled:opacity-50"
            disabled={isPending}
            aria-disabled={cooldownSeconds > 0 || undefined}
            aria-busy={isPending || undefined}
            onClick={openDialog}
          >
            {cooldownSeconds > 0
              ? t('Повторить через {count} с', { count: cooldownSeconds })
              : t('Изменить email')}
          </Button>

          {announcement && (
            <p
              role="status"
              className="flex items-center gap-1.5 text-sm text-success"
            >
              <CheckCircle2 className="size-4" aria-hidden />
              {announcement}
            </p>
          )}
        </CardContent>
      </Card>

      <Dialog
        open={isOpen}
        onOpenChange={(open) => {
          if (!open) {
            focusTriggerAfterClose.current = true
            setIsOpen(false)
          }
        }}
        onOpenChangeComplete={(open) => {
          if (!open && focusTriggerAfterClose.current) {
            focusTriggerAfterClose.current = false
            triggerRef.current?.focus()
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t('Изменить email')}</DialogTitle>
            <DialogDescription>
              {t(
                'На новый адрес придёт ссылка для подтверждения, на старый — уведомление. Адрес изменится после перехода по ссылке.'
              )}
            </DialogDescription>
          </DialogHeader>

          <form
            noValidate
            aria-busy={isPending}
            className="flex flex-col gap-4"
            onSubmit={handleSubmit}
          >
            <FieldGroup>
              <Field data-invalid={Boolean(errors.newEmail)}>
                <FieldLabel htmlFor="email-change-new">
                  {t('Новый email')}
                </FieldLabel>
                <Input
                  ref={newEmailRef}
                  id="email-change-new"
                  name="newEmail"
                  type="email"
                  autoComplete="email"
                  inputMode="email"
                  spellCheck={false}
                  maxLength={MAX_EMAIL_LENGTH}
                  value={values.newEmail}
                  disabled={isPending}
                  aria-invalid={Boolean(errors.newEmail)}
                  aria-describedby={
                    errors.newEmail ? 'email-change-new-error' : undefined
                  }
                  onChange={(event) =>
                    update('newEmail', event.target.value)
                  }
                />
                <FieldError id="email-change-new-error">
                  {errors.newEmail}
                </FieldError>
              </Field>

              <Field data-invalid={Boolean(errors.currentPassword)}>
                <FieldLabel htmlFor="email-change-password">
                  {t('Текущий пароль')}
                </FieldLabel>
                <Input
                  ref={currentPasswordRef}
                  id="email-change-password"
                  name="currentPassword"
                  type="password"
                  autoComplete="current-password"
                  value={values.currentPassword}
                  disabled={isPending}
                  aria-invalid={Boolean(errors.currentPassword)}
                  aria-describedby={
                    errors.currentPassword
                      ? 'email-change-password-error'
                      : undefined
                  }
                  onChange={(event) =>
                    update('currentPassword', event.target.value)
                  }
                />
                <FieldError id="email-change-password-error">
                  {errors.currentPassword}
                </FieldError>
              </Field>
            </FieldGroup>

            {formError && (
              <p role="alert" className="text-sm text-destructive">
                {formError}
              </p>
            )}

            <DialogFooter className="flex-col sm:flex-row">
              <Button
                type="button"
                variant="outline"
                className="h-11 sm:h-8"
                disabled={isPending}
                onClick={() => {
                  focusTriggerAfterClose.current = true
                  setIsOpen(false)
                }}
              >
                {t('Отмена')}
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
                {isPending ? t('Отправляем письмо…') : t('Отправить письмо')}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </section>
  )
}

/** Тот же простой паттерн, что в `login-dialog.tsx`: пусто и один `@`. */
function validate(
  newEmail: string,
  currentPassword: string
): EmailChangeErrors {
  const errors: EmailChangeErrors = {}

  if (!newEmail) {
    errors.newEmail = t('Введите email.')
  } else if (!EMAIL_PATTERN.test(newEmail)) {
    errors.newEmail = t('Похоже, в адресе опечатка.')
  }

  if (!currentPassword) {
    errors.currentPassword = t('Введите текущий пароль.')
  }

  return errors
}
