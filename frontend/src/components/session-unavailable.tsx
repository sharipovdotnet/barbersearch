import { RefreshCw, WifiOff } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { useSession } from '@/hooks/use-session'
import { useI18n } from '@/i18n'

/**
 * Состояние «сессию проверить не удалось».
 *
 * Это не провалившееся действие пользователя, поэтому без `role="alert"`: карточка
 * объясняет причину и даёт единственный безопасный шаг — повторить проверку.
 */
export function SessionUnavailable() {
  const { t } = useI18n()
  const { retrySession } = useSession()

  return (
    <div className="mx-auto w-full max-w-md">
      <Card>
        <CardHeader>
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <WifiOff className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <CardTitle>{t('Не удалось проверить сессию')}</CardTitle>
          <CardDescription>
            {t(
              'Сервер не ответил. Проверьте подключение и повторите попытку.'
            )}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button
            size="lg"
            className="h-11 w-full sm:h-9"
            onClick={retrySession}
          >
            <RefreshCw data-icon="inline-start" aria-hidden />
            {t('Повторить')}
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

/** Компактный вариант для шапки: на узком экране остаётся иконка с доступным именем. */
export function SessionRetryButton() {
  const { t } = useI18n()
  const { retrySession } = useSession()

  return (
    <Button
      variant="outline"
      size="sm"
      onClick={retrySession}
      aria-label={t('Нет связи с сервером. Повторить проверку сессии')}
    >
      <RefreshCw data-icon="inline-start" aria-hidden />
      <span className="hidden sm:inline">{t('Повторить')}</span>
    </Button>
  )
}
