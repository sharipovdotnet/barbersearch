import { useEffect, useState } from 'react'
import { Loader2, UserRound } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import {
  BarberCabinetSkeleton,
  BarberRoleGate,
} from '@/components/barber/barber-cabinet-gates'
import { BarberProfileForm } from '@/components/barber/barber-profile-form'
import { LoginDialog } from '@/components/login-dialog'
import { SessionUnavailable } from '@/components/session-unavailable'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { useSession } from '@/hooks/use-session'
import {
  BARBER_CABINET_PATH,
  BARBER_ONBOARDING_PATH,
} from '@/lib/barber-cabinet-path'
import { navigate } from '@/lib/router'
import { refreshSession } from '@/lib/session-refresh'

/**
 * Онбординг барбера: создание профиля без администратора.
 *
 * Регистрация всегда создаёт клиента, роль `Barber` выдаёт администратор, но до этого
 * среза после выдачи роли кабинет закрывался тупиком «профиль не привязан». Здесь
 * профиль создаётся существующим `POST /api/barber-profiles` — он доступен только роли
 * `Barber`, то есть ровно тому, кто не мог до него добраться из интерфейса.
 *
 * `barber_profile_id` живёт в claim access-токена, поэтому после `201` старый токен
 * честно говорит `null`: клиент продлевает сессию (`refreshSession`), повторяет проверку
 * (`retrySession`) и только затем переходит в кабинет. Форма после создания не
 * возвращается — профиль уже есть.
 */

type OnboardingStage = 'form' | 'finishing' | 'terminal' | 'transient'

export function BarberOnboardingPage() {
  const { status, user, retrySession } = useSession()
  const [stage, setStage] = useState<OnboardingStage>('form')

  useEffect(() => {
    document.title = 'Профиль барбера — BarberSearch'
  }, [])

  const hasProfile =
    status === 'authenticated' &&
    user !== null &&
    user.role === 'Barber' &&
    user.barberProfileId !== null

  // Профиль уже есть (вкладку открыли с устаревшей сессией): форме здесь не место.
  // Переход идемпотентен — StrictMode выполняет эффект дважды.
  useEffect(() => {
    if (hasProfile) {
      navigate(BARBER_CABINET_PATH, { replace: true })
    }
  }, [hasProfile])

  /**
   * Один общий финал для `201` и `409`: профиль существует, нужен свежий claim.
   * Провал продления терминален (сессию начинают заново) или временный (повторяем
   * только refresh, но никогда не `POST` — иначе получили бы `409`).
   */
  async function finishCreation(): Promise<void> {
    setStage('finishing')

    const outcome = await refreshSession()

    if (outcome.kind === 'refreshed') {
      retrySession()
      navigate(BARBER_CABINET_PATH, { replace: true })
      return
    }

    setStage(outcome.kind === 'terminal' ? 'terminal' : 'transient')
  }

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-8 sm:py-10">
      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            Загружаем профиль…
          </p>
          <BarberCabinetSkeleton />
        </div>
      )}

      {status === 'unavailable' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SessionUnavailable />
        </div>
      )}

      {status === 'anonymous' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SignInGate
            title="Войдите, чтобы создать профиль барбера"
            description="Профиль барбера создаётся один раз для вашего аккаунта: имя в карточке, опыт и языки. Вход по email и паролю."
            redirectTo={BARBER_ONBOARDING_PATH}
          />
        </div>
      )}

      {status === 'authenticated' && user && user.role !== 'Barber' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <BarberRoleGate role={user.role} />
        </div>
      )}

      {status === 'authenticated' && user && user.role === 'Barber' && (
        user.barberProfileId === null ? (
          <OnboardingScreen
            stage={stage}
            onCreated={() => {
              void finishCreation()
            }}
            onAlreadyExists={() => {
              void finishCreation()
            }}
            onRetrySession={() => {
              void finishCreation()
            }}
          />
        ) : (
          <div className="space-y-5">
            <p role="status" className="sr-only">
              Открываем кабинет…
            </p>
            <BarberCabinetSkeleton />
          </div>
        )
      )}
    </div>
  )
}

/** Форма или финальная карточка: после создания профиля форма не возвращается. */
function OnboardingScreen({
  stage,
  onCreated,
  onAlreadyExists,
  onRetrySession,
}: {
  stage: OnboardingStage
  onCreated: () => void
  onAlreadyExists: () => void
  onRetrySession: () => void
}) {
  return (
    <div className="space-y-5">
      <header className="flex items-center gap-3">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <UserRound className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <div>
          <h1 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
            Профиль барбера
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Заполните профиль, чтобы принимать записи.
          </p>
        </div>
      </header>

      {stage === 'form' && (
        <Card>
          <CardHeader>
            <CardTitle>Данные профиля</CardTitle>
            <CardDescription>
              Это увидит клиент в вашей карточке. Обязательны только имя, опыт и
              хотя бы один язык — остальное можно оставить пустым.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <BarberProfileForm
              onCreated={onCreated}
              onAlreadyExists={onAlreadyExists}
            />
          </CardContent>
        </Card>
      )}

      {stage === 'finishing' && (
        <Card>
          <CardContent className="flex items-center gap-2 pt-6 text-sm text-muted-foreground">
            <Loader2 className="size-4 animate-spin" aria-hidden />
            <p role="status">Профиль создан. Обновляем сессию…</p>
          </CardContent>
        </Card>
      )}

      {stage === 'terminal' && (
        <Card>
          <CardHeader>
            <CardTitle>Профиль создан, войдите снова</CardTitle>
            <CardDescription>
              Профиль барбера уже привязан к аккаунту, но сессия в этом браузере
              закончилась. Войдите — и кабинет откроется.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <LoginDialog
              trigger="Войти"
              variant="default"
              size="lg"
              className="h-11 w-full sm:h-8 sm:w-auto"
              redirectTo={BARBER_CABINET_PATH}
            />
          </CardContent>
        </Card>
      )}

      {stage === 'transient' && (
        <Card>
          <CardHeader>
            <CardTitle>Профиль создан, но сессия не обновилась</CardTitle>
            <CardDescription>
              Повторять форму не нужно — профиль уже существует. Продлите сессию
              ещё раз или войдите заново.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              className="h-11 sm:h-8"
              onClick={onRetrySession}
            >
              Повторить
            </Button>
            <LoginDialog
              trigger="Войти"
              variant="ghost"
              size="lg"
              className="h-11 sm:h-8"
              redirectTo={BARBER_CABINET_PATH}
            />
          </CardContent>
        </Card>
      )}
    </div>
  )
}
