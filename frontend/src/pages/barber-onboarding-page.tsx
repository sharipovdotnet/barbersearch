import { useEffect, useState } from 'react'
import { Loader2, MapPin, UserRound } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import {
  BarberCabinetSkeleton,
  BarberRoleGate,
} from '@/components/barber/barber-cabinet-gates'
import {
  BarberProfileForm,
  type BarberProfilePrefill,
} from '@/components/barber/barber-profile-form'
import { SalonPickerDialog } from '@/components/barber/barber-salon-picker-dialog'
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
import { useMyBarberRequest } from '@/hooks/use-my-barber-request'
import { useSession } from '@/hooks/use-session'
import type { BarberRequest } from '@/lib/barber-requests'
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
 *
 * Имя, опыт и «О себе» заявитель уже написал в заявке `barber_requests`, поэтому шаг
 * читает свою заявку (`GET /api/barber-requests/mine`) и передаёт её значения в форму
 * готовыми и только для чтения. Запрос уходит только на шаге формы, то есть после
 * гейтов: аноним и клиент его не делают.
 */

type OnboardingStage =
  | 'form'
  | 'refreshing'
  | 'salon'
  | 'terminal'
  | 'transient'

export function BarberOnboardingPage() {
  const { status, user, retrySession } = useSession()
  const [stage, setStage] = useState<OnboardingStage>('form')
  const [profileId, setProfileId] = useState<string | null>(null)

  useEffect(() => {
    document.title = 'Профиль барбера — BarberSearch'
  }, [])

  const hasProfile =
    status === 'authenticated' &&
    user !== null &&
    user.role === 'Barber' &&
    user.barberProfileId !== null

  // Профиль уже есть (вкладку открыли с устаревшей сессией): форме здесь не место.
  // Шаг «Добавить салон» — исключение: профиль только что создан, и уходить с него
  // нельзя, поэтому авто-переход работает только из формы.
  useEffect(() => {
    if (hasProfile && stage === 'form') {
      navigate(BARBER_CABINET_PATH, { replace: true })
    }
  }, [hasProfile, stage])

  /**
   * После `201` профиль уже существует, но claim ещё старый. Продлеваем сессию и
   * остаёмся на шаге выбора салона: `profileId` берём из ответа `POST`, а не из сессии.
   * Провал продления терминален или временный — оба состояния уже были в онбординге.
   */
  async function finishCreation(id: string): Promise<void> {
    setProfileId(id)
    setStage('refreshing')

    const outcome = await refreshSession()

    if (outcome.kind === 'refreshed') {
      retrySession()
      setStage('salon')
      return
    }

    setStage(outcome.kind === 'terminal' ? 'terminal' : 'transient')
  }

  /**
   * `409` значит, что профиль уже создан, но его id неизвестен: салон предложить
   * нельзя, ведём в кабинет после продления сессии — как было до этого среза.
   */
  async function finishExistingProfile(): Promise<void> {
    setStage('refreshing')

    const outcome = await refreshSession()

    if (outcome.kind === 'refreshed') {
      retrySession()
      navigate(BARBER_CABINET_PATH, { replace: true })
      return
    }

    setStage(outcome.kind === 'terminal' ? 'terminal' : 'transient')
  }

  /** Повтор продления из терминального/временного состояния — без повторного `POST`. */
  async function retrySessionRefresh(): Promise<void> {
    setStage('refreshing')

    const outcome = await refreshSession()

    if (outcome.kind === 'refreshed') {
      retrySession()

      if (profileId !== null) {
        setStage('salon')
      } else {
        navigate(BARBER_CABINET_PATH, { replace: true })
      }

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
        user.barberProfileId === null ||
        stage === 'salon' ||
        stage === 'refreshing' ? (
          <OnboardingScreen
            stage={stage}
            profileId={profileId}
            onCreated={(id) => {
              void finishCreation(id)
            }}
            onAlreadyExists={() => {
              void finishExistingProfile()
            }}
            onRetrySession={() => {
              void retrySessionRefresh()
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

/** Форма, шаг салона или финальная карточка: после создания профиля форма не возвращается. */
function OnboardingScreen({
  stage,
  profileId,
  onCreated,
  onAlreadyExists,
  onRetrySession,
}: {
  stage: OnboardingStage
  profileId: string | null
  onCreated: (id: string) => void
  onAlreadyExists: () => void
  onRetrySession: () => void
}) {
  const [salonAdded, setSalonAdded] = useState(false)

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
        <ProfileFormCard
          onCreated={onCreated}
          onAlreadyExists={onAlreadyExists}
        />
      )}

      {stage === 'refreshing' && (
        <Card>
          <CardContent className="flex items-center gap-2 pt-6 text-sm text-muted-foreground">
            <Loader2 className="size-4 animate-spin" aria-hidden />
            <p role="status">Профиль создан. Обновляем сессию…</p>
          </CardContent>
        </Card>
      )}

      {stage === 'salon' && profileId !== null && (
        <Card>
          <CardHeader>
            <CardTitle>Добавьте место приёма</CardTitle>
            <CardDescription>
              Расписание и запись ведутся по паре «барбер — место приёма». Выберите
              место из каталога или пропустите шаг — кабинет откроется и без него,
              но расписание будет пустым.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {salonAdded ? (
              <p role="status" className="text-sm">
                Салон добавлен.
              </p>
            ) : null}

            <div className="flex flex-wrap items-center gap-2">
              {!salonAdded && (
                <SalonPickerDialog
                  barberProfileId={profileId}
                  hasSalons={false}
                  trigger={
                    <>
                      <MapPin data-icon="inline-start" aria-hidden />
                      Выбрать салон
                    </>
                  }
                  onAttached={() => {
                    setSalonAdded(true)
                  }}
                  onStale={() => {}}
                />
              )}

              <Button
                type="button"
                className="h-11 sm:h-8"
                onClick={() => {
                  navigate(BARBER_CABINET_PATH)
                }}
              >
                Перейти в кабинет
              </Button>

              {!salonAdded && (
                <Button
                  type="button"
                  variant="ghost"
                  className="h-11 sm:h-8"
                  onClick={() => {
                    navigate(BARBER_CABINET_PATH)
                  }}
                >
                  Пропустить
                </Button>
              )}
            </div>

            <p className="text-sm text-muted-foreground">
              Настроить расписание можно позже — в разделе «Расписание».
            </p>
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

/** Одобренная заявка даёт имя, опыт и «О себе»; остальные статусы — нет. */
function toPrefill(request: BarberRequest | null): BarberProfilePrefill | null {
  if (request === null || request.status !== 'Approved') {
    return null
  }

  return {
    displayName: request.displayName,
    yearsOfExperience: request.yearsOfExperience,
    bio: request.about,
  }
}

/**
 * Форма поверх заявки: читает её и передаёт заполненные поля готовыми.
 *
 * Пока заявка читается, форма не рендерится: иначе поля успели бы показаться
 * редактируемыми и только потом перерисоваться в `readOnly`. Ошибка чтения тоже
 * закрывает форму — профиль создался бы без данных заявки, а редактировать их
 * после `201` негде.
 */
function ProfileFormCard({
  onCreated,
  onAlreadyExists,
}: {
  onCreated: (id: string) => void
  onAlreadyExists: () => void
}) {
  const mine = useMyBarberRequest()

  // Сессия закончилась: гейт входа выше, карточке нечего добавить.
  if (mine.requiresSignIn) {
    return null
  }

  if (mine.status === 'loading' || mine.status === 'idle') {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Данные профиля</CardTitle>
        </CardHeader>
        <CardContent>
          <p role="status" className="text-sm text-muted-foreground">
            Проверяем заявку…
          </p>
        </CardContent>
      </Card>
    )
  }

  if (mine.status === 'error') {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Данные профиля</CardTitle>
          <CardDescription>
            Имя, опыт и «О себе» берутся из одобренной заявки, поэтому без неё
            профиль не создать.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <p role="alert" className="text-sm text-destructive">
            {mine.error ?? 'Не удалось проверить заявку.'}
          </p>
          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-8"
            onClick={mine.reload}
          >
            Повторить
          </Button>
        </CardContent>
      </Card>
    )
  }

  const prefill = toPrefill(mine.request)

  return (
    <Card>
      <CardHeader>
        <CardTitle>Данные профиля</CardTitle>
        <CardDescription>
          {prefill
            ? 'Данные из одобренной заявки уже заполнены и не редактируются. Выберите грейд и языки — остальное можно оставить пустым.'
            : 'Это увидит клиент в вашей карточке. Обязательны только имя, опыт, грейд и хотя бы один язык — остальное можно оставить пустым.'}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <BarberProfileForm
          onCreated={onCreated}
          onAlreadyExists={onAlreadyExists}
          prefill={prefill}
        />
      </CardContent>
    </Card>
  )
}
