import { useCallback, useEffect, useRef, useState } from 'react'
import { X } from 'lucide-react'

import { AccountAccess, SignInGate } from '@/components/account/account-gates'
import { AccountShell } from '@/components/account/account-shell'
import { DeleteReviewDialog } from '@/components/account/delete-review-dialog'
import { EditReviewDialog } from '@/components/account/edit-review-dialog'
import { MyReviewsList } from '@/components/account/my-reviews-list'
import { useMyReviews } from '@/hooks/use-my-reviews'
import { useI18n } from '@/i18n'
import { ACCOUNT_REVIEWS_PATH } from '@/lib/account-path'
import type { MyReview, UpdateMyReviewInput } from '@/lib/my-reviews'

/**
 * «Мои отзывы» в личном кабинете.
 *
 * Отдельный адрес, а не вкладка: у экрана своя пагинация и диалоги, на него можно
 * дать ссылку. Список читает `GET api/reviews/mine` и только он: брони и профиль
 * барбера для строки не нужны — контекст визита уже в ответе.
 */
export function AccountReviewsPage() {
  const { locale, t } = useI18n()

  useEffect(() => {
    document.title = t('Мои отзывы — BarberSearch')
  }, [locale, t])

  return (
    <AccountAccess
      title={t('Войдите, чтобы увидеть свои отзывы')}
      description={t(
        'Здесь собраны отзывы, которые вы оставили после визитов: опубликованные и ожидающие проверки. Вход по email и паролю.'
      )}
    >
      {(user) => (
        <AccountShell
          user={user}
          section="reviews"
          title={t('Мои отзывы')}
          description={t(
            'Отзывы, которые вы оставили после визитов. Опубликованные и ожидающие проверки — в одном списке.'
          )}
        >
          <MyReviewsScreen />
        </AccountShell>
      )}
    </AccountAccess>
  )
}

/**
 * Экран списка монтируется только после проверки сессии: иначе анонимный экран
 * сходит за отзывами и получит `401` вместо гейта.
 */
function MyReviewsScreen() {
  const { t } = useI18n()
  const reviews = useMyReviews()
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [editTarget, setEditTarget] = useState<MyReview | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<MyReview | null>(null)

  const headingRef = useRef<HTMLHeadingElement>(null)
  const editButtonsRef = useRef(new Map<string, HTMLButtonElement>())
  const deleteButtonsRef = useRef(new Map<string, HTMLButtonElement>())
  // Куда вернуть фокус после удаления: id следующей строки или заголовок списка.
  const pendingDeleteFocus = useRef<{ id: string } | null>(null)

  useEffect(() => {
    const pending = pendingDeleteFocus.current

    if (!pending) {
      return
    }

    pendingDeleteFocus.current = null

    const button = deleteButtonsRef.current.get(pending.id)

    if (button) {
      button.focus()
      return
    }

    headingRef.current?.focus()
  }, [reviews.items])

  const registerEditButton = useCallback(
    (id: string, element: HTMLButtonElement | null) => {
      if (element) {
        editButtonsRef.current.set(id, element)
      } else {
        editButtonsRef.current.delete(id)
      }
    },
    []
  )

  const registerDeleteButton = useCallback(
    (id: string, element: HTMLButtonElement | null) => {
      if (element) {
        deleteButtonsRef.current.set(id, element)
      } else {
        deleteButtonsRef.current.delete(id)
      }
    },
    []
  )

  async function handleSave(id: string, input: UpdateMyReviewInput) {
    await reviews.updateReview(id, input)
    setAnnouncement(t('Отзыв обновлён.'))
  }

  async function handleDelete(review: MyReview) {
    const index = reviews.items.findIndex((item) => item.id === review.id)
    const next = index >= 0 ? (reviews.items[index + 1] ?? null) : null

    const outcome = await reviews.deleteReview(review.id)

    pendingDeleteFocus.current = { id: next?.id ?? '' }
    setAnnouncement(
      outcome === 'alreadyDeleted'
        ? t('Этот отзыв уже удалён.')
        : t('Отзыв удалён.')
    )
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

      {reviews.requiresSignIn ? (
        <div className="flex min-h-[45vh] items-center justify-center py-6">
          <SignInGate
            title={t('Сессия истекла')}
            description={t(
              'Войдите снова, чтобы увидеть свои отзывы: cookie-сессия закончилась, пока страница была открыта.'
            )}
            redirectTo={ACCOUNT_REVIEWS_PATH}
            onSignedIn={reviews.reload}
          />
        </div>
      ) : (
        <MyReviewsList
          items={reviews.items}
          status={reviews.status}
          error={reviews.error}
          pageIndex={reviews.pageIndex}
          offset={reviews.offset}
          hasPrev={reviews.hasPrev}
          hasNext={reviews.hasNext}
          isLoadingPage={reviews.isLoadingPage}
          pageError={reviews.pageError}
          headingRef={headingRef}
          onRetry={reviews.reload}
          onPrev={reviews.goPrev}
          onNext={reviews.goNext}
          onRequestEdit={setEditTarget}
          onRequestDelete={setDeleteTarget}
          registerEditButton={registerEditButton}
          registerDeleteButton={registerDeleteButton}
        />
      )}

      <EditReviewDialog
        review={editTarget}
        onClose={() => setEditTarget(null)}
        onSave={handleSave}
      />

      <DeleteReviewDialog
        review={deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
      />
    </div>
  )
}
