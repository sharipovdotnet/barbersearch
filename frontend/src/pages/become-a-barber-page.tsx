import { useEffect, useState, type ReactNode } from 'react'
import {
  ArrowRight,
  CalendarDays,
  CircleCheck,
  ShieldCheck,
  Scissors,
  UserRound,
} from 'lucide-react'

import {
  BarberRequestError,
  BarberRequestStatus,
  BarberRequestStatusSkeleton,
} from '@/components/barber/barber-request-status'
import { SignInGate } from '@/components/account/account-gates'
import { BarberRequestForm } from '@/components/barber/barber-request-form'
import { LoginDialog } from '@/components/login-dialog'
import { SessionUnavailable } from '@/components/session-unavailable'
import { Button, buttonVariants } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useMyBarberRequest } from '@/hooks/use-my-barber-request'
import { useSession } from '@/hooks/use-session'
import { BARBER_CABINET_PATH, BARBER_ONBOARDING_PATH } from '@/lib/barber-cabinet-path'
import { BECOME_A_BARBER_PATH } from '@/lib/barber-request-path'
import type { SessionUser } from '@/lib/auth'
import type { BarberRequest } from '@/lib/barber-requests'
import { REGISTER_PATH } from '@/lib/register-path'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'
import { useI18n } from '@/i18n'

/**
 * Публичная страница «Стать барбером».
 *
 * Один адрес для трёх состояний: анонимный посетитель читает, что даёт статус
 * барбера (и не делает ни одного запроса), клиент подаёт заявку или видит её
 * статус, барбер и администратор получают ссылку в свою рабочую зону. Форма и
 * статус монтируются только после проверки роли, поэтому `/become-a-barber` не
 * дёргает `/barber-requests/mine` ни для кого, кроме клиента.
 */
export function BecomeABarberPage() {
  const { t, locale } = useI18n()
  const { status, user } = useSession()

  useEffect(() => {
    document.title = t('Стать барбером — BarberSearch')
  }, [t, locale])

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-8 sm:py-10">
      <header className="mb-6">
        <h1 className="font-heading text-3xl font-semibold tracking-tight">
          {t('Стать барбером')}
        </h1>
        <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
          {t(
            'Заявка на роль барбера: администратор проверит её и откроет кабинет с записями клиентов.'
          )}
        </p>
      </header>

      {status === 'loading' && <PageSkeleton />}

      {/* Пока неизвестно, есть ли сессия, ни форма, ни «Войти» не показываются. */}
      {status === 'unavailable' && <SessionUnavailable />}

      {status === 'anonymous' && <AnonymousIntro />}

      {status === 'authenticated' && user && <SignedIn user={user} />}
    </div>
  )
}

/**
 * Что даёт статус барбера. Запросов нет: страница обязана работать для анонимного
 * посетителя и на упавшем API.
 */
function AnonymousIntro() {
  const { t } = useI18n()

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <h2 className="font-heading text-xl font-semibold tracking-tight">
            {t('Что даёт статус барбера')}
          </h2>
          <CardDescription>
            {t(
              'Профиль в поиске, собственные записи и расписание. Роль выдаёт администратор после проверки заявки.'
            )}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ul className="space-y-3 text-sm">
            <Benefit icon={UserRound}>
              {t(
                'Публичный профиль с услугами и ценами — вас найдут в поиске.'
              )}
            </Benefit>
            <Benefit icon={CalendarDays}>
              {t(
                'Записи клиентов: подтверждение, завершение и заметки в кабинете.'
              )}
            </Benefit>
            <Benefit icon={Scissors}>
              {t('Своё расписание: рабочие дни, исключения и отпуска.')}
            </Benefit>
          </ul>
        </CardContent>
      </Card>

      <div className="flex flex-col gap-2 sm:flex-row">
        <LoginDialog
          trigger={t('Войти')}
          variant="default"
          size="lg"
          className="h-11 w-full sm:w-auto"
          redirectTo={BECOME_A_BARBER_PATH}
        />
        <a
          href={REGISTER_PATH}
          className={cn(
            buttonVariants({ variant: 'outline', size: 'lg' }),
            'h-11 w-full sm:w-auto'
          )}
          onClick={(event) => {
            if (!isPlainClick(event)) {
              return
            }

            event.preventDefault()
            navigate(REGISTER_PATH)
          }}
        >
          {t('Зарегистрироваться')}
          <ArrowRight data-icon="inline-end" aria-hidden />
        </a>
      </div>

      <p className="text-xs text-muted-foreground">
        {t(
          'Заявку может подать только клиент: администратор проверяет её и только потом меняет роль.'
        )}
      </p>
    </div>
  )
}

