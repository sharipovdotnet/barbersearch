import { KeyRound, LogOut, MailWarning } from 'lucide-react'

import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import type { SessionUser } from '@/lib/auth'

/** Кто вошёл и что с этим аккаунтом можно сделать. */
export function ProfileCard({
  user,
  onChangePassword,
  onSignOut,
}: {
  user: SessionUser
  onChangePassword: () => void
  onSignOut: () => void
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-3">
          <Avatar size="lg" aria-hidden>
            <AvatarFallback>{user.email.slice(0, 1).toUpperCase()}</AvatarFallback>
          </Avatar>
          <div className="min-w-0">
            <CardTitle className="truncate text-sm">{user.email}</CardTitle>
            <CardDescription className="text-xs">Клиент</CardDescription>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-3">
        {!user.isEmailVerified && (
          <div className="flex gap-2 rounded-lg border border-warning/30 bg-warning/10 p-2.5 text-xs text-warning">
            <MailWarning className="mt-px size-3.5 shrink-0" aria-hidden />
            <p>
              Email не подтверждён: подтверждение появится вместе с письмом-ссылкой.
            </p>
          </div>
        )}

        <div className="grid gap-1.5">
          <Button
            variant="outline"
            size="sm"
            className="justify-start"
            onClick={onChangePassword}
          >
            <KeyRound data-icon="inline-start" aria-hidden />
            Сменить пароль
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="justify-start text-muted-foreground"
            onClick={onSignOut}
          >
            <LogOut data-icon="inline-start" aria-hidden />
            Выйти
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

/** Короткая памятка: страница отвечает на вопросы «что дальше» и «что можно». */
export function HowItWorksCard() {
  return (
    <Card size="sm">
      <CardHeader>
        <CardTitle className="text-sm">Как это работает</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2 text-sm text-muted-foreground">
        <p>Заявку подтверждает барбер — статус обновится на этой странице.</p>
        <p>Отменить запись можно до начала визита, пока она не завершена.</p>
        <p>После визита появится кнопка отзыва: он уйдёт на проверку.</p>
      </CardContent>
    </Card>
  )
}
