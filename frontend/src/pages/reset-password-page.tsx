import {
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type ReactNode,
  type RefObject,
} from 'react'
import {
  CircleCheck,
  Loader2,
  MailWarning,
  TriangleAlert,
} from 'lucide-react'

import { LoginDialog } from '@/components/login-dialog'
import { Button, buttonVariants } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
} from '@/components/ui/card'
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useSession } from '@/hooks/use-session'
import { t, useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import { MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH } from '@/lib/auth'
import {
  confirmPasswordReset,
  describePasswordResetFailure,
} from '@/lib/password-reset'
import {
  FORGOT_PASSWORD_PATH,
  RESET_PASSWORD_PATH,
} from '@/lib/password-reset-path'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Установка нового пароля по ссылке из письма.
 *
 * Токен читается из `window.location.search` **один раз** в состоянии, после чего
 * адрес сразу очищается через `replace`: так токен не остаётся в истории, `Referer`
 * и адресной строке. Компромисс осознанный — перезагрузка страницы теряет токен,
 * придётся открыть письмо снова (§5.1).
 *
 * Страница не делает preflight-проверку токена: форма показывается для любого
 * непустого значения, а недействительность выясняется на submit. Повторный confirm
 * тем же токеном — `400 auth.passwordReset.invalid`, и форма заменяется карточкой.
 */

const FALLBACK_ERROR = 'Не удалось сохранить пароль. Попробуйте позже.'

type ResetState = 'missing' | 'form' | 'success' | 'invalid'

type PasswordField = 'password' | 'confirm'
type PasswordErrors = Partial<Record<PasswordField, string>>

export function ResetPasswordPage() {
  const { locale, t } = useI18n()
  const { status, signOut } = useSession()

  // Один раз на монтирование: живой `useLocation` после очистки адреса вернул бы
  // пустой токен и показал «Ссылка неполная».
  const [token] = useState(readResetToken)
  const [state, setState] = useState<ResetState>(() =>
    token ? 'form' : 'missing'
  )
  const [values, setValues] = useState<Record<PasswordField, string>>({
    password: '',
    confirm: '',
  })
  const [errors, setErrors] = useState<PasswordErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  const passwordRef = useRef<HTMLInputElement>(null)
  const confirmRef = useRef<HTMLInputElement>(null)
  const headingRef = useRef<HTMLHeadingElement>(null)
  // Синхронный замок: disabled на кнопке обновляется только со следующим рендером,
  // а два submit в одном тике не должны отправить два POST.
  const inFlightRef = useRef(false)

  useEffect(() => {
    document.title = t('Новый пароль — BarberSearch')
  }, [locale, t])

  // Токен не должен оставаться в адресной строке ни на форме, ни после ошибки.
  useEffect(() => {
    if (token) {
      navigate(RESET_PASSWORD_PATH, { replace: true })
    }
  }, [token])

  useEffect(() => {
    if (state === 'success' || state === 'invalid') {
      headingRef.current?.focus()
    }
  }, [state])

  // Старые cookie после сброса всё равно мертвы, но локальная сессия не должна
  // оставаться «вошедшей». Ошибка выхода не важна: сервер уже отозвал refresh.
  useEffect(() => {
    if (state !== 'success' || status !== 'authenticated') {
      return
    }

    void signOut().catch(() => {})
  }, [signOut, state, status])

  function update(field: PasswordField, value: string) {
    setValues((current) => ({ ...current, [field]: value }))
    setErrors((current) => ({ ...current, [field]: undefined }))
    setFormError(null)
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (inFlightRef.current) {
      return
    }

    const nextErrors = validate(values)

    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      focusFirstError(nextErrors, passwordRef, confirmRef)
      return
    }

    if (!token) {
      setState('missing')
      return
    }

    inFlightRef.current = true
    setIsPending(true)

    try {
      await confirmPasswordReset(token, values.password)
      setState('success')
    } catch (error) {
      if (error instanceof ApiError) {
        if (error.hasCode('auth.passwordReset.invalid')) {
          setState('invalid')
          return
        }

        const policy = passwordPolicyMessage(error)
        if (policy) {
          setErrors({ password: policy })
          passwordRef.current?.focus()
          return
        }
      }

      setFormError(describePasswordResetFailure(error, FALLBACK_ERROR).message)
    } finally {
      inFlightRef.current = false
      setIsPending(false)
    }
  }

  return (
    <div className="mx-auto w-full max-w-md px-4 py-8 sm:py-10">
      {state === 'missing' ? (
        <MissingTokenCard headingRef={headingRef} />
      ) : state === 'invalid' ? (
        <InvalidLinkCard headingRef={headingRef} />
      ) : state === 'success' ? (
        <PasswordChangedCard headingRef={headingRef} />
      ) : (
        <form
          noValidate
          aria-busy={isPending}
          className="flex flex-col gap-4"
          onSubmit={handleSubmit}
        >
          <Card>
            <CardHeader>
              <div className="flex size-10 items-center justify-center rounded-full bg-muted">
                <MailWarning
                  className="size-5 text-muted-foreground"
                  aria-hidden
                />
              </div>
              <h1 className="font-heading text-2xl font-semibold tracking-tight">
                {t('Новый пароль')}
              </h1>
              <CardDescription>
                {t('После сохранения все сессии входа будут завершены.')}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <FieldGroup>
                <Field data-invalid={Boolean(errors.password)}>
                  <FieldLabel htmlFor="reset-password-new">
                    {t('Новый пароль')}
                  </FieldLabel>
                  <Input
                    ref={passwordRef}
                    id="reset-password-new"
                    name="newPassword"
                    type="password"
                    autoComplete="new-password"
                    value={values.password}
                    disabled={isPending}
                    aria-invalid={Boolean(errors.password)}
                    aria-describedby={
                      errors.password
                        ? 'reset-password-hint reset-password-new-error'
                        : 'reset-password-hint'
                    }
                    onChange={(event) => update('password', event.target.value)}
                  />
                  <p
                    id="reset-password-hint"
                    className="text-sm text-muted-foreground"
                  >
                    {t(
                      'Минимум {count} символов. Не должен совпадать с email, именем или фамилией.',
                      { count: MIN_PASSWORD_LENGTH }
                    )}
                  </p>
                  <FieldError id="reset-password-new-error">
                    {errors.password}
                  </FieldError>
                </Field>

                <Field data-invalid={Boolean(errors.confirm)}>
                  <FieldLabel htmlFor="reset-password-confirm">
                    {t('Повторите пароль')}
                  </FieldLabel>
                  <Input
                    ref={confirmRef}
                    id="reset-password-confirm"
                    name="confirmPassword"
                    type="password"
                    autoComplete="new-password"
                    value={values.confirm}
                    disabled={isPending}
                    aria-invalid={Boolean(errors.confirm)}
                    aria-describedby={
                      errors.confirm
                        ? 'reset-password-confirm-error'
                        : undefined
                    }
                    onChange={(event) => update('confirm', event.target.value)}
                  />
                  <FieldError id="reset-password-confirm-error">
                    {errors.confirm}
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
                {isPending
                  ? t('Сохраняем…')
                  : t('Сохранить новый пароль')}
              </Button>
            </CardContent>
          </Card>
        </form>
      )}
    </div>
  )
}

