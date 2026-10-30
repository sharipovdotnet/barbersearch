import { useRef, useState, type FormEvent } from 'react'
import { Loader2 } from 'lucide-react'

import { LoginDialog } from '@/components/login-dialog'
import { Button } from '@/components/ui/button'
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useSession } from '@/hooks/use-session'
import { ApiError } from '@/lib/api'
import {
  MAX_PASSWORD_LENGTH,
  MIN_PASSWORD_LENGTH,
  register,
  type RegistrationInput,
} from '@/lib/auth'
import { describeApiFailure } from '@/lib/errors'
import { navigate } from '@/lib/router'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

// Верхняя граница адреса — из серверного валидатора (3..254).
const MAX_EMAIL_LENGTH = 254

// Сообщения клиентских проверок и серверных кодов совпадают: пользователь не должен
// видеть два разных текста об одном и том же правиле.
const FIRST_NAME_ERROR = 'Введите имя.'
const LAST_NAME_ERROR = 'Введите фамилию.'
const EMAIL_ERROR = 'Проверьте email: нужен адрес вида name@example.com.'
const PASSWORD_REQUIRED_ERROR = 'Введите пароль.'
const PASSWORD_SHORT_ERROR = `Пароль должен быть не короче ${MIN_PASSWORD_LENGTH} символов.`
const PASSWORD_LONG_ERROR = `Пароль должен быть не длиннее ${MAX_PASSWORD_LENGTH} символов.`
const PASSWORD_COMMON_ERROR = 'Этот пароль слишком распространён. Выберите другой.'
const PASSWORD_EQUALS_EMAIL_ERROR = 'Пароль не должен совпадать с email.'
const PASSWORD_EQUALS_NAME_ERROR =
  'Пароль не должен совпадать с именем или фамилией.'
const PASSWORD_REPEAT_ERROR = 'Пароли не совпадают.'
const EMAIL_TAKEN_ERROR =
  'Аккаунт с таким email уже есть. Войдите или укажите другой адрес.'
const RATE_LIMIT_ERROR =
  'Слишком много попыток регистрации. Подождите минуту и повторите.'
const FALLBACK_ERROR = 'Не удалось создать аккаунт. Попробуйте ещё раз.'

const FIELD_ORDER = [
  'firstName',
  'lastName',
  'email',
  'password',
  'passwordRepeat',
] as const

/** Поля формы в том же порядке, в котором они идут по Tab. */
type RegisterField = (typeof FIELD_ORDER)[number]
type RegisterErrors = Partial<Record<RegisterField, string>>

type RegisterValues = {
  firstName: string
  lastName: string
  email: string
  password: string
  passwordRepeat: string
}

// Словарь серверных паролей на клиенте не воспроизводится: проверка живёт на сервере,
// здесь только её публичные коды.
const PASSWORD_CODE_MESSAGES: Record<string, string> = {
  'auth.password.required': PASSWORD_REQUIRED_ERROR,
  'auth.password.tooShort': PASSWORD_SHORT_ERROR,
  'auth.password.tooLong': PASSWORD_LONG_ERROR,
  'auth.password.common': PASSWORD_COMMON_ERROR,
  'auth.password.equalsEmail': PASSWORD_EQUALS_EMAIL_ERROR,
  'auth.password.equalsName': PASSWORD_EQUALS_NAME_ERROR,
}

function emailLocalPart(email: string): string {
  return email.split('@')[0] ?? ''
}

/**
 * Клиентские проверки формы.
 *
 * Пароль не trim-ится: пробелы внутри и по краям для сервера значимы. Email и имена
 * срезаются только для проверки и отправки — «исправлять» данные клиент не должен.
 */
function validate(values: RegisterValues): RegisterErrors {
  const errors: RegisterErrors = {}
  const firstName = values.firstName.trim()
  const lastName = values.lastName.trim()
  const email = values.email.trim()
  const { password } = values

  if (!firstName) {
    errors.firstName = FIRST_NAME_ERROR
  }

  if (!lastName) {
    errors.lastName = LAST_NAME_ERROR
  }

  if (!email) {
    errors.email = 'Введите email.'
  } else if (email.length > MAX_EMAIL_LENGTH || !EMAIL_PATTERN.test(email)) {
    errors.email = EMAIL_ERROR
  }

  if (!password) {
    errors.password = PASSWORD_REQUIRED_ERROR
  } else if (password.length < MIN_PASSWORD_LENGTH) {
    errors.password = PASSWORD_SHORT_ERROR
  } else if (password.length > MAX_PASSWORD_LENGTH) {
    errors.password = PASSWORD_LONG_ERROR
  } else if (password === email || password === emailLocalPart(email)) {
    errors.password = PASSWORD_EQUALS_EMAIL_ERROR
  } else if (password === firstName || password === lastName) {
    errors.password = PASSWORD_EQUALS_NAME_ERROR
  }

  if (values.passwordRepeat !== password) {
    errors.passwordRepeat = PASSWORD_REPEAT_ERROR
  }

  return errors
}

