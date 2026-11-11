import { useState, type ReactNode } from 'react'
import { Loader2 } from 'lucide-react'

import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { useI18n } from '@/i18n'
import type { AvatarModerationAction } from '@/lib/avatar-moderation'

/**
 * Блок аватара в админских карточках пользователя и барбера.
 *
 * Аватар — поле `users`, поэтому у обеих карточек одна разметка, один диалог и
 * одни тексты; различается только владелец запроса (`user.id` или `barber.userId`).
 * Админ видит сырой URL и при скрытом фото: иначе он не смог бы вернуть кадр.
 *
 * «Скрыть фото» спрашивает подтверждение `alertdialog`: фото исчезает из шапки,
 * поиска, карточек и списков броней, но аккаунт остаётся активным. Возврат
 * обратим и диалога не требует — как разблокировка в портфолио.
 */
export function AvatarModeration({
  avatarUrl,
  isAvatarBlocked,
  initials,
  isBusy,
  pendingAction,
  onBlock,
  onUnblock,
  children,
}: {
  /** Сырой URL: рендерится и при блокировке — это админская поверхность. */
  avatarUrl: string | null
  isAvatarBlocked: boolean
  initials: string
  /** Любое действие карточки: кнопки аватара гаснут вместе с остальными. */
  isBusy: boolean
  pendingAction: AvatarModerationAction | null
  onBlock: () => void
  onUnblock: () => void
  children: ReactNode
}) {
  const { t } = useI18n()
  const [isConfirmOpen, setIsConfirmOpen] = useState(false)

  return (
    <div className="space-y-3">
      <div className="flex items-start gap-3">
        <Avatar size="lg">
          {avatarUrl && <AvatarImage src={avatarUrl} alt="" />}
          <AvatarFallback>{initials}</AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1">{children}</div>
      </div>

      {avatarUrl === null ? (
        <p className="text-xs text-muted-foreground">
          {t('У пользователя нет фото — скрывать нечего.')}
        </p>
      ) : (
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          {isAvatarBlocked && (
            <Badge variant="destructive">{t('Фото скрыто')}</Badge>
          )}

          {isAvatarBlocked ? (
            <Button
              variant="outline"
              className="h-11 sm:h-9"
              disabled={isBusy}
              onClick={onUnblock}
            >
              {pendingAction === 'unblockAvatar' && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {pendingAction === 'unblockAvatar'
                ? t('Возвращаем…')
                : t('Вернуть фото')}
            </Button>
          ) : (
            <Button
              variant="destructive"
              className="h-11 sm:h-9"
              disabled={isBusy}
              onClick={() => setIsConfirmOpen(true)}
            >
              {pendingAction === 'blockAvatar' && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {pendingAction === 'blockAvatar'
                ? t('Скрываем…')
                : t('Скрыть фото')}
            </Button>
          )}
        </div>
      )}

      <Dialog open={isConfirmOpen} onOpenChange={setIsConfirmOpen}>
        <DialogContent role="alertdialog">
          <DialogHeader>
            <DialogTitle>{t('Скрыть фото?')}</DialogTitle>
            <DialogDescription>
              {t(
                'Фото исчезнет из шапки, поиска, карточек и списков броней. Аккаунт останется активным, вернуть фото можно здесь же.'
              )}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              className="h-11 sm:h-8"
              disabled={isBusy}
              onClick={() => setIsConfirmOpen(false)}
            >
              {t('Отмена')}
            </Button>
            <Button
              type="button"
              variant="destructive"
              className="h-11 sm:h-8"
              disabled={isBusy}
              onClick={() => {
                setIsConfirmOpen(false)
                onBlock()
              }}
            >
              {t('Скрыть фото')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