/** Токен из query; пустое или пробельное значение — отсутствие токена. */
function readResetToken(): string | null {
  const value = new URLSearchParams(window.location.search).get('token')?.trim()

  return value ? value : null
}

function validate(values: Record<PasswordField, string>): PasswordErrors {
  const errors: PasswordErrors = {}
  const { password, confirm } = values

  if (!password) {
    errors.password = t('Введите новый пароль.')
  } else if (password.length < MIN_PASSWORD_LENGTH) {
    errors.password = t('Минимум {count} символов.', {
      count: MIN_PASSWORD_LENGTH,
    })
  } else if (password.length > MAX_PASSWORD_LENGTH) {
    errors.password = t('Максимум {count} символов.', {
      count: MAX_PASSWORD_LENGTH,
    })
  }

  if (confirm !== password) {
    errors.confirm = t('Пароли не совпадают.')
  }

  return errors
}

function focusFirstError(
  errors: PasswordErrors,
  passwordRef: RefObject<HTMLInputElement | null>,
  confirmRef: RefObject<HTMLInputElement | null>
) {
  if (errors.password) {
    passwordRef.current?.focus()
    return
  }

  if (errors.confirm) {
    confirmRef.current?.focus()
  }
}

/**
 * Публичные коды политики пароля → текст поля.
 *
 * Тексты совпадают с регистрацией и сменой пароля: одно правило не должно звучать
 * по-разному на разных экранах. Коды относятся к полю, а не к ссылке, поэтому не
 * входят в общий `describePasswordResetFailure`.
 */
