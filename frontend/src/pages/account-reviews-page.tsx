import { useCallback, useEffect, useRef, useState } from 'react'
import { ArrowLeft, X } from 'lucide-react'

import {
  AccountSkeleton,
  RoleGate,
  SignInGate,
} from '@/components/account/account-gates'
import { DeleteReviewDialog } from '@/components/account/delete-review-dialog'
import { EditReviewDialog } from '@/components/account/edit-review-dialog'
import { MyReviewsList } from '@/components/account/my-reviews-list'
import { SessionUnavailable } from '@/components/session-unavailable'
import { buttonVariants } from '@/components/ui/button'
import { useMyReviews } from '@/hooks/use-my-reviews'
import { useSession } from '@/hooks/use-session'
import { ACCOUNT_PATH, ACCOUNT_REVIEWS_PATH } from '@/lib/account-path'
import type { MyReview, UpdateMyReviewInput } from '@/lib/my-reviews'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * «Мои отзывы» в кабинете клиента.
 *
 * Отдельный адрес, а не вкладка: у экрана своя пагинация и диалоги, на него можно
 * дать ссылку. Список читает `GET api/reviews/mine` и только он: брони и профиль
 * барбера для строки не нужны — контекст визита уже в ответе.
 */
export function AccountReviewsPage() {
  const { status, user } = useSession()

  useEffect(() => {
    document.title = 'Мои отзывы — BarberSearch'
  }, [])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && <AccountSkeleton />}

      {status === 'unavailable' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SessionUnavailable />
        </div>
      )}

      {status === 'anonymous' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SignInGate
            title="Войдите, чтобы увидеть свои отзывы"
            description="Здесь собраны отзывы, которые вы оставили после визитов: опубликованные и ожидающие проверки. Вход по email и паролю."
            redirectTo={ACCOUNT_REVIEWS_PATH}
          />
        </div>
      )}

      {status === 'authenticated' && user && user.role !== 'Customer' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <RoleGate role={user.role} />
        </div>
      )}

      {status === 'authenticated' && user && user.role === 'Customer' && (
        <MyReviewsScreen />
      )}
    </div>
  )
}

/**
 * Экран списка монтируется только после проверки сессии и роли: иначе анонимный
 * экран сходит за отзывами и получит `401` вместо гейта.
 */
function MyReviewsScreen() {
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
    setAnnouncement('Отзыв обновлён.')
  }

  async function handleDelete(review: MyReview) {
    const index = reviews.items.findIndex((item) => item.id === review.id)
    const next = index >= 0 ? (reviews.items[index + 1] ?? null) : null

    const outcome = await reviews.deleteReview(review.id)

    pendingDeleteFocus.current = { id: next?.id ?? '' }
    setAnnouncement(
      outcome === 'alreadyDeleted'
        ? 'Этот отзыв уже удалён.'
        : 'Отзыв удалён.'
    )

    // Страница опустела, но сервер знает продолжение: иначе экран соврал бы «пусто».
    if (reviews.items.length <= 1 && reviews.nextCursor !== null) {
      reviews.loadMore()
    }
  }

  return (
    <div className="space-y-5">
      <header className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <h1 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
            Мои отзывы
          </h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            Отзывы, которые вы оставили после визитов. Опубликованные и ожидающие
            проверки — в одном списке.
          </p>
        </div>

        <div className="sm:pt-0.5">
          <a
            href={ACCOUNT_PATH}
            className={cn(
              buttonVariants({ variant: 'outline', size: 'sm' }),
              'min-h-9 sm:min-h-0'
            )}
            onClick={(event) => {
              if (isPlainClick(event)) {
                event.preventDefault()
                navigate(ACCOUNT_PATH)
              }
            }}
          >
            <ArrowLeft data-icon="inline-start" aria-hidden />
            Мои записи
          </a>
        </div>
      </header>

      {announcement && (
        <div
          role="status"
          className="flex items-start justify-between gap-3 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p>{announcement}</p>
          <button
            type="button"
            aria-label="Скрыть сообщение"
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
            title="Сессия истекла"
            description="Войдите снова, чтобы увидеть свои отзывы: cookie-сессия закончилась, пока страница была открыта."
            redirectTo={ACCOUNT_REVIEWS_PATH}
            onSignedIn={reviews.reload}
          />
        </div>
      ) : (
        <MyReviewsList
          items={reviews.items}
          status={reviews.status}
          error={reviews.error}
          nextCursor={reviews.nextCursor}
          isLoadingMore={reviews.isLoadingMore}
          loadMoreError={reviews.loadMoreError}
          headingRef={headingRef}
          onRetry={reviews.reload}
          onLoadMore={reviews.loadMore}
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
