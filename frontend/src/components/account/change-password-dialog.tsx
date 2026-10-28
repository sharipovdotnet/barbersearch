import { useState, type FormEvent } from 'react'
import { Loader2 } from 'lucide-react'

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
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { ApiError } from '@/lib/api'
import {
  changePassword,
  MAX_PASSWORD_LENGTH,
  MIN_PASSWORD_LENGTH,
} from '@/lib/auth'
import { describeApiFailure } from '@/lib/errors'

type PasswordField = 'current' | 'next' | 'confirm'
type PasswordErrors = Partial<Record<PasswordField, string>>

const EMPTY: Record<PasswordField, string> = {
  current: '',
  next: '',
  confirm: '',
}

function validate(values: Record<PasswordField, string>): PasswordErrors {
  const errors: PasswordErrors = {}

  if (!values.current) {
    errors.current = 'Введите текущий пароль.'
  }

  if (!values.next) {
    errors.next = 'Введите новый пароль.'
  } else if (values.next.length < MIN_PASSWORD_LENGTH) {
    errors.next = `Минимум ${MIN_PASSWORD_LENGTH} символов.`
  } else if (values.next.length > MAX_PASSWORD_LENGTH) {
    errors.next = `Максимум ${MAX_PASSWORD_LENGTH} символов.`
  } else if (values.next === values.current) {
    errors.next = 'Новый пароль должен отличаться от текущего.'
  }

  if (values.confirm !== values.next) {
    errors.confirm = 'Пароли не совпадают.'
  }

  return errors
}

function describePasswordError(error: unknown): {
  field?: PasswordField
  message: string
} {
  if (error instanceof ApiError) {
    if (error.hasCode('auth.changePassword.invalidCurrentPassword')) {
      return { field: 'current', message: 'Текущий пароль неверный.' }
    }

    if (error.hasCode('auth.password.tooShort')) {
      return { field: 'next', message: `Минимум ${MIN_PASSWORD_LENGTH} символов.` }
    }

    if (error.hasCode('auth.password.tooLong')) {
      return {
        field: 'next',
        message: `Максимум ${MAX_PASSWORD_LENGTH} символов.`,
      }
    }

    if (error.hasCode('auth.password.common')) {
      return { field: 'next', message: 'Этот пароль слишком распространён.' }
    }

    if (error.hasCode('auth.password.equalsEmail')) {
      return { field: 'next', message: 'Пароль не должен совпадать с email.' }
    }
  }

  return { message: describeApiFailure(error, 'Не удалось сменить пароль.') }
}

/**
 * Смена пароля.
 *
 * Форма монтируется только при открытии: поля и ошибки не переезжают с прошлой
 * попытки, и сбрасывать их эффектом не нужно.
 */
export function ChangePasswordDialog({
  open,
  onOpenChange,
  onSuccess,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSuccess: () => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {open && (
        <DialogContent>
          <PasswordForm
            onClose={() => onOpenChange(false)}
            onSuccess={onSuccess}
          />
        </DialogContent>
      )}
    </Dialog>
  )
}

function PasswordForm({
  onClose,
  onSuccess,
}: {
  onClose: () => void
  onSuccess: () => void
}) {
  const [values, setValues] = useState(EMPTY)
  const [errors, setErrors] = useState<PasswordErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  function update(field: PasswordField, value: string) {
    setValues((current) => ({ ...current, [field]: value }))
    setErrors((current) => ({ ...current, [field]: undefined }))
    setFormError(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const nextErrors = validate(values)
    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      return
    }

    setIsPending(true)

    try {
      await changePassword(values.current, values.next)
      onSuccess()
      onClose()
    } catch (cause) {
      const described = describePasswordError(cause)
      const field = described.field

      if (field) {
        setErrors((current) => ({ ...current, [field]: described.message }))
      } else {
        setFormError(described.message)
      }

      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Смена пароля</DialogTitle>
        <DialogDescription>
          Пароль от 12 символов. После смены остальные сессии входа завершатся —
          здесь вы останетесь.
        </DialogDescription>
      </DialogHeader>

      <form className="flex flex-col gap-4" onSubmit={handleSubmit} noValidate>
        <FieldGroup>
          <Field data-invalid={Boolean(errors.current)}>
            <FieldLabel htmlFor="password-current">Текущий пароль</FieldLabel>
            <Input
              id="password-current"
              name="current"
              type="password"
              autoComplete="current-password"
              value={values.current}
              disabled={isPending}
              aria-invalid={Boolean(errors.current)}
              aria-describedby={errors.current ? 'password-current-error' : undefined}
              onChange={(event) => update('current', event.target.value)}
            />
            <FieldError id="password-current-error">{errors.current}</FieldError>
          </Field>

          <Field data-invalid={Boolean(errors.next)}>
            <FieldLabel htmlFor="password-new">Новый пароль</FieldLabel>
            <Input
              id="password-new"
              name="newPassword"
              type="password"
              autoComplete="new-password"
              value={values.next}
              disabled={isPending}
              aria-invalid={Boolean(errors.next)}
              aria-describedby={errors.next ? 'password-new-error' : undefined}
              onChange={(event) => update('next', event.target.value)}
            />
            <FieldError id="password-new-error">{errors.next}</FieldError>
          </Field>

          <Field data-invalid={Boolean(errors.confirm)}>
            <FieldLabel htmlFor="password-confirm">Повторите пароль</FieldLabel>
            <Input
              id="password-confirm"
              name="confirmPassword"
              type="password"
              autoComplete="new-password"
              value={values.confirm}
              disabled={isPending}
              aria-invalid={Boolean(errors.confirm)}
              aria-describedby={errors.confirm ? 'password-confirm-error' : undefined}
              onChange={(event) => update('confirm', event.target.value)}
            />
            <FieldError id="password-confirm-error">{errors.confirm}</FieldError>
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
            onClick={onClose}
            disabled={isPending}
          >
            Отмена
          </Button>
          <Button type="submit" disabled={isPending}>
            {isPending && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {isPending ? 'Сохраняем…' : 'Сменить пароль'}
          </Button>
        </DialogFooter>
      </form>
    </>
  )
}
