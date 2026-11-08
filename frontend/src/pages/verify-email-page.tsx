import { useEffect, useRef, useState, type ReactNode } from 'react'
import { CircleCheck, MailWarning, TriangleAlert } from 'lucide-react'

import { LoginDialog } from '@/components/login-dialog'
import { buttonVariants } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useSession, type SessionStatus } from '@/hooks/use-session'
import { t, useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  confirmEmailVerification,
  describeEmailVerificationFailure,
} from '@/lib/email-verification'
import { isPlainClick, navigate } from '@/lib/router'
import { refreshSession } from '@/lib/session-refresh'
import { VERIFY_EMAIL_PATH } from '@/lib/verify-email-path'
import { cn } from '@/lib/utils'

/**
 * Страница подтверждения адреса из письма.
 *
 * Токен читается из `window.location.search` **один раз** в состоянии: после успеха
 * или ошибки он вычищается из адреса (`replace`), чтобы не остаться в истории и
 * логах прокси. Запрос к серверу ровно один на монтирование — StrictMode дважды
 * запускает эффект, поэтому запрос кэшируется в ref, а не отправляется повторно.
 *
 * Если посетитель вошёл, успех подтверждения обновляет cookie-сессию: клейм
 * `email_verified` в уже выданной cookie не переписывается сам, поэтому после
 * success идут `refreshSession()` и `retrySession()`. Их терминальный или
 * временный сбой не превращает успешное подтверждение в ошибку.
 */

type VerifyState = 'checking' | 'confirmed' | 'invalid' | 'error'

type VerifyOutcome = {
  state: Exclude<VerifyState, 'checking'>
  /** Текст причины; у успеха его нет. */
  message: string | null
}

const CONFIRM_FALLBACK = 'Не удалось проверить ссылку. Попробуйте ещё раз.'

export function VerifyEmailPage() {
  const { locale, t } = useI18n()
  const { status, retrySession } = useSession()

  // Один раз на монтирование: живой `useLocation` после очистки адреса вернул бы
  // пустой токен и перезапустил проверку.
  const [token] = useState(readVerificationToken)
  const [state, setState] = useState<VerifyState>('checking')
  const [message, setMessage] = useState<string | null>(null)

  // Общий запрос для StrictMode: второй запуск эффекта ждёт тот же промис.
  const verificationRef = useRef<Promise<VerifyOutcome> | null>(null)
  // Обновление клейма — тоже один раз, даже если эффект перезапустится.
  const hasRefreshedRef = useRef(false)

  useEffect(() => {
    document.title = t('Подтверждение email — BarberSearch')
  }, [locale, t])

  useEffect(() => {
    if (!token) {
      return
    }

    let isActive = true
    const pending = verificationRef.current ?? verifyToken(token)
    verificationRef.current = pending

    void pending.then((outcome) => {
      if (!isActive) {
        return
      }

      setMessage(outcome.message)
      setState(outcome.state)
    })

    return () => {
      isActive = false
    }
  }, [token])

  // Токен не должен оставаться в адресной строке после того, как он отработал.
  useEffect(() => {
    if (state === 'checking') {
      return
    }

    navigate(VERIFY_EMAIL_PATH, { replace: true })
  }, [state])

  useEffect(() => {
    if (
      state !== 'confirmed' ||
      status !== 'authenticated' ||
      hasRefreshedRef.current
    ) {
      return
    }

    hasRefreshedRef.current = true
    void (async () => {
      // Исход не важен: успех подтверждения уже показан, а сессия либо обновится,
      // либо экран `unavailable`/`anonymous` покажет своё состояние.
      await refreshSession()
      retrySession()
    })()
  }, [retrySession, state, status])

  return (
    <div className="mx-auto w-full max-w-md px-4 py-8 sm:py-10">
      {!token ? (
        <MissingTokenCard />
      ) : state === 'checking' ? (
        <CheckingCard />
      ) : state === 'confirmed' ? (
        <ConfirmedCard status={status} />
      ) : (
        <FailureCard state={state} message={message} status={status} />
      )}
    </div>
  )
}