type RegisterFailure = {
  fields: RegisterErrors
  /** Общая ошибка формы; `null`, когда всё разложено по полям. */
  form: string | null
}

/**
 * Публичные коды сервера → русский текст.
 *
 * Английский `detail` не показываем никогда: он диагностический, а не контракт.
 * Коды пароля и имён привязываются к своим полям, остальное — к форме.
 */
function describeRegisterFailure(error: unknown): RegisterFailure {
  if (!(error instanceof ApiError)) {
    return { fields: {}, form: FALLBACK_ERROR }
  }

  if (error.status === 0) {
    return { fields: {}, form: describeApiFailure(error, FALLBACK_ERROR) }
  }

  if (error.status === 429) {
    return { fields: {}, form: RATE_LIMIT_ERROR }
  }

  if (error.hasCode('user.create.emailTaken')) {
    return { fields: {}, form: EMAIL_TAKEN_ERROR }
  }

  const fields: RegisterErrors = {}
  let hasUnknownCode = false

  for (const code of error.codes) {
    const passwordMessage = PASSWORD_CODE_MESSAGES[code]

    if (passwordMessage) {
      fields.password = passwordMessage
    } else if (code === 'user.email') {
      fields.email = EMAIL_ERROR
    } else if (code === 'user.firstName') {
      fields.firstName = FIRST_NAME_ERROR
    } else if (code === 'user.lastName') {
      fields.lastName = LAST_NAME_ERROR
    } else {
      hasUnknownCode = true
    }
  }

  const hasFieldErrors = Object.keys(fields).length > 0

  return {
    fields,
    form: hasUnknownCode || !hasFieldErrors ? FALLBACK_ERROR : null,
  }
}

/**
 * Форма самостоятельной регистрации клиента.
 *
 * Регистрация только создаёт аккаунт: после `201` форма сама входит тем же паролем и
 * уводит в `/account`. Если авто-вход не удался, повторять регистрацию нельзя — второй
 * запрос получил бы `409`, поэтому форма заменяется карточкой успеха со входом.
 */
