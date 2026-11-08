import { useEffect, useState } from 'react'
import {
  CheckCircle2,
  KeyRound,
  Loader2,
  LogOut,
  MailWarning,
  X,
} from 'lucide-react'

import {
  AccountAccess,
} from '@/components/account/account-gates'
import { AccountShell } from '@/components/account/account-shell'
import { useAccountProfile } from '@/components/account/account-profile-context'
import { ChangePasswordDialog } from '@/components/account/change-password-dialog'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { useSession } from '@/hooks/use-session'
import { useI18n } from '@/i18n'
import { ACCOUNT_SECURITY_PATH } from '@/lib/account-path'
import { ApiError } from '@/lib/api'
import type { SessionUser } from '@/lib/auth'
import {
  describeEmailVerificationFailure,
  emailVerificationCooldownMessage,
  requestEmailVerification,
} from '@/lib/email-verification'
import { formatDateTime } from '@/lib/format'
import { navigate } from '@/lib/router'

/** Локальный кулдаун повторной отправки: серверный лимит — те же 60 секунд. */
const RESEND_COOLDOWN_SECONDS = 60

/**
 * «Безопасность» личного кабинета.
 *
 * Здесь только то, что действительно поддерживает API: смена пароля, выход и
 * подтверждение email. Списка активных устройств нет — сервер его не отдаёт,
 * поэтому экран не рисует фальшивые строки. Повторная отправка письма живёт с
 * локальным кулдауном, а `429` лимитера объясняется тем же текстом, что и `409`
 * кулдауна: пользователю не важно, кто именно попросил подождать минуту.
 */
export function AccountSecurityPage() {
  const { locale, t } = useI18n()

  // Query читается один раз в состояние: `navigate(..., { replace: true })` ниже
  // убирает его из адреса, и живой `useLocation` тут же потерял бы уведомление.
  const [verificationSent] = useState(readVerificationSent)

  useEffect(() => {
    document.title = t('Безопасность — BarberSearch')
  }, [locale, t])

  useEffect(() => {
    if (!verificationSent) {
      return
    }

    navigate(ACCOUNT_SECURITY_PATH, { replace: true })
  }, [verificationSent])

  return (
    <AccountAccess
      title={t('Войдите, чтобы открыть настройки безопасности')}
      description={t(
        'Смена пароля и выход доступны после входа. Вход по email и паролю.'
      )}
    >
      {(user) => (
        <AccountShell
          user={user}
          section="security"
          title={t('Безопасность')}
          description={t(
            'Пароль, вход и подтверждение адреса. Остальные настройки аккаунта — в профиле.'
          )}
        >
          <SecurityScreen user={user} verificationSent={verificationSent} />
        </AccountShell>
      )}
    </AccountAccess>
  )
}

/** `?verification=sent` — приземление после регистрации. */
function readVerificationSent(): boolean {
  return (
    new URLSearchParams(window.location.search).get('verification') === 'sent'
  )
}