/** Токен из query; пустое значение — это отсутствие токена, а не пустой запрос. */
function readVerificationToken(): string | null {
  const value = new URLSearchParams(window.location.search).get('token')?.trim()

  return value ? value : null
}

async function verifyToken(token: string): Promise<VerifyOutcome> {
  try {
    await confirmEmailVerification(token)

    return { state: 'confirmed', message: null }
  } catch (error) {
    const failure = describeEmailVerificationFailure(error, CONFIRM_FALLBACK)

    // 400 у confirm — всегда «токен недействителен»: других поводов для него нет.
    if (failure.kind === 'invalid' || (error instanceof ApiError && error.status === 400)) {
      return {
        state: 'invalid',
        message:
          failure.kind === 'invalid'
            ? failure.message
            : t('Ссылка недействительна или устарела.'),
      }
    }

    return { state: 'error', message: failure.message }
  }
}

function CheckingCard() {
  const { t } = useI18n()

  return (
    <div role="status" aria-live="polite">
      <span className="sr-only">{t('Проверяем ссылку…')}</span>
      <Card className="gap-6" aria-hidden>
        <CardHeader>
          <Skeleton className="h-8 w-52" />
          <Skeleton className="h-4 w-full" />
        </CardHeader>
        <CardContent className="space-y-3">
          <Skeleton className="h-4 w-3/4" />
          <Skeleton className="h-11 w-full" />
        </CardContent>
      </Card>
    </div>
  )
}

function ConfirmedCard({ status }: { status: SessionStatus }) {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-success/10">
          <CircleCheck className="size-5 text-success" aria-hidden />
        </div>
        <h1 className="font-heading text-2xl font-semibold tracking-tight">
          {t('Адрес подтверждён')}
        </h1>
        <CardDescription>
          {t(
            'Email подтверждён. Теперь на него можно получать письма о записях.'
          )}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {status === 'authenticated' ? (
          <ActionLink href="/account" primary>
            {t('Перейти в кабинет')}
          </ActionLink>
        ) : status === 'anonymous' ? (
          <LoginDialog
            trigger={t('Войти')}
            variant="default"
            size="lg"
            className="h-11 w-full"
            redirectTo="/account"
          />
        ) : null}
        <ActionLink href="/">{t('На главную')}</ActionLink>
      </CardContent>
    </Card>
  )
}

/** Неполная ссылка: токена нет вовсе, сервер не нужен. */
function MissingTokenCard() {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-warning/10">
          <MailWarning className="size-5 text-warning" aria-hidden />
        </div>
        <h1 className="font-heading text-2xl font-semibold tracking-tight">
          {t('Ссылка неполная')}
        </h1>
        <CardDescription>
          {t(
            'В ссылке нет токена подтверждения. Откройте письмо и перейдите по ссылке целиком.'
          )}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <ActionLink href="/">{t('На главную')}</ActionLink>
      </CardContent>
    </Card>
  )
}

/** Недействительная ссылка или сетевой сбой: без выдуманного успеха. */
function FailureCard({
  state,
  message,
  status,
}: {
  state: 'invalid' | 'error'
  message: string | null
  status: SessionStatus
}) {
  const { t } = useI18n()
  const isInvalid = state === 'invalid'

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
          <TriangleAlert className="size-5 text-destructive" aria-hidden />
        </div>
        <h1 className="font-heading text-2xl font-semibold tracking-tight">
          {isInvalid
            ? t('Не удалось подтвердить адрес')
            : t('Не удалось проверить ссылку')}
        </h1>
        <CardDescription>{message}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {isInvalid && (
          <p className="text-sm text-muted-foreground">
            {t(
              'Попробуйте ещё раз из письма или запросите новое в разделе «Безопасность».'
            )}
          </p>
        )}

        {status === 'authenticated' ? (
          <ActionLink href="/account" primary>
            {t('Перейти в кабинет')}
          </ActionLink>
        ) : status === 'anonymous' ? (
          <LoginDialog
            trigger={t('Войти')}
            variant="default"
            size="lg"
            className="h-11 w-full"
            redirectTo="/account"
          />
        ) : null}
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
