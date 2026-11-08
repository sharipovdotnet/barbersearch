import { useRef, useState, type FormEvent, type RefObject } from 'react'
import { Loader2 } from 'lucide-react'

import { LoginDialog } from '@/components/login-dialog'
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
import { useSession } from '@/hooks/use-session'
import { getLocale, localeTag, t, useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  MAX_PASSWORD_LENGTH,
  MIN_PASSWORD_LENGTH,
  register,
  type RegistrationInput,
} from '@/lib/auth'
import { describeApiFailure } from '@/lib/errors'
import {
  PRIVACY_PATH,
  PRIVACY_POLICY_DATE,
  PRIVACY_POLICY_VERSION,
} from '@/lib/legal'
import { isPlainClick, navigate } from '@/lib/router'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Подпись редакции для отметки согласия: та же дата, что в Политике. */
function policyDateLabel(): string {
  return new Intl.DateTimeFormat(localeTag(getLocale()), {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(new Date(`${PRIVACY_POLICY_DATE}T00:00:00Z`))
}

// Верхняя граница адреса — из серверного валидатора (3..254).
const MAX_EMAIL_LENGTH = 254

// Сообщения клиентских проверок и серверных кодов совпадают: пользователь не должен
// видеть два разных текста об одном и том же правиле.
const FIRST_NAME_ERROR = 'Введите имя.'
const LAST_NAME_ERROR = 'Введите фамилию.'
const EMAIL_ERROR = 'Проверьте email: нужен адрес вида name@example.com.'
const PASSWORD_REQUIRED_ERROR = 'Введите пароль.'
const PASSWORD_SHORT_ERROR = 'Пароль должен быть не короче {count} символов.'
const PASSWORD_LONG_ERROR = 'Пароль должен быть не длиннее {count} символов.'
const PASSWORD_COMMON_ERROR = 'Этот пароль слишком распространён. Выберите другой.'
const PASSWORD_EQUALS_EMAIL_ERROR = 'Пароль не должен совпадать с email.'
const PASSWORD_EQUALS_NAME_ERROR =
  'Пароль не должен совпадать с именем или фамилией.'
const PASSWORD_REPEAT_ERROR = 'Пароли не совпадают.'
const CONSENT_REQUIRED_ERROR =
  'Отметьте согласие на обработку персональных данных.'
const CONSENT_VERSION_ERROR =
  'Политика обработки данных обновилась. Обновите страницу и подтвердите согласие снова.'
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
  'consent',
] as const

/** Поля формы в том же порядке, в котором они идут по Tab. */
type RegisterField = (typeof FIELD_ORDER)[number]
type RegisterErrors = Partial<Record<RegisterField, string>>

/** Поля с текстовым вводом: у отметки согласия другой элемент и другой ref. */
type RegisterInputField = Exclude<RegisterField, 'consent'>

type RegisterValues = {
  firstName: string
  lastName: string
  email: string
  password: string
  passwordRepeat: string
  consent: boolean
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
    errors.firstName = t(FIRST_NAME_ERROR)
  }

  if (!lastName) {
    errors.lastName = t(LAST_NAME_ERROR)
  }

  if (!email) {
    errors.email = t('Введите email.')
  } else if (email.length > MAX_EMAIL_LENGTH || !EMAIL_PATTERN.test(email)) {
    errors.email = t(EMAIL_ERROR)
  }

  if (!password) {
    errors.password = t(PASSWORD_REQUIRED_ERROR)
  } else if (password.length < MIN_PASSWORD_LENGTH) {
    errors.password = t(PASSWORD_SHORT_ERROR, { count: MIN_PASSWORD_LENGTH })
  } else if (password.length > MAX_PASSWORD_LENGTH) {
    errors.password = t(PASSWORD_LONG_ERROR, { count: MAX_PASSWORD_LENGTH })
  } else if (password === email || password === emailLocalPart(email)) {
    errors.password = t(PASSWORD_EQUALS_EMAIL_ERROR)
  } else if (password === firstName || password === lastName) {
    errors.password = t(PASSWORD_EQUALS_NAME_ERROR)
  }

  if (values.passwordRepeat !== password) {
    errors.passwordRepeat = t(PASSWORD_REPEAT_ERROR)
  }

  // Согласие — отдельное основание обработки (ч. 1 ст. 9 152-ФЗ): без отметки
  // регистрация не отправляется, чтобы аккаунт не появился без согласия.
  if (!values.consent) {
    errors.consent = CONSENT_REQUIRED_ERROR
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
    return { fields: {}, form: t(FALLBACK_ERROR) }
  }

  if (error.status === 0) {
    return { fields: {}, form: describeApiFailure(error, FALLBACK_ERROR) }
  }

  if (error.status === 429) {
    return { fields: {}, form: t(RATE_LIMIT_ERROR) }
  }

  if (error.hasCode('user.create.emailTaken')) {
    return { fields: {}, form: t(EMAIL_TAKEN_ERROR) }
  }

  const fields: RegisterErrors = {}
  let hasUnknownCode = false
  let isPolicyOutdated = false

  for (const code of error.codes) {
    const passwordMessage = PASSWORD_CODE_MESSAGES[code]

    if (passwordMessage) {
      fields.password = t(passwordMessage, {
        count:
          code === 'auth.password.tooLong'
            ? MAX_PASSWORD_LENGTH
            : MIN_PASSWORD_LENGTH,
      })
    } else if (code === 'user.email') {
      fields.email = t(EMAIL_ERROR)
    } else if (code === 'user.firstName') {
      fields.firstName = t(FIRST_NAME_ERROR)
    } else if (code === 'user.lastName') {
      fields.lastName = t(LAST_NAME_ERROR)
    } else if (code === 'auth.register.consentRequired') {
      fields.consent = t(CONSENT_REQUIRED_ERROR)
    } else if (code === 'auth.register.consentVersion') {
      // Сервер авторитетен: старая открытая форма подписала другую редакцию Политики.
      isPolicyOutdated = true
    } else {
      hasUnknownCode = true
    }
  }

  const hasFieldErrors = Object.keys(fields).length > 0

  return {
    fields,
    form: isPolicyOutdated
      ? t(CONSENT_VERSION_ERROR)
      : hasUnknownCode || !hasFieldErrors
        ? t(FALLBACK_ERROR)
        : null,
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
  const { t } = useI18n()
  const { signIn } = useSession()

  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [passwordRepeat, setPasswordRepeat] = useState('')
  const [isConsentGiven, setIsConsentGiven] = useState(false)
  const [errors, setErrors] = useState<RegisterErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)
  const [isCreated, setIsCreated] = useState(false)

  const firstNameRef = useRef<HTMLInputElement>(null)
  const lastNameRef = useRef<HTMLInputElement>(null)
  const emailRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)
  const passwordRepeatRef = useRef<HTMLInputElement>(null)
  const consentRef = useRef<HTMLButtonElement>(null)

  const fieldRefs: Record<
    RegisterInputField,
    RefObject<HTMLInputElement | null>
  > = {
    firstName: firstNameRef,
    lastName: lastNameRef,
    email: emailRef,
    password: passwordRef,
    passwordRepeat: passwordRepeatRef,
  }

  function focusFirstError(nextErrors: RegisterErrors) {
    for (const field of FIELD_ORDER) {
      if (!nextErrors[field]) {
        continue
      }

      if (field === 'consent') {
        consentRef.current?.focus()
        return
      }

      fieldRefs[field].current?.focus()
      return
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
      consent: isConsentGiven,
    }
    const trimmed: RegistrationInput = {
      email: email.trim(),
      password,
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      consentVersion: PRIVACY_POLICY_VERSION,
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
            <FieldLabel htmlFor="register-first-name">{t('Имя')}</FieldLabel>
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
            <FieldLabel htmlFor="register-last-name">{t('Фамилия')}</FieldLabel>
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
          <FieldLabel htmlFor="register-password">{t('Пароль')}</FieldLabel>
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
            {t(
              'Минимум {count} символов. Не должен совпадать с email, именем или фамилией.',
              { count: MIN_PASSWORD_LENGTH }
            )}
          </p>
          <FieldError id="register-password-error">{errors.password}</FieldError>
        </Field>

        <Field data-invalid={Boolean(errors.passwordRepeat)}>
          <FieldLabel htmlFor="register-password-repeat">
            {t('Повтор пароля')}
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

        <Field orientation="horizontal" data-invalid={Boolean(errors.consent)}>
          <Checkbox
            ref={consentRef}
            id="register-consent"
            name="consent"
            checked={isConsentGiven}
            disabled={isPending}
            aria-invalid={Boolean(errors.consent)}
            aria-describedby={
              errors.consent
                ? 'register-consent-hint register-consent-error'
                : 'register-consent-hint'
            }
            onCheckedChange={(checked) => {
              setIsConsentGiven(checked)
              setErrors((current) => ({ ...current, consent: undefined }))
              setFormError(null)
            }}
          />
          <FieldContent>
            <FieldLabel htmlFor="register-consent" className="font-normal">
              {t('Я согласен на обработку персональных данных')}
            </FieldLabel>
            <FieldDescription id="register-consent-hint">
              {t(
                'Для регистрации и использования аккаунта, оформления и сопровождения записей. Условия — в'
              )}{' '}
              <a
                href={PRIVACY_PATH}
                onClick={(event) => {
                  // Ctrl/Cmd-клик не перехватываем: Политика открывается в новой вкладке.
                  if (!isPlainClick(event)) {
                    return
                  }

                  event.preventDefault()
                  navigate(PRIVACY_PATH)
                }}
              >
                {t('Политике обработки персональных данных')}
              </a>{' '}
              {t('(редакция от {date}).', { date: policyDateLabel() })}
            </FieldDescription>
            <FieldError id="register-consent-error">{errors.consent}</FieldError>
          </FieldContent>
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
        {isPending ? t('Создаём аккаунт…') : t('Зарегистрироваться')}
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
  const { t } = useI18n()

  return (
    <div role="status" className="flex flex-col gap-4">
      <div className="space-y-1">
        <p className="font-heading text-lg font-medium">{t('Аккаунт создан')}</p>
        <p className="text-sm text-muted-foreground">
          {t(
            'Автоматический вход не удался, но аккаунт уже существует — повторно регистрироваться не нужно. Войдите с тем же email и паролем.'
          )}
        </p>
      </div>
      <LoginDialog
        trigger={t('Войти')}
        variant="default"
        size="lg"
        className="h-11 w-full sm:h-8"
        redirectTo="/account"
      />
    </div>
  )
}
