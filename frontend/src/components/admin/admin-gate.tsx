import { ShieldAlert, ShieldCheck } from 'lucide-react'

import { LoginDialog } from '@/components/login-dialog'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useI18n } from '@/i18n'
import { navigate } from '@/lib/router'

/** Экран до входа: обычный диалог входа, но с возвратом в админку. */
export function AdminSignInGate({
  returnTo,
  onSignedIn,
}: {
  returnTo: string
  onSignedIn?: () => void
}) {
  const { t } = useI18n()

  return (
    <Card className="w-full">
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <ShieldCheck className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <CardTitle>{t('Вход для администраторов')}</CardTitle>
        <CardDescription>
          {t(
            'Разделы администрирования доступны только администраторам сервиса: очередь отзывов, барберы и действия с ними. Войдите под своей учётной записью.'
          )}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        <LoginDialog
          trigger={t('Войти')}
          variant="default"
          size="lg"
          className="h-11 w-full sm:h-9"
          redirectTo={returnTo}
          onSignedIn={onSignedIn}
        />
        <Button
          variant="ghost"
          size="sm"
          className="h-11 sm:h-7"
          onClick={() => {
            navigate('/')
          }}
        >
          {t('На сайт')}
        </Button>
      </CardContent>
    </Card>
  )
}

/** Вошедший пользователь без роли Admin: данные не запрашиваются. */
export function AdminForbidden() {
  const { t } = useI18n()

  return (
    <Card className="w-full">
      <CardHeader>
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <ShieldAlert className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <CardTitle>{t('Раздел доступен только администратору')}</CardTitle>
        <CardDescription>
          {t(
            'У вашей учётной записи нет прав администратора. Если доступ нужен, обратитесь к владельцу сервиса.'
          )}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Button
          variant="outline"
          size="sm"
          className="h-11 sm:h-7"
          onClick={() => {
            navigate('/')
          }}
        >
          {t('На сайт')}
        </Button>
      </CardContent>
    </Card>
  )
}

/** Неизвестный вложенный адрес внутри админки. */
export function AdminNotFound() {
  const { t } = useI18n()

  return (
    <Card className="w-full">
      <CardHeader>
        <CardTitle>{t('Раздел не найден')}</CardTitle>
        <CardDescription>
          {t(
            'Такого адреса в администрировании нет. Откройте очередь отзывов.'
          )}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Button
          size="sm"
          className="h-11 sm:h-7"
          onClick={() => {
            navigate('/admin/reviews')
          }}
        >
          {t('К отзывам')}
        </Button>
      </CardContent>
    </Card>
  )
}

/** Скелетон проверки сессии и прав. */
export function AdminPageSkeleton() {
  const { t } = useI18n()

  return (
    <div
      className="mx-auto grid w-full max-w-6xl gap-6 px-4 py-6 lg:grid-cols-[13rem_minmax(0,1fr)] lg:items-start lg:py-8"
      aria-busy="true"
    >
      <p role="status" className="sr-only">
        {t('Проверяем доступ…')}
      </p>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-4 w-20" />
        <Skeleton className="h-8 w-full" />
        <Skeleton className="h-8 w-24" />
      </div>
      <div className="space-y-4" aria-hidden>
        <Skeleton className="h-8 w-40" />
        <Skeleton className="h-4 w-72" />
        <Skeleton className="h-96 rounded-xl" />
      </div>
    </div>
  )
}
