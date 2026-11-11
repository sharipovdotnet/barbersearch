import { useRef, useState, type ReactNode } from 'react'
import { Loader2 } from 'lucide-react'

import { PhotoRejectionDialog } from '@/components/admin/photo-rejection-dialog'
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
import {
  approveAvatar,
  describePhotoModerationError,
  rejectAvatar,
} from '@/lib/admin-photos'
import type { AvatarModerationAction } from '@/lib/avatar-moderation'
import {
  photoModerationLabel,
  type PhotoModerationStatus,
} from '@/lib/photo-moderation'

/**
 * Блок аватара в админских карточках пользователя и барбера.
 *
 * Аватар — поле `users`, поэтому у обеих карточек одна разметка, один диалог и
 * одни тексты; различается только владелец запроса (`user.id` или `barber.userId`).
 * Админ видит сырой URL и при скрытом фото: иначе он не смог бы вернуть кадр.
 *
 * Премодерация добавляет два состояния: «На проверке» с кнопками «Одобрить» и
 * «Отклонить» и «Отклонено» с причиной без кнопок решения. Повторное решение по
 * уже проверенному кадру сервер отклоняет `409`; карточка показывает русский текст
 * и не притворяется, что решение прошло. После успеха строка обновляется на месте,
 * без второго `GET`: `local` хранит решение до тех пор, пока сервер не пришлёт
 * новый статус.
 *
 * «Скрыть фото» остаётся только у одобренного кадра и спрашивает подтверждение
 * `alertdialog`: фото исчезает из шапки, поиска, карточек и списков броней, но
 * аккаунт остаётся активным. Возврат обратим и диалога не требует.
 */
export function AvatarModeration({
  avatarUrl,
  isAvatarBlocked,
  moderationStatus,
  rejectionReason,
  userId,
  subject,
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
  /** Статус премодерации аватара из админского ответа. */
  moderationStatus: PhotoModerationStatus
  /** Причина отказа администратора; `null`, пока кадр не отклонён. */
  rejectionReason: string | null
  /** Владелец аватара: адрес решения `POST .../avatar/approval|rejection`. */
  userId: string
  /** Имя владельца для диалога отказа. */
  subject: string
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
  const [isRejectOpen, setIsRejectOpen] = useState(false)
  const [pendingDecision, setPendingDecision] = useState<
    'approve' | 'reject' | null
  >(null)
  const [decisionError, setDecisionError] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [local, setLocal] = useState<{
    signature: string
    status: PhotoModerationStatus
    reason: string | null
  } | null>(null)
  const rejectButtonRef = useRef<HTMLButtonElement>(null)
  const actionsRef = useRef<HTMLDivElement>(null)

  // Решение живёт до тех пор, пока сервер не пришлёт новый статус: тогда
  // подпись ответа меняется и карточка снова верит данным, а не памяти формы.
  const signature = `${avatarUrl ?? ''}|${moderationStatus}|${
    rejectionReason ?? ''
  }`
  const status =
    local?.signature === signature ? local.status : moderationStatus
  const reason =
    local?.signature === signature ? local.reason : rejectionReason
  const isDecisionPending = pendingDecision !== null
  const isDisabled = isBusy || isDecisionPending

  function closeReject(): void {
    setIsRejectOpen(false)
    // После отмены кнопка «Отклонить» на месте; после успешного отказа её уже
    // нет, поэтому фокус уходит на контейнер действий — он остаётся в DOM.
    window.requestAnimationFrame(() => {
      const trigger = rejectButtonRef.current

      if (trigger !== null && trigger.isConnected) {
        trigger.focus()
        return
      }

      actionsRef.current?.focus()
    })
  }

  async function handleApprove(): Promise<void> {
    if (isDisabled) {
      return
    }

    setPendingDecision('approve')
    setDecisionError(null)
    setAnnouncement(null)

    try {
      await approveAvatar(userId)
      setLocal({ signature, status: 'Approved', reason: null })
      setAnnouncement(t('Фото одобрено.'))
    } catch (cause) {
      setDecisionError(describePhotoModerationError(cause))
    } finally {
      setPendingDecision(null)
    }
  }

  async function handleReject(reasonText: string): Promise<string | null> {
    setPendingDecision('reject')
    setDecisionError(null)
    setAnnouncement(null)

    try {
      await rejectAvatar(userId, reasonText)
      setLocal({ signature, status: 'Rejected', reason: reasonText })
      setAnnouncement(t('Фото отклонено.'))
      return null
    } catch (cause) {
      return describePhotoModerationError(cause)
    } finally {
      setPendingDecision(null)
    }
  }

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
        <div className="space-y-2">
          <div
            ref={actionsRef}
            tabIndex={-1}
            className="flex flex-col gap-2 outline-none sm:flex-row sm:items-center"
          >
            {isAvatarBlocked ? (
              <Badge variant="destructive">{t('Фото скрыто')}</Badge>
            ) : status === 'Rejected' ? (
              <Badge variant="destructive">
                {photoModerationLabel('Rejected')}
              </Badge>
            ) : status === 'Pending' ? (
              <Badge variant="outline">
                {photoModerationLabel('Pending')}
              </Badge>
            ) : null}

            {isAvatarBlocked ? (
              <Button
                variant="outline"
                className="h-11 sm:h-9"
                disabled={isDisabled}
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
            ) : status === 'Pending' ? (
              <>
                <Button
                  className="h-11 sm:h-9"
                  disabled={isDisabled}
                  aria-busy={pendingDecision === 'approve' || undefined}
                  onClick={() => {
                    void handleApprove()
                  }}
                >
                  {pendingDecision === 'approve' && (
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                  )}
                  {t('Одобрить')}
                </Button>
                <Button
                  ref={rejectButtonRef}
                  variant="outline"
                  className="h-11 text-destructive hover:text-destructive sm:h-9"
                  disabled={isDisabled}
                  aria-busy={pendingDecision === 'reject' || undefined}
                  onClick={() => {
                    setDecisionError(null)
                    setIsRejectOpen(true)
                  }}
                >
                  {pendingDecision === 'reject' && (
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                  )}
                  {t('Отклонить')}
                </Button>
              </>
            ) : status === 'Approved' ? (
              <Button
                variant="destructive"
                className="h-11 sm:h-9"
                disabled={isDisabled}
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
            ) : null}
          </div>

          {!isAvatarBlocked && status === 'Rejected' && reason !== null && (
            <p className="text-sm break-words text-muted-foreground">
              {t('Причина: {reason}', { reason })}
            </p>
          )}

          {decisionError && (
            <p role="alert" className="text-sm text-destructive">
              {decisionError}
            </p>
          )}

          {announcement && (
            <p role="status" className="text-sm text-success">
              {announcement}
            </p>
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

      {isRejectOpen && (
        <PhotoRejectionDialog
          target={subject}
          onClose={closeReject}
          onConfirm={handleReject}
        />
      )}
    </div>
  )
}