function SignedIn({ user }: { user: SessionUser }) {
  if (user.role === 'Admin') {
    return <AdminIntro />
  }

  if (user.role === 'Barber') {
    return <BarberIntro barberProfileId={user.barberProfileId} />
  }

  return <CustomerSection />
}

/** Клиент: своя заявка или форма. Запрос уходит только отсюда. */
function CustomerSection() {
  const { t } = useI18n()
  const mine = useMyBarberRequest()
  const [isReapplying, setIsReapplying] = useState(false)
  const [created, setCreated] = useState<BarberRequest | null>(null)

  // 401 на чтении: сессия закончилась уже после проверки роли. Гейт входа —
  // единственный честный выход, скелетон здесь висел бы вечно (§9.5 правило 7).
  if (mine.requiresSignIn) {
    return (
      <SignInGate
        title={t('Сессия истекла')}
        description={t(
          'Войдите снова, чтобы проверить заявку: cookie-сессия закончилась, пока страница была открыта.'
        )}
        redirectTo={BECOME_A_BARBER_PATH}
        onSignedIn={mine.reload}
      />
    )
  }

  if (mine.status === 'loading' || mine.status === 'idle') {
    return (
      <div role="status" aria-live="polite" aria-busy="true">
        <span className="sr-only">{t('Загружаем заявку…')}</span>
        <BarberRequestStatusSkeleton />
      </div>
    )
  }

  if (mine.status === 'error') {
    return (
      <BarberRequestError
        message={mine.error ?? t('Не удалось проверить заявку.')}
        onRetry={mine.reload}
      />
    )
  }

  if (created) {
    return (
      <BarberRequestStatus
        request={created}
        heading={t('Заявка отправлена')}
      />
    )
  }

  if (mine.request && !isReapplying) {
    return (
      <BarberRequestStatus
        request={mine.request}
        onReapply={() => setIsReapplying(true)}
      />
    )
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <h2 className="font-heading text-xl font-semibold tracking-tight">
            {t('Заявка на роль барбера')}
          </h2>
          <CardDescription>
            {t(
              'Несколько полей о вас — администратор проверит заявку и откроет кабинет. Пока решение не принято, вы остаётесь клиентом.'
            )}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <BarberRequestForm onCreated={setCreated} />
        </CardContent>
      </Card>
    </div>
  )
}

/** Барбер: заявка ему больше не нужна, ему нужна рабочая зона. */
function BarberIntro({ barberProfileId }: { barberProfileId: string | null }) {
  const { t } = useI18n()
  const needsProfile = barberProfileId === null

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <CircleCheck className="size-5 text-success" aria-hidden />
        </div>
        <h2 className="font-heading text-xl font-semibold tracking-tight">
          {t('Вы уже барбер')}
        </h2>
        <CardDescription>
          {needsProfile
            ? t(
                'Роль выдана. Осталось создать профиль барбера — без него клиенты не увидят вас в поиске.'
              )
            : t(
                'Роль выдана, профиль создан. Записи клиентов и расписание — в кабинете барбера.'
              )}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Button
          className="h-11 sm:h-9"
          onClick={() => {
            navigate(needsProfile ? BARBER_ONBOARDING_PATH : BARBER_CABINET_PATH)
          }}
        >
          {needsProfile
            ? t('Создать профиль барбера')
            : t('Открыть кабинет барбера')}
        </Button>
      </CardContent>
    </Card>
  )
}

/** Администратор: раздел заявок живёт в админке, а не здесь. */
function AdminIntro() {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <ShieldCheck className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <h2 className="font-heading text-xl font-semibold tracking-tight">
          {t('Вы администратор')}
        </h2>
        <CardDescription>
          {t(
            'Заявки клиентов разбираются в разделе администрирования: очередь, карточка заявителя, одобрение и отказ с причиной.'
          )}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Button
          className="h-11 sm:h-9"
          onClick={() => {
            navigate('/admin/barber-requests')
          }}
        >
          {t('Открыть заявки')}
        </Button>
      </CardContent>
    </Card>
  )
}

function Benefit({
  icon: Icon,
  children,
}: {
  icon: typeof UserRound
  children: ReactNode
}) {
  return (
    <li className="flex items-start gap-2">
      <Icon className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
      <span>{children}</span>
    </li>
  )
}

/** Скелетон страницы: заголовок уже нарисован, ждёт только карточка. */
function PageSkeleton() {
  const { t } = useI18n()

  return (
    <div role="status" aria-live="polite" aria-busy="true">
      <span className="sr-only">{t('Проверяем сессию…')}</span>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-40 rounded-xl" />
        <Skeleton className="h-11 w-40" />
      </div>
    </div>
  )
}