export function RegisterForm() {
  const { signIn } = useSession()

  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [passwordRepeat, setPasswordRepeat] = useState('')
  const [errors, setErrors] = useState<RegisterErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)
  const [isCreated, setIsCreated] = useState(false)

  const firstNameRef = useRef<HTMLInputElement>(null)
  const lastNameRef = useRef<HTMLInputElement>(null)
  const emailRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)
  const passwordRepeatRef = useRef<HTMLInputElement>(null)

  const fieldRefs: Record<RegisterField, typeof firstNameRef> = {
    firstName: firstNameRef,
    lastName: lastNameRef,
    email: emailRef,
    password: passwordRef,
    passwordRepeat: passwordRepeatRef,
  }

  function focusFirstError(nextErrors: RegisterErrors) {
    for (const field of FIELD_ORDER) {
      if (nextErrors[field]) {
        fieldRefs[field].current?.focus()
        return
      }
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    // Повторный клик и Enter после 201 не должны создать второй аккаунт.
    if (isPending || isCreated) {
      return
    }

    const values: RegisterValues = {
      firstName,
      lastName,
      email,
      password,
      passwordRepeat,
    }
    const trimmed: RegistrationInput = {
      email: email.trim(),
      password,
      firstName: firstName.trim(),
      lastName: lastName.trim(),
    }

    const nextErrors = validate(values)
    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      focusFirstError(nextErrors)
      return
    }

    setIsPending(true)

    try {
      await register(trimmed)
    } catch (error) {
      const failure = describeRegisterFailure(error)

      setErrors(failure.fields)
      setFormError(failure.form)
      setIsPending(false)
      focusFirstError(failure.fields)
      return
    }

    // Аккаунт уже существует: дальше только обычный вход, без второго `POST /register`.
    setIsCreated(true)

    try {
      await signIn({
        email: trimmed.email,
        password: trimmed.password,
        rememberMe: false,
      })
      navigate('/account')
    } catch {
      // Карточка успеха вместо ошибки: аккаунт создан, осталось войти вручную.
      setIsPending(false)
    }
  }

  if (isCreated) {
    return <RegistrationCreated />
  }

  return (
    <form
      className="flex flex-col gap-4"
      noValidate
      aria-busy={isPending}
      onSubmit={handleSubmit}
    >
      <FieldGroup>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field data-invalid={Boolean(errors.firstName)}>
            <FieldLabel htmlFor="register-first-name">Имя</FieldLabel>
            <Input
              ref={firstNameRef}
              id="register-first-name"
              name="firstName"
              type="text"
              autoComplete="given-name"
              value={firstName}
              disabled={isPending}
              aria-invalid={Boolean(errors.firstName)}
              aria-describedby={
                errors.firstName ? 'register-first-name-error' : undefined
              }
              onChange={(event) => {
                setFirstName(event.target.value)
                setErrors((current) => ({ ...current, firstName: undefined }))
                setFormError(null)
              }}
            />
            <FieldError id="register-first-name-error">
              {errors.firstName}
            </FieldError>
          </Field>

          <Field data-invalid={Boolean(errors.lastName)}>
            <FieldLabel htmlFor="register-last-name">Фамилия</FieldLabel>
            <Input
              ref={lastNameRef}
              id="register-last-name"
              name="lastName"
              type="text"
              autoComplete="family-name"
              value={lastName}
              disabled={isPending}
              aria-invalid={Boolean(errors.lastName)}
              aria-describedby={
                errors.lastName ? 'register-last-name-error' : undefined
              }
              onChange={(event) => {
                setLastName(event.target.value)
                setErrors((current) => ({ ...current, lastName: undefined }))
                setFormError(null)
              }}
            />
            <FieldError id="register-last-name-error">
              {errors.lastName}
            </FieldError>
          </Field>
        </div>

        <Field data-invalid={Boolean(errors.email)}>
          <FieldLabel htmlFor="register-email">Email</FieldLabel>
          <Input
            ref={emailRef}
            id="register-email"
            name="email"
            type="email"
            autoComplete="email"
            inputMode="email"
            spellCheck={false}
            placeholder="you@example.com"
            value={email}
            disabled={isPending}
            aria-invalid={Boolean(errors.email)}
            aria-describedby={errors.email ? 'register-email-error' : undefined}
            onChange={(event) => {
              setEmail(event.target.value)
              setErrors((current) => ({ ...current, email: undefined }))
              setFormError(null)
            }}
          />
          <FieldError id="register-email-error">{errors.email}</FieldError>
        </Field>

        <Field data-invalid={Boolean(errors.password)}>
          <FieldLabel htmlFor="register-password">Пароль</FieldLabel>
          <Input
            ref={passwordRef}
            id="register-password"
            name="password"
            type="password"
            autoComplete="new-password"
            value={password}
            disabled={isPending}
            aria-invalid={Boolean(errors.password)}
            aria-describedby={
              errors.password
                ? 'register-password-hint register-password-error'
                : 'register-password-hint'
            }
            onChange={(event) => {
              setPassword(event.target.value)
              setErrors((current) => ({ ...current, password: undefined }))
              setFormError(null)
            }}
          />
          <p
            id="register-password-hint"
            className="text-sm text-muted-foreground"
          >
            Минимум {MIN_PASSWORD_LENGTH} символов. Не должен совпадать с email,
            именем или фамилией.
          </p>
          <FieldError id="register-password-error">{errors.password}</FieldError>
        </Field>

        <Field data-invalid={Boolean(errors.passwordRepeat)}>
          <FieldLabel htmlFor="register-password-repeat">
            Повтор пароля
          </FieldLabel>
          <Input
            ref={passwordRepeatRef}
            id="register-password-repeat"
            name="passwordRepeat"
            type="password"
            autoComplete="new-password"
            value={passwordRepeat}
            disabled={isPending}
            aria-invalid={Boolean(errors.passwordRepeat)}
            aria-describedby={
              errors.passwordRepeat ? 'register-password-repeat-error' : undefined
            }
            onChange={(event) => {
              setPasswordRepeat(event.target.value)
              setErrors((current) => ({
                ...current,
                passwordRepeat: undefined,
              }))
              setFormError(null)
            }}
          />
          <FieldError id="register-password-repeat-error">
            {errors.passwordRepeat}
          </FieldError>
        </Field>
      </FieldGroup>

      {formError && (
        <p role="alert" className="text-sm text-destructive">
          {formError}
        </p>
      )}

      <Button
        type="submit"
        className="h-11 w-full sm:h-8"
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
        {isPending ? 'Создаём аккаунт…' : 'Зарегистрироваться'}
      </Button>
    </form>
  )
}

/**
 * Аккаунт создан, но авто-вход не удался.
 *
 * Это не ошибка формы: повторная регистрация получила бы `409`, поэтому вместо формы —
 * карточка успеха и обычный диалог входа.
 */
function RegistrationCreated() {
  return (
    <div role="status" className="flex flex-col gap-4">
      <div className="space-y-1">
        <p className="font-heading text-lg font-medium">Аккаунт создан</p>
        <p className="text-sm text-muted-foreground">
          Автоматический вход не удался, но аккаунт уже существует — повторно
          регистрироваться не нужно. Войдите с тем же email и паролем.
        </p>
      </div>
      <LoginDialog
        trigger="Войти"
        variant="default"
        size="lg"
        className="h-11 w-full sm:h-8"
        redirectTo="/account"
      />
    </div>
  )
}