function passwordPolicyMessage(error: ApiError): string | null {
  for (const code of error.codes) {
    switch (code) {
      case 'auth.password.required':
        return t('Введите новый пароль.')
      case 'auth.password.tooShort':
        return t('Минимум {count} символов.', {
          count: MIN_PASSWORD_LENGTH,
        })
      case 'auth.password.tooLong':
        return t('Максимум {count} символов.', {
          count: MAX_PASSWORD_LENGTH,
        })
      case 'auth.password.common':
        return t('Этот пароль слишком распространён.')
      case 'auth.password.equalsEmail':
        return t('Пароль не должен совпадать с email.')
      case 'auth.password.equalsName':
        return t('Пароль не должен совпадать с именем или фамилией.')
      default:
        break
    }
  }

  return null
}

function MissingTokenCard({
  headingRef,
}: {
  headingRef: RefObject<HTMLHeadingElement | null>
}) {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-warning/10">
          <MailWarning className="size-5 text-warning" aria-hidden />
        </div>
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="font-heading text-2xl font-semibold tracking-tight outline-none"
        >
          {t('Ссылка неполная')}
        </h1>
        <CardDescription>
          {t(
            'В ссылке нет токена для сброса пароля. Откройте письмо и перейдите по ссылке целиком.'
          )}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        <ActionLink href={FORGOT_PASSWORD_PATH} primary>
          {t('Запросить новую ссылку')}
        </ActionLink>
        <ActionLink href="/">{t('На главную')}</ActionLink>
      </CardContent>
    </Card>
  )
}

function InvalidLinkCard({
  headingRef,
}: {
  headingRef: RefObject<HTMLHeadingElement | null>
}) {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
          <TriangleAlert className="size-5 text-destructive" aria-hidden />
        </div>
        <div role="status" aria-live="polite" className="space-y-2">
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="font-heading text-2xl font-semibold tracking-tight outline-none"
          >
            {t('Ссылка недействительна')}
          </h1>
          <CardDescription>
            {t(
              'Ссылка для сброса пароля устарела или уже использована. Запросите новую.'
            )}
          </CardDescription>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        <ActionLink href={FORGOT_PASSWORD_PATH} primary>
          {t('Запросить новую ссылку')}
        </ActionLink>
        <ActionLink href="/">{t('На главную')}</ActionLink>
      </CardContent>
    </Card>
  )
}

function PasswordChangedCard({
  headingRef,
}: {
  headingRef: RefObject<HTMLHeadingElement | null>
}) {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-success/10">
          <CircleCheck className="size-5 text-success" aria-hidden />
        </div>
        <div role="status" aria-live="polite" className="space-y-2">
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="font-heading text-2xl font-semibold tracking-tight outline-none"
          >
            {t('Пароль изменён')}
          </h1>
          <CardDescription>
            {t('Войдите с новым паролем. Все сессии входа завершены.')}
          </CardDescription>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {/* `redirectTo` не задан: роль сама выберет раздел после входа. */}
        <LoginDialog
          trigger={t('Войти')}
          variant="default"
          size="lg"
          className="h-11 w-full sm:h-8"
        />
        <ActionLink href="/">{t('На главную')}</ActionLink>
      </CardContent>
    </Card>
  )
}

/** Ссылка-кнопка: остаётся настоящим `<a href>`, поэтому средний клик работает. */
function ActionLink({
  href,
  primary = false,
  children,
}: {
  href: string
  primary?: boolean
  children: ReactNode
}) {
  return (
    <a
      href={href}
      className={cn(
        buttonVariants({ variant: primary ? 'default' : 'outline' }),
        'h-11 w-full sm:h-8'
      )}
      onClick={(event) => {
        if (!isPlainClick(event)) {
          return
        }

        event.preventDefault()
        navigate(href)
      }}
    >
      {children}
    </a>
  )
}