function SecurityScreen({
  user,
  verificationSent,
}: {
  user: SessionUser
  verificationSent: boolean
}) {
  const { t } = useI18n()
  const { signOut } = useSession()
  const profileState = useAccountProfile()
  const profile = profileState.profile
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [isPasswordOpen, setIsPasswordOpen] = useState(false)
  const [isSigningOut, setIsSigningOut] = useState(false)
  const [signOutError, setSignOutError] = useState<string | null>(null)
  const [isResending, setIsResending] = useState(false)
  const [emailNotice, setEmailNotice] = useState<string | null>(null)
  const [emailError, setEmailError] = useState<string | null>(null)
  const [cooldownEndsAt, setCooldownEndsAt] = useState<number | null>(null)
  const [cooldownSeconds, setCooldownSeconds] = useState(0)

  const email = profile?.email ?? user.email
  const isEmailVerified = profile?.isEmailVerified ?? user.isEmailVerified
  // Пока профиль грузится, кнопка не показывается: статус ещё может оказаться
  // подтверждённым, и предлагать письмо было бы неверно.
  const isProfileLoading = profileState.status === 'loading'

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

  async function handleResend() {
    if (isResending || cooldownSeconds > 0) {
      return
    }

    setIsResending(true)
    setEmailNotice(null)
    setEmailError(null)

    try {
      await requestEmailVerification()
      setEmailNotice(t('Письмо отправлено на {email}.', { email }))
      startCooldown()

      // После `204` профиль перечитывается тем же механизмом, что и каркас:
      // статус мог измениться (подтверждение в другой вкладке), а локальная
      // догадка «теперь подтверждён» была бы ложью.
      await profileState.reload()
    } catch (error) {
      if (error instanceof ApiError && error.status === 429) {
        setEmailError(emailVerificationCooldownMessage())
        startCooldown()
      } else {
        const failure = describeEmailVerificationFailure(
          error,
          'Не удалось отправить письмо. Попробуйте позже.'
        )

        setEmailError(failure.message)

        if (failure.kind === 'cooldown') {
          startCooldown()
        }
      }
    } finally {
      setIsResending(false)
    }
  }

  async function handleSignOut() {
    setIsSigningOut(true)
    setSignOutError(null)

    try {
      await signOut()
      navigate('/')
    } catch {
      setSignOutError(
        t('Не удалось выйти. Проверьте соединение и попробуйте снова.')
      )
      setIsSigningOut(false)
    }
  }

  return (
    <div className="space-y-5">
      {verificationSent && (
        <div
          role="status"
          aria-live="polite"
          className="rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          {t(
            'Письмо отправлено на {email}. Откройте письмо и перейдите по ссылке, чтобы подтвердить адрес.',
            { email }
          )}
        </div>
      )}

      {announcement && (
        <div
          role="status"
          className="flex items-start justify-between gap-3 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p>{announcement}</p>
          <button
            type="button"
            aria-label={t('Скрыть сообщение')}
            className="rounded-md p-0.5 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            onClick={() => setAnnouncement(null)}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        </div>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <KeyRound className="size-4 text-muted-foreground" aria-hidden />
            {t('Пароль')}
          </CardTitle>
          <CardDescription>
            {t(
              'Пароль от 12 символов. После смены остальные сессии входа завершатся — здесь вы останетесь.'
            )}
          </CardDescription>
        </CardHeader>
        <CardFooter>
          <Button
            variant="outline"
            className="h-11 sm:h-8"
            onClick={() => setIsPasswordOpen(true)}
          >
            {t('Сменить пароль')}
          </Button>
        </CardFooter>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <LogOut className="size-4 text-muted-foreground" aria-hidden />
            {t('Вход в аккаунт')}
          </CardTitle>
          <CardDescription>
            {t(
              'Вы вошли в этом браузере. Выйти можно здесь; смена пароля завершает остальные сессии.'
            )}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-2 text-sm">
          {profile?.lastLoginAt && (
            <p className="text-muted-foreground">
              {t('Последний вход:')}{' '}
              <time dateTime={profile.lastLoginAt}>
                {formatDateTime(profile.lastLoginAt)}
              </time>
            </p>
          )}
          {signOutError && (
            <p role="alert" className="text-destructive">
              {signOutError}
            </p>
          )}
        </CardContent>
        <CardFooter>
          <Button
            variant="outline"
            className="h-11 sm:h-8"
            disabled={isSigningOut}
            onClick={() => void handleSignOut()}
          >
            <LogOut data-icon="inline-start" aria-hidden />
            {isSigningOut ? t('Выходим…') : t('Выйти')}
          </Button>
        </CardFooter>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            {isEmailVerified ? (
              <CheckCircle2 className="size-4 text-success" aria-hidden />
            ) : (
              <MailWarning className="size-4 text-warning" aria-hidden />
            )}
            {t('Подтверждение email')}
          </CardTitle>
          <CardDescription>
            {isEmailVerified
              ? t(
                  'Адрес подтверждён: на него можно получать письма о записях.'
                )
              : t(
                  'Адрес пока не подтверждён. Подтвердите его по ссылке из письма — пароль и вход это не ограничивает.'
                )}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3 text-sm">
          <p>
            <span className="text-muted-foreground">{t('Адрес:')} </span>
            {email}
          </p>

          <p className="flex items-center gap-1.5">
            {isEmailVerified ? (
              <CheckCircle2 className="size-3.5 shrink-0 text-success" aria-hidden />
            ) : (
              <MailWarning className="size-3.5 shrink-0 text-warning" aria-hidden />
            )}
            <span className={isEmailVerified ? 'text-success' : 'text-warning'}>
              {isEmailVerified
                ? t('Адрес подтверждён.')
                : t('Адрес не подтверждён.')}
            </span>
          </p>

          {!isEmailVerified && !isProfileLoading && (
            <div className="space-y-2">
              <Button
                variant="outline"
                className="h-11 sm:h-8"
                disabled={isResending || cooldownSeconds > 0}
                aria-busy={isResending}
                onClick={() => void handleResend()}
              >
                {isResending && (
                  <Loader2
                    data-icon="inline-start"
                    aria-hidden
                    className="animate-spin"
                  />
                )}
                {isResending
                  ? t('Отправляем письмо…')
                  : cooldownSeconds > 0
                    ? t('Повторить через {count} с', {
                        count: cooldownSeconds,
                      })
                    : t('Отправить письмо ещё раз')}
              </Button>
              <p className="text-xs text-muted-foreground">
                {t('Письмо действует 24 часа.')}
              </p>
            </div>
          )}

          {emailNotice && (
            <p role="status" className="text-success">
              {emailNotice}
            </p>
          )}
          {emailError && (
            <p role="alert" className="text-destructive">
              {emailError}
            </p>
          )}
        </CardContent>
      </Card>

      <ChangePasswordDialog
        open={isPasswordOpen}
        onOpenChange={setIsPasswordOpen}
        onSuccess={() =>
          setAnnouncement(
            t(
              'Пароль изменён. Остальные сессии входа завершены — здесь вы остались.'
            )
          )
        }
      />
    </div>
  )
}
