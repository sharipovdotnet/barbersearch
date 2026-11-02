import { useEffect, type ReactNode } from 'react'
import { TriangleAlert } from 'lucide-react'

import {
  AccountAccess,
  SignInGate,
} from '@/components/account/account-gates'
import { AccountShell } from '@/components/account/account-shell'
import { useAccountProfile } from '@/components/account/account-profile-context'
import { ProfileForm } from '@/components/account/profile-form'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { ACCOUNT_PROFILE_PATH } from '@/lib/account-path'
import { formatDateTime } from '@/lib/format'
import {
  ACCOUNT_ROLE_LABELS,
  type MyProfile,
} from '@/lib/my-profile'

/**
 * «Профиль» личного кабинета.
 *
 * Имя, фамилия и телефон редактируются здесь; email остаётся логином и меняется
 * отдельным сценарием. Данные читает каркас кабинета из `GET api/users/me`, поэтому
 * форма не делает собственный запрос.
 */
export function AccountProfilePage() {
  useEffect(() => {
    document.title = 'Профиль — BarberSearch'
  }, [])

  return (
    <AccountAccess
      title="Войдите, чтобы открыть профиль"
      description="Имя, телефон и данные аккаунта доступны после входа. Вход по email и паролю."
    >
      {(user) => (
        <AccountShell
          user={user}
          section="profile"
          title="Профиль"
          description="Имя, телефон и данные аккаунта. Email и пароль меняются в соседних разделах."
        >
          <ProfileScreen />
        </AccountShell>
      )}
    </AccountAccess>
  )
}

function ProfileScreen() {
  const state = useAccountProfile()
  const profile = state.profile

  if (state.requiresSignIn) {
    return (
      <div className="flex min-h-[45vh] items-center justify-center py-6">
        <SignInGate
          title="Сессия истекла"
          description="Войдите снова, чтобы увидеть профиль: cookie-сессия закончилась, пока страница была открыта."
          redirectTo={ACCOUNT_PROFILE_PATH}
          onSignedIn={state.reload}
        />
      </div>
    )
  }

  if (state.status === 'loading' && !profile) {
    return (
      <div className="space-y-5">
        <p role="status" className="sr-only">
          Загружаем профиль…
        </p>
        <Skeleton className="h-80 rounded-xl" aria-hidden />
        <Skeleton className="h-40 rounded-xl" aria-hidden />
      </div>
    )
  }

  if (state.status === 'error' && !profile) {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
          <TriangleAlert className="size-4 text-destructive" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Не удалось загрузить профиль</p>
          <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
            {state.error}
          </p>
        </div>
        <Button size="sm" variant="outline" onClick={state.reload}>
          Повторить
        </Button>
      </div>
    )
  }

  if (!profile) {
    return null
  }

  return (
    <div className="space-y-5">
      <ProfileForm profile={profile} />
      <AccountDetails profile={profile} />
    </div>
  )
}

function AccountDetails({ profile }: { profile: MyProfile }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Аккаунт</CardTitle>
        <CardDescription>Служебные данные аккаунта.</CardDescription>
      </CardHeader>

      <CardContent>
        <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-3">
          <AccountDetail label="Роль">
            {ACCOUNT_ROLE_LABELS[profile.role]}
          </AccountDetail>
          <AccountDetail label="Аккаунт создан">
            <time dateTime={profile.createdAt}>
              {formatDateTime(profile.createdAt)}
            </time>
          </AccountDetail>
          <AccountDetail label="Последний вход">
            {profile.lastLoginAt ? (
              <time dateTime={profile.lastLoginAt}>
                {formatDateTime(profile.lastLoginAt)}
              </time>
            ) : (
              'Ещё не было'
            )}
          </AccountDetail>
        </dl>
      </CardContent>
    </Card>
  )
}

function AccountDetail({
  label,
  children,
}: {
  label: string
  children: ReactNode
}) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm break-words">{children}</dd>
    </div>
  )
}
