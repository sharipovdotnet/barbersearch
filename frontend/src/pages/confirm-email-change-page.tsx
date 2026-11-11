import { useEffect, useRef, useState, type ReactNode, type RefObject } from 'react'
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
import { useSession } from '@/hooks/use-session'
import { t, useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  confirmEmailChange,
  describeEmailChangeFailure,
} from '@/lib/email-change'
import { CONFIRM_EMAIL_CHANGE_PATH } from '@/lib/email-change-path'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Страница подтверждения нового адреса электронной почты.
 *
 * Токен читается из `window.location.search` **один раз** в состоянии: после
 * результата он вычищается из адреса (`replace`), чтобы не оставаться в истории
 * и логах прокси. Запрос к серверу ровно один на монтирование — StrictMode дважды
 * запускает эффект, поэтому промис кэшируется в ref, а не отправляется повторно.
 *
 * Подтверждение завершает **все** сессии пользователя (ротация security stamp и
 * отзыв refresh-токенов), поэтому после успеха локальная сессия тоже завершается
 * через `signOut()`. Ошибка выхода не важна: сервер уже отозвал сессию, а экран
 * всё равно предлагает войти заново.
 */

type ConfirmState = 'missing' | 'checking' | 'changed' | 'invalid' | 'error'

type ConfirmOutcome = {
  state: Exclude<ConfirmState, 'missing' | 'checking'>
  /** Текст причины; у успеха его нет. */
  message: string | null
}

const CONFIRM_FALLBACK = 'Не удалось подтвердить новый адрес. Попробуйте ещё раз.'

export function ConfirmEmailChangePage() {
  const { locale, t } = useI18n()
  const { status, signOut } = useSession()

  // Один раз на монтирование: живой `useLocation` после очистки адреса вернул бы
  // пустой токен и перезапустил проверку.
  const [token] = useState(readConfirmToken)
  const [state, setState] = useState<ConfirmState>(() =>
    token ? 'checking' : 'missing'
  )
  const [message, setMessage] = useState<string | null>(null)

  // Общий запрос для StrictMode: второй запуск эффекта ждёт тот же промис.
  const confirmationRef = useRef<Promise<ConfirmOutcome> | null>(null)
  const hasSignedOutRef = useRef(false)
  const headingRef = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    document.title = t('Смена email — BarberSearch')
  }, [locale, t])

  useEffect(() => {
    if (!token) {
      return
    }

    let isActive = true
    const pending = confirmationRef.current ?? confirmToken(token)
    confirmationRef.current = pending

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
    if (!token || state === 'checking') {
      return
    }

    navigate(CONFIRM_EMAIL_CHANGE_PATH, { replace: true })
  }, [state, token])

  // Заголовок результата получает фокус: экран сменился, и пользователю
  // скринридера нужно услышать итог, а не потерять место на странице.
  useEffect(() => {
    if (state === 'changed' || state === 'invalid' || state === 'error') {
      headingRef.current?.focus()
    }
  }, [state])

  // Все сессии уже завершены на сервере. Локальную тоже гасим, но только если
  // она была: у анонимного посетителя выходить не из чего.
  useEffect(() => {
    if (
      state !== 'changed' ||
      status !== 'authenticated' ||
      hasSignedOutRef.current
    ) {
      return
    }

    hasSignedOutRef.current = true
    void signOut().catch(() => {})
  }, [signOut, state, status])

  return (
    <div className="mx-auto w-full max-w-md px-4 py-8 sm:py-10">
      {state === 'missing' ? (
        <MissingTokenCard />
      ) : state === 'checking' ? (
        <CheckingCard />
      ) : state === 'changed' ? (
        <ChangedCard headingRef={headingRef} />
      ) : (
        <FailureCard
          state={state}
          message={message}
          headingRef={headingRef}
        />
      )}
    </div>
  )
}

/** Токен из query; пустое или пробельное значение — отсутствие токена. */
function readConfirmToken(): string | null {
  const value = new URLSearchParams(window.location.search).get('token')?.trim()

  return value ? value : null
}

async function confirmToken(token: string): Promise<ConfirmOutcome> {
  try {
    await confirmEmailChange(token)

    return { state: 'changed', message: null }
  } catch (error) {
    const failure = describeEmailChangeFailure(error, CONFIRM_FALLBACK)

    // 400 у confirm — всегда «токен недействителен»: других поводов нет.
    if (
      failure.kind === 'invalid' ||
      (error instanceof ApiError && error.status === 400)
    ) {
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

function ChangedCard({
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
            {t('Email изменён')}
          </h1>
          <CardDescription>
            {t(
              'Новый адрес подтверждён. Все сессии входа завершены — войдите с новым email и прежним паролем.'
            )}
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
            'В ссылке нет токена для смены email. Откройте письмо и перейдите по ссылке целиком.'
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
  headingRef,
}: {
  state: 'invalid' | 'error'
  message: string | null
  headingRef: RefObject<HTMLHeadingElement | null>
}) {
  const { t } = useI18n()
  const isInvalid = state === 'invalid'

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
          <TriangleAlert className="size-5 text-destructive" aria-hidden />
        </div>
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="font-heading text-2xl font-semibold tracking-tight outline-none"
        >
          {isInvalid
            ? t('Ссылка недействительна')
            : t('Не удалось проверить ссылку')}
        </h1>
        <CardDescription>{message}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {isInvalid && (
          <p className="text-sm text-muted-foreground">
            {t(
              'Ссылка для смены email устарела или уже использована. Запросите новую в профиле.'
            )}
          </p>
        )}
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
