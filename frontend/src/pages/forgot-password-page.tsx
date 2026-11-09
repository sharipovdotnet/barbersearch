import {
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type ReactNode,
} from 'react'
import { CircleCheck, Loader2, MailWarning } from 'lucide-react'

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
import { t, useI18n } from '@/i18n'
import {
  describePasswordResetFailure,
  requestPasswordReset,
} from '@/lib/password-reset'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Запрос ссылки для сброса забытого пароля.
 *
 * Ответ `204` одинаков для существующего и несуществующего адреса, поэтому экран
 * никогда не различает эти случаи: текст всегда «Если аккаунт с адресом … существует».
 * Это главное ограничение среза, и UI его не обходит.
 *
 * Кулдаун повторной отправки (`60` секунд) локальный: сервер намеренно отвечает `204`
 * и на слишком частый запрос, чтобы не выдать существование аккаунта временем или
 * кодом. `409 unavailable` — глобальная ошибка конфигурации почты, её можно показать.
 */

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

// Верхняя граница адреса — из серверного валидатора (3..254).
const MAX_EMAIL_LENGTH = 254

const RESEND_COOLDOWN_SECONDS = 60

const EMAIL_ERROR = 'Проверьте email: нужен адрес вида name@example.com.'
const FALLBACK_ERROR = 'Не удалось отправить ссылку. Попробуйте позже.'

type ForgotState = 'form' | 'sent'

export function ForgotPasswordPage() {
  const { locale, t } = useI18n()

  const [state, setState] = useState<ForgotState>('form')
  const [email, setEmail] = useState('')
  const [sentEmail, setSentEmail] = useState('')
  const [emailError, setEmailError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)
  const [cooldownEndsAt, setCooldownEndsAt] = useState<number | null>(null)
  const [cooldownSeconds, setCooldownSeconds] = useState(0)

  const emailRef = useRef<HTMLInputElement>(null)
  const headingRef = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    document.title = t('Восстановление пароля — BarberSearch')
  }, [locale, t])

  // Фокус на заголовке после смены состояния: форма исчезает, и пользователь
  // скринридера должен услышать результат, а не потерять место.
  useEffect(() => {
    if (state === 'sent') {
      headingRef.current?.focus()
    }
  }, [state])

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

  /** Один общий путь формы и повторной отправки: отличается только адрес. */
  async function send(targetEmail: string): Promise<boolean> {
    setIsPending(true)
    setFormError(null)

    try {
      await requestPasswordReset(targetEmail)

      return true
    } catch (error) {
      const failure = describePasswordResetFailure(error, FALLBACK_ERROR)

      setFormError(failure.message)

      return false
    } finally {
      setIsPending(false)
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (isPending) {
      return
    }

    const normalizedEmail = email.trim()
    const validationError = validateEmail(normalizedEmail)

    setEmailError(validationError)
    setFormError(null)

    if (validationError) {
      emailRef.current?.focus()
      return
    }

    const isSent = await send(normalizedEmail)

    if (!isSent) {
      return
    }

    setSentEmail(normalizedEmail)
    setState('sent')
    startCooldown()
  }

  async function handleResend() {
    if (isPending || cooldownSeconds > 0) {
      return
    }

    const isSent = await send(sentEmail)

    if (isSent) {
      startCooldown()
    }
  }

  if (state === 'sent') {
    return (
      <div className="mx-auto w-full max-w-md px-4 py-8 sm:py-10">
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
                {t('Проверьте почту')}
              </h1>
              <CardDescription>
                {t(
                  'Если аккаунт с адресом {email} существует, мы отправили на него ссылку для сброса пароля. Ссылка действует 30 минут.',
                  { email: sentEmail }
                )}
              </CardDescription>
              <CardDescription>
                {t(
                  'Письма нет? Проверьте папку «Спам» или запросите ссылку ещё раз.'
                )}
              </CardDescription>
            </div>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            <Button
              variant="outline"
              className="h-11 w-full sm:h-8"
              disabled={isPending || cooldownSeconds > 0}
              aria-busy={isPending}
              onClick={() => void handleResend()}
            >
              {isPending && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {isPending
                ? t('Отправляем…')
                : cooldownSeconds > 0
                  ? t('Повторить через {count} с', { count: cooldownSeconds })
                  : t('Отправить ещё раз')}
            </Button>

            <Button
              variant="ghost"
              className="h-11 w-full sm:h-8"
              disabled={isPending}
              onClick={() => {
                setState('form')
                setFormError(null)
              }}
            >
              {t('Указать другой адрес')}
            </Button>

            {formError && (
              <p role="alert" className="text-sm text-destructive">
                {formError}
              </p>
            )}

            <ActionLink href="/">{t('На главную')}</ActionLink>
          </CardContent>
        </Card>
      </div>
    )
  }

  return (
    <div className="mx-auto w-full max-w-md px-4 py-8 sm:py-10">
      <Card>
        <CardHeader>
          <div className="flex size-10 items-center justify-center rounded-full bg-warning/10">
            <MailWarning className="size-5 text-warning" aria-hidden />
          </div>
          <h1 className="font-heading text-2xl font-semibold tracking-tight">
            {t('Восстановление пароля')}
          </h1>
          <CardDescription>
            {t(
              'Укажите email аккаунта — мы отправим ссылку для сброса пароля.'
            )}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <form
            noValidate
            aria-busy={isPending}
            className="flex flex-col gap-4"
            onSubmit={handleSubmit}
          >
            <FieldGroup>
              <Field data-invalid={Boolean(emailError)}>
                <FieldLabel htmlFor="forgot-password-email">Email</FieldLabel>
                <Input
                  ref={emailRef}
                  id="forgot-password-email"
                  name="email"
                  type="email"
                  autoComplete="email"
                  inputMode="email"
                  spellCheck={false}
                  placeholder="you@example.com"
                  value={email}
                  disabled={isPending}
                  aria-invalid={Boolean(emailError)}
                  aria-describedby={
                    emailError ? 'forgot-password-email-error' : undefined
                  }
                  onChange={(event) => {
                    setEmail(event.target.value)
                    setEmailError(null)
                    setFormError(null)
                  }}
                />
                <FieldError id="forgot-password-email-error">
                  {emailError}
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
              {isPending ? t('Отправляем…') : t('Отправить ссылку')}
            </Button>
          </form>

          <ActionLink href="/">{t('На главную')}</ActionLink>
        </CardContent>
      </Card>
    </div>
  )
}

function validateEmail(email: string): string | null {
  if (!email) {
    return t('Введите email.')
  }

  if (email.length > MAX_EMAIL_LENGTH || !EMAIL_PATTERN.test(email)) {
    return t(EMAIL_ERROR)
  }

  return null
}

/** Ссылка-кнопка: остаётся настоящим `<a href>`, поэтому средний клик работает. */
function ActionLink({ href, children }: { href: string; children: ReactNode }) {
  return (
    <a
      href={href}
      className={cn(
        buttonVariants({ variant: 'ghost' }),
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
