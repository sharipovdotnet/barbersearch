import { useState, type FormEvent, type ReactNode } from 'react'
import { Loader2, LogIn } from 'lucide-react'

import { useSession } from '@/hooks/use-session'
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
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { REGISTER_PATH } from '@/lib/register-path'
import { isPlainClick, navigate } from '@/lib/router'
import { describeSignInError } from '@/lib/sign-in-errors'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

// Верхняя граница совпадает с PasswordPolicy.MAX_LENGTH на сервере.
const MAX_PASSWORD_LENGTH = 128

type LoginErrors = {
  email?: string
  password?: string
}

function validate(email: string, password: string): LoginErrors {
  const errors: LoginErrors = {}

  if (!email) {
    errors.email = 'Введите email.'
  } else if (!EMAIL_PATTERN.test(email)) {
    errors.email = 'Похоже, в адресе опечатка.'
  }

  if (!password) {
    errors.password = 'Введите пароль.'
  } else if (password.length > MAX_PASSWORD_LENGTH) {
    errors.password = `Максимум ${MAX_PASSWORD_LENGTH} символов.`
  }

  return errors
}

function LoginForm({
  onSuccess,
  onRegister,
}: {
  onSuccess: () => void
  onRegister: () => void
}) {
  const { signIn } = useSession()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(false)
  const [errors, setErrors] = useState<LoginErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const normalizedEmail = email.trim()
    const nextErrors = validate(normalizedEmail, password)

    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      return
    }

    setIsPending(true)

    try {
      await signIn({
        email: normalizedEmail,
        password,
        rememberMe,
      })
      onSuccess()
    } catch (error) {
      setFormError(describeSignInError(error))
      setIsPending(false)
    }
  }

  return (
    <form
      className="flex flex-col gap-4"
      noValidate
      aria-busy={isPending}
      onSubmit={handleSubmit}
    >
      <FieldGroup>
        <Field data-invalid={Boolean(errors.email)}>
          <FieldLabel htmlFor="login-email">Email</FieldLabel>
          <Input
            id="login-email"
            name="email"
            type="email"
            autoComplete="email"
            inputMode="email"
            spellCheck={false}
            placeholder="you@example.com"
            value={email}
            disabled={isPending}
            aria-invalid={Boolean(errors.email)}
            aria-describedby={errors.email ? 'login-email-error' : undefined}
            onChange={(event) => {
              setEmail(event.target.value)
              setErrors((current) => ({ ...current, email: undefined }))
              setFormError(null)
            }}
          />
          <FieldError id="login-email-error">{errors.email}</FieldError>
        </Field>

        <Field data-invalid={Boolean(errors.password)}>
          <FieldLabel htmlFor="login-password">Пароль</FieldLabel>
          <Input
            id="login-password"
            name="password"
            type="password"
            autoComplete="current-password"
            value={password}
            disabled={isPending}
            aria-invalid={Boolean(errors.password)}
            aria-describedby={
              errors.password ? 'login-password-error' : undefined
            }
            onChange={(event) => {
              setPassword(event.target.value)
              setErrors((current) => ({ ...current, password: undefined }))
              setFormError(null)
            }}
          />
          <FieldError id="login-password-error">{errors.password}</FieldError>
        </Field>

        <Field orientation="horizontal">
          <Checkbox
            id="login-remember"
            checked={rememberMe}
            disabled={isPending}
            onCheckedChange={(checked) => setRememberMe(checked)}
          />
          <FieldLabel
            htmlFor="login-remember"
            className="font-normal text-muted-foreground"
          >
            Запомнить меня
          </FieldLabel>
        </Field>
      </FieldGroup>

      {formError && (
        <p role="alert" className="text-sm text-destructive">
          {formError}
        </p>
      )}

      <p className="text-center text-sm text-muted-foreground">
        Нет аккаунта?{' '}
        <a
          href={REGISTER_PATH}
          className="rounded-sm font-medium text-foreground underline underline-offset-4 hover:text-foreground/80 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
          onClick={(event) => {
            // Ctrl/Cmd-клик не перехватываем: `/register` должен открыться в новой вкладке.
            if (!isPlainClick(event)) {
              return
            }

            event.preventDefault()
            onRegister()
          }}
        >
          Зарегистрироваться
        </a>
      </p>

      <DialogFooter>
        <Button type="submit" className="w-full sm:w-auto" disabled={isPending}>
          {isPending && (
            <Loader2
              data-icon="inline-start"
              aria-hidden
              className="animate-spin"
            />
          )}
          {isPending ? 'Входим…' : 'Войти'}
        </Button>
      </DialogFooter>
    </form>
  )
}

function LoginDialog({
  trigger,
  variant = 'ghost',
  size = 'sm',
  className,
  redirectTo = '/account',
  onSignedIn,
}: {
  /** Содержимое кнопки-триггера; по умолчанию — «Войти» с иконкой. */
  trigger?: ReactNode
  variant?: 'ghost' | 'default'
  size?: 'sm' | 'default' | 'lg'
  className?: string
  /** Куда вернуться после входа; по умолчанию — кабинет клиента, `null` — остаться здесь. */
  redirectTo?: string | null
  /** Дополнительное действие после успешного входа, например повторный запрос данных. */
  onSignedIn?: () => void
}) {
  const [isOpen, setIsOpen] = useState(false)

  return (
    <Dialog open={isOpen} onOpenChange={setIsOpen}>
      <DialogTrigger render={<Button variant={variant} size={size} className={className} />}>
        {trigger ?? (
          <>
            <LogIn data-icon="inline-start" aria-hidden />
            Войти
          </>
        )}
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Вход в BarberSearch</DialogTitle>
          <DialogDescription>
            Войдите, чтобы записываться к барберам и управлять бронированиями.
          </DialogDescription>
        </DialogHeader>
        <LoginForm
          onSuccess={() => {
            setIsOpen(false)
            // После входа пользователь возвращается туда, откуда открыл диалог.
            // `null` — выбора слота на странице барбера: уходить с неё нельзя.
            if (redirectTo) {
              navigate(redirectTo)
            }
            onSignedIn?.()
          }}
          onRegister={() => {
            // Диалог живёт в шапке и без закрытия остался бы поверх `/register`.
            setIsOpen(false)
            navigate(REGISTER_PATH)
          }}
        />
      </DialogContent>
    </Dialog>
  )
}

export { LoginDialog }
