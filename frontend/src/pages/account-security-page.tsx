import { useEffect, useState } from 'react'
import {
  CheckCircle2,
  KeyRound,
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
import type { SessionUser } from '@/lib/auth'
import { formatDateTime } from '@/lib/format'
import { navigate } from '@/lib/router'

/**
 * «Безопасность» личного кабинета.
 *
 * Здесь только то, что действительно поддерживает API: смена пароля и выход.
 * Списка активных устройств нет — сервер его не отдаёт, поэтому экран не рисует
 * фальшивые строки. Смена пароля отзывает refresh-токены, то есть завершает
 * остальные сессии входа.
 */
export function AccountSecurityPage() {
  const { locale, t } = useI18n()

  useEffect(() => {
    document.title = t('Безопасность — BarberSearch')
  }, [locale, t])

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
          <SecurityScreen user={user} />
        </AccountShell>
      )}
    </AccountAccess>
  )
}

function SecurityScreen({ user }: { user: SessionUser }) {
  const { t } = useI18n()
  const { signOut } = useSession()
  const profileState = useAccountProfile()
  const profile = profileState.profile
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [isPasswordOpen, setIsPasswordOpen] = useState(false)
  const [isSigningOut, setIsSigningOut] = useState(false)
  const [signOutError, setSignOutError] = useState<string | null>(null)

  const isEmailVerified = profile?.isEmailVerified ?? user.isEmailVerified

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
                  'Адрес пока не подтверждён. Письмо с подтверждением появится вместе с отдельным сценарием — пароль и вход это не ограничивает.'
                )}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <p className="text-sm">
            <span className="text-muted-foreground">{t('Адрес:')} </span>
            {profile?.email ?? user.email}
          </p>
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
