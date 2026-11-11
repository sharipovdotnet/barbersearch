import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react'
import {
  CheckCircle2,
  Loader2,
  RefreshCw,
  Search,
  TriangleAlert,
  X,
} from 'lucide-react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import { ListPager } from '@/components/list-pager'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { useAutoRefresh } from '@/hooks/use-auto-refresh'
import { usePagedList } from '@/hooks/use-paged-list'
import { useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  ADMIN_PHOTO_SEARCH_MAX_LENGTH,
  approveAvatar,
  approvePortfolioPhoto,
  describeAdminPhotoListError,
  describePhotoModerationError,
  fetchAdminAvatarPhotos,
  fetchAdminPortfolioPhotos,
  isAdminPhotoUnauthorized,
  rejectAvatar,
  rejectPortfolioPhoto,
  type AdminAvatarPhoto,
  type AdminPortfolioPhoto,
} from '@/lib/admin-photos'
import type { PhotosTab } from '@/lib/admin-path'
import { photosPath } from '@/lib/admin-path'
import { formatClockTime, formatShortDateTime } from '@/lib/format'
import { formatPageRange, type Page } from '@/lib/paging'
import {
  PHOTO_REJECTION_MAX_LENGTH,
  validatePhotoRejectionReason,
} from '@/lib/photo-moderation'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/** Одна строка очереди: аватар пользователя или работа в портфолио. */
type PhotoRow =
  | { kind: 'avatar'; photo: AdminAvatarPhoto }
  | { kind: 'portfolio'; photo: AdminPortfolioPhoto }

const TABS: readonly { id: PhotosTab; label: string }[] = [
  { id: 'avatars', label: 'Аватары' },
  { id: 'portfolio', label: 'Портфолио' },
]

/** Ключ строки: id пользователя у аватара, id фото у работы. */
function rowId(row: PhotoRow): string {
  return row.kind === 'avatar' ? row.photo.userId : row.photo.photoId
}

/** Имя кадра: имя пользователя у аватара, имя барбера у работы. */
function rowName(row: PhotoRow): string {
  if (row.kind === 'portfolio') {
    return row.photo.barberDisplayName
  }

  const name = `${row.photo.firstName} ${row.photo.lastName}`.trim()

  return name === '' ? row.photo.email : name
}

/**
 * Имя в `aria-label` кнопки решения.
 *
 * У работы к нему добавляется подпись кадра: в очереди портфолио у одного барбера
 * может быть несколько кадров, и «Одобрить: Артём Соколов» было бы неоднозначным.
 */
function rowActionTarget(row: PhotoRow): string {
  if (row.kind === 'avatar') {
    return rowName(row)
  }

  return row.photo.description
    ? `${row.photo.barberDisplayName}: ${row.photo.description}`
    : row.photo.barberDisplayName
}

function rowUrl(row: PhotoRow): string {
  return row.kind === 'avatar' ? row.photo.avatarUrl : row.photo.url
}

/**
 * Строка очереди премодерации: превью, владелец, подпись и два решения.
 *
 * Кнопки решения подписаны видимым словом и именем кадра: экран читается
 * голосом и диктовкой (WCAG 2.5.3). На 320 px строка становится карточкой:
 * превью сверху, кнопки — во всю ширину и высотой 44 px.
 */
function PhotoRowView({
  row,
  isActing,
  disabled,
  onPreview,
  onApprove,
  onReject,
  previewRef,
}: {
  row: PhotoRow
  isActing: boolean
  disabled: boolean
  onPreview: (row: PhotoRow, trigger: HTMLButtonElement) => void
  onApprove: (row: PhotoRow) => void
  onReject: (row: PhotoRow, trigger: HTMLButtonElement) => void
  previewRef: (element: HTMLButtonElement | null) => void
}) {
  const { t } = useI18n()
  const name = rowName(row)
  const actionTarget = rowActionTarget(row)
  const email =
    row.kind === 'avatar' ? row.photo.email : row.photo.ownerEmail
  const description =
    row.kind === 'portfolio' ? row.photo.description : null

  return (
    <li className="p-3">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <button
          ref={previewRef}
          type="button"
          aria-label={t('Открыть фото крупно: {name}', { name })}
          className="size-20 shrink-0 overflow-hidden rounded-lg bg-muted ring-1 ring-foreground/10 outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
          onClick={(event) => onPreview(row, event.currentTarget)}
        >
          <img
            src={rowUrl(row)}
            alt=""
            className="size-full object-cover"
            loading="lazy"
          />
        </button>

        <div className="min-w-0 flex-1 space-y-1">
          <p className="font-medium break-words">{name}</p>
          <p className="text-xs break-all text-muted-foreground">{email}</p>
          {description !== null && description !== '' && (
            <p className="text-sm break-words text-muted-foreground">
              {description}
            </p>
          )}
          <p className="text-xs text-muted-foreground tabular-nums">
            {t('ждёт с {date}', {
              date: formatShortDateTime(row.photo.submittedAt),
            })}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2 sm:shrink-0">
          <Button
            type="button"
            className="h-11 flex-1 sm:h-8 sm:flex-none"
            aria-label={t('Одобрить: {name}', { name: actionTarget })}
            aria-busy={isActing}
            disabled={disabled}
            onClick={() => onApprove(row)}
          >
            {isActing && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {t('Одобрить')}
          </Button>
          <Button
            type="button"
            variant="outline"
            className="h-11 flex-1 sm:h-8 sm:flex-none"
            aria-label={t('Отклонить: {name}', { name: actionTarget })}
            disabled={disabled}
            onClick={(event) => onReject(row, event.currentTarget)}
          >
            {t('Отклонить')}
          </Button>
        </div>
      </div>
    </li>
  )
}

/**
 * Вкладки очереди: аватары или портфолио.
 *
 * Вкладка — настоящая ссылка с `href` из `photosPath`: средний клик и «открыть
 * в новой вкладке» работают, а обычный левый клик перехватывает SPA. Клавиши
 * ←/→, Home и End ходят по вкладкам, как в каталоге и отзывах.
 */
function PhotoTabs({
  tab,
  onChange,
}: {
  tab: PhotosTab
  onChange: (tab: PhotosTab) => void
}) {
  const { t } = useI18n()
  const tabRefs = useRef<Record<PhotosTab, HTMLAnchorElement | null>>({
    avatars: null,
    portfolio: null,
  })

  function moveTo(next: PhotosTab) {
    onChange(next)
    tabRefs.current[next]?.focus()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLAnchorElement>) {
    const index = TABS.findIndex((item) => item.id === tab)

    switch (event.key) {
      case 'ArrowRight':
        event.preventDefault()
        moveTo(TABS[(index + 1) % TABS.length].id)
        break
      case 'ArrowLeft':
        event.preventDefault()
        moveTo(TABS[(index - 1 + TABS.length) % TABS.length].id)
        break
      case 'Home':
        event.preventDefault()
        moveTo(TABS[0].id)
        break
      case 'End':
        event.preventDefault()
        moveTo(TABS[TABS.length - 1].id)
        break
      default:
        break
    }
  }

  return (
    <div
      role="tablist"
      aria-label={t('Какие фотографии показать')}
      className="flex max-w-full items-center gap-0.5 overflow-x-auto rounded-lg bg-muted p-0.5"
    >
      {TABS.map((item) => {
        const isSelected = item.id === tab

        return (
          <a
            key={item.id}
            ref={(element) => {
              tabRefs.current[item.id] = element
            }}
            href={photosPath(item.id)}
            role="tab"
            id={`photos-tab-${item.id}`}
            aria-selected={isSelected}
            aria-controls="photos-panel"
            tabIndex={isSelected ? 0 : -1}
            onClick={(event) => {
              if (!isPlainClick(event)) {
                return
              }

              event.preventDefault()
              onChange(item.id)
            }}
            onKeyDown={handleKeyDown}
            className={cn(
              'inline-flex h-11 shrink-0 items-center rounded-md px-3 text-sm whitespace-nowrap transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
              isSelected
                ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                : 'text-muted-foreground hover:text-foreground'
            )}
          >
            {t(item.label)}
          </a>
        )
      })}
    </div>
  )
}

/**
 * `/admin/photos` — очередь премодерации аватаров и работ в портфолио.
 *
 * Вкладка живёт в `?tab=`, поиск отправляется с задержкой и не попадает в адрес:
 * очередь читается только со статусом `Pending` в порядке поступления, поэтому
 * карточки и второго чтения у строки нет. Решение принимается прямо в списке:
 * «Одобрить» действует сразу, «Отклонить» спрашивает причину в `alertdialog`.
 * После решения строка исчезает локально, объявление уходит в `role="status"`,
 * а очередь тихо перечитывается — сервер остаётся источником правды.
 */
export function PhotosScreen({ tab }: { tab: PhotosTab }) {
  const { t } = useI18n()
  const headingRef = useRef<HTMLHeadingElement | null>(null)
  const previewRefs = useRef(new Map<string, HTMLButtonElement>())
  const previewTriggerRef = useRef<HTMLButtonElement | null>(null)
  const rejectTriggerRef = useRef<HTMLButtonElement | null>(null)

  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')

  // Задержка перед запросом: 300 мс хватает, чтобы не искать на каждую букву.
  useEffect(() => {
    const trimmed = searchInput.trim()

    if (trimmed === search) {
      return
    }

    const timer = window.setTimeout(() => setSearch(trimmed), 300)

    return () => window.clearTimeout(timer)
  }, [searchInput, search])

  const fetchPage = useCallback(
    async (
      cursor: string | null,
      signal: AbortSignal
    ): Promise<Page<PhotoRow>> => {
      if (tab === 'avatars') {
        const page = await fetchAdminAvatarPhotos({
          search: search === '' ? undefined : search,
          cursor,
          signal,
        })

        return {
          items: page.items.map((photo) => ({ kind: 'avatar', photo })),
          nextCursor: page.nextCursor,
        }
      }

      const page = await fetchAdminPortfolioPhotos({
        search: search === '' ? undefined : search,
        cursor,
        signal,
      })

      return {
        items: page.items.map((photo) => ({ kind: 'portfolio', photo })),
        nextCursor: page.nextCursor,
      }
    },
    [tab, search]
  )

  const list = usePagedList<PhotoRow>({
    getId: rowId,
    fetchPage,
    fallback: t('Не удалось загрузить фотографии на проверке.'),
    pageFallback: t('Не удалось загрузить следующую страницу фотографий.'),
    requiresAuth: true,
    mapError: describeAdminPhotoListError,
  })

  const [preview, setPreview] = useState<PhotoRow | null>(null)
  const [rejecting, setRejecting] = useState<PhotoRow | null>(null)
  const [actingKey, setActingKey] = useState<string | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  // Новая загрузка и решение второго администратора должны появляться сами.
  // Пока открыт диалог или идёт решение, список не трогаем.
  useAutoRefresh({
    enabled:
      list.status === 'ready' &&
      !list.requiresSignIn &&
      !requiresSignIn &&
      actingKey === null &&
      rejecting === null &&
      preview === null,
    onRefresh: list.refresh,
  })

  // Смена вкладки или поиска — другая выборка: прежние объявления к ней не относятся.
  // Чистим их в обработчиках, а не эффектом: вкладка и ввод уже меняют состояние.
  function resetSearch() {
    setSearchInput('')
    setSearch('')
    setAnnouncement(null)
    setActionError(null)
  }

  function closePreview() {
    setPreview(null)

    // Фокус возвращается на превью, которое открыло диалог.
    window.requestAnimationFrame(() => previewTriggerRef.current?.focus())
  }

  function closeReject() {
    setRejecting(null)
    window.requestAnimationFrame(() => rejectTriggerRef.current?.focus())
  }

  /**
   * Фокус после исчезновения строки.
   *
   * Кнопка решения исчезает вместе со строкой, поэтому фокус уходит на следующую
   * (или предыдущую) строку, а если очередь опустела — на заголовок экрана.
   */
  function focusAfterRemoval(key: string) {
    const index = list.items.findIndex((row) => rowId(row) === key)
    const next =
      list.items[index + 1] ?? list.items[index - 1] ?? null

    window.requestAnimationFrame(() => {
      if (next === null) {
        headingRef.current?.focus()
        return
      }

      previewRefs.current.get(rowId(next))?.focus()
    })
  }

  function isAlreadyReviewed(cause: unknown): boolean {
    return (
      cause instanceof ApiError &&
      (cause.status === 409 ||
        cause.hasCode('user.avatarModeration.invalidStatus') ||
        cause.hasCode('portfolioPhoto.moderation.invalidStatus'))
    )
  }

  function removeReviewedRow(row: PhotoRow) {
    focusAfterRemoval(rowId(row))
    list.removeItem(rowId(row))
    list.refresh()
  }

  async function handleApprove(row: PhotoRow) {
    if (actingKey !== null) {
      return
    }

    setActingKey(rowId(row))
    setAnnouncement(null)
    setActionError(null)

    try {
      if (row.kind === 'avatar') {
        await approveAvatar(row.photo.userId)
      } else {
        await approvePortfolioPhoto(
          row.photo.barberProfileId,
          row.photo.photoId
        )
      }

      setAnnouncement(t('Фото одобрено.'))
      removeReviewedRow(row)
    } catch (cause) {
      if (isAdminPhotoUnauthorized(cause)) {
        setRequiresSignIn(true)
        return
      }

      setActionError(describePhotoModerationError(cause))

      // `409` значит, что кадр уже разобрали: строка устарела, очередь перечитываем.
      if (isAlreadyReviewed(cause)) {
        removeReviewedRow(row)
      }
    } finally {
      setActingKey(null)
    }
  }

  /**
   * Отказ с причиной. Возвращает текст ошибки для диалога или `null` при успехе.
   *
   * `409` — кадр уже проверил кто-то другой: диалог закрывается, строка исчезает,
   * а объявление просит обновить список (очередь уже перечитана).
   */
  async function handleReject(
    row: PhotoRow,
    reason: string
  ): Promise<string | null> {
    if (actingKey !== null) {
      return null
    }

    setActingKey(rowId(row))
    setAnnouncement(null)
    setActionError(null)

    try {
      if (row.kind === 'avatar') {
        await rejectAvatar(row.photo.userId, reason)
      } else {
        await rejectPortfolioPhoto(
          row.photo.barberProfileId,
          row.photo.photoId,
          reason
        )
      }

      setAnnouncement(t('Фото отклонено.'))
      setRejecting(null)
      removeReviewedRow(row)

      return null
    } catch (cause) {
      if (isAdminPhotoUnauthorized(cause)) {
        setRequiresSignIn(true)
        return null
      }

      const message = describePhotoModerationError(cause)

      if (isAlreadyReviewed(cause)) {
        setActionError(message)
        setRejecting(null)
        removeReviewedRow(row)

        return null
      }

      return message
    } finally {
      setActingKey(null)
    }
  }

  function handleRefresh() {
    setActionError(null)
    list.refresh()
  }

  if (list.requiresSignIn || requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate
          returnTo={photosPath(tab)}
          onSignedIn={() => {
            setRequiresSignIn(false)
            list.reload()
          }}
        />
      </div>
    )
  }

  const isEmpty = list.status === 'ready' && list.items.length === 0
  const showList = list.status === 'ready' && list.items.length > 0

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1
            ref={headingRef}
            tabIndex={-1}
            className="scroll-mt-20 font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
          >
            {t('Фотографии')}
          </h1>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
            {t(
              'Очередь премодерации: новые аватары и работы не видны публично, пока вы их не одобрите. Откройте кадр крупно и примите решение.'
            )}
          </p>
        </div>

        <div className="flex items-center gap-2">
          {list.updatedAt !== null && (
            <span className="text-xs text-muted-foreground tabular-nums">
              {t('Обновлено в {time}', {
                time: formatClockTime(new Date(list.updatedAt)),
              })}
            </span>
          )}
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={list.isRefreshing || list.status === 'loading'}
            onClick={handleRefresh}
          >
            {list.isRefreshing ? (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            ) : (
              <RefreshCw data-icon="inline-start" aria-hidden />
            )}
            {list.isRefreshing ? t('Обновляем…') : t('Обновить')}
          </Button>
        </div>
      </header>

      {list.refreshError && (
        <p role="alert" className="text-sm text-destructive">
          {list.refreshError}
        </p>
      )}

      {announcement && (
        <div
          role="status"
          className="flex flex-wrap items-start justify-between gap-2 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p className="min-w-0 flex-1">{announcement}</p>
          <button
            type="button"
            aria-label={t('Скрыть сообщение')}
            className="inline-flex size-11 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:size-auto sm:p-1"
            onClick={() => setAnnouncement(null)}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        </div>
      )}

      {actionError && (
        <div className="flex flex-wrap items-start justify-between gap-2 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          <p role="alert" className="min-w-0 flex-1">
            {actionError}
          </p>
          <button
            type="button"
            aria-label={t('Скрыть сообщение')}
            className="inline-flex size-11 shrink-0 items-center justify-center rounded-md transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:size-auto sm:p-1"
            onClick={() => setActionError(null)}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        </div>
      )}

      <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center">
        <PhotoTabs
          tab={tab}
          onChange={(next) => {
            setAnnouncement(null)
            setActionError(null)
            navigate(photosPath(next))
          }}
        />

        <div className="relative w-full sm:ml-auto sm:w-64">
          <label htmlFor="photos-search" className="sr-only">
            {t('Поиск по имени или email')}
          </label>
          <Search
            className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            id="photos-search"
            type="search"
            value={searchInput}
            maxLength={ADMIN_PHOTO_SEARCH_MAX_LENGTH}
            placeholder={t('Имя или email')}
            className="h-11 pr-11 pl-8 sm:h-8 sm:pr-9"
            onChange={(event) => {
              setSearchInput(event.target.value)
              setAnnouncement(null)
              setActionError(null)
            }}
          />
          {searchInput !== '' && (
            <button
              type="button"
              aria-label={t('Очистить поиск')}
              className="absolute top-1/2 right-1 inline-flex size-11 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:right-1.5 sm:size-auto sm:p-1"
              onClick={resetSearch}
            >
              <X className="size-3.5" aria-hidden />
            </button>
          )}
        </div>
      </div>

      <div
        id="photos-panel"
        role="tabpanel"
        aria-labelledby={`photos-tab-${tab}`}
        className="space-y-3"
      >
        {list.status === 'loading' && <PhotosSkeleton />}

        {list.status === 'error' && (
          <PhotosError
            message={list.error ?? t('Не удалось загрузить фотографии на проверке.')}
            onRetry={list.reload}
          />
        )}

        {isEmpty && (
          <PhotosEmpty
            search={search}
            onRefresh={list.refresh}
          />
        )}

        {showList && (
          <>
            <div className="flex items-center justify-between gap-2 px-1 text-xs text-muted-foreground">
              <span>
                {t('Показано {count}', { count: list.items.length })}
              </span>
              <span>{t('Сначала старые')}</span>
            </div>

            <section aria-label={t('Очередь фотографий')} className="space-y-3">
              <ul className="divide-y divide-border overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
                {list.items.map((row) => {
                  const key = rowId(row)

                  return (
                    <PhotoRowView
                      key={key}
                      row={row}
                      isActing={actingKey === key}
                      disabled={actingKey !== null}
                      onPreview={(target, trigger) => {
                        previewTriggerRef.current = trigger
                        setPreview(target)
                      }}
                      onApprove={(target) => {
                        void handleApprove(target)
                      }}
                      onReject={(target, trigger) => {
                        rejectTriggerRef.current = trigger
                        setAnnouncement(null)
                        setActionError(null)
                        setRejecting(target)
                      }}
                      previewRef={(element) => {
                        if (element) {
                          previewRefs.current.set(key, element)
                        } else {
                          previewRefs.current.delete(key)
                        }
                      }}
                    />
                  )
                })}
              </ul>

              <ListPager
                label={t('Страницы фотографий')}
                rangeLabel={
                  list.items.length === 0
                    ? ''
                    : formatPageRange(list.offset, list.items.length)
                }
                hasPrev={list.hasPrev}
                hasNext={list.hasNext}
                isLoading={list.isLoadingPage}
                error={list.pageError}
                onPrev={list.goPrev}
                onNext={list.goNext}
                pageIndex={list.pageIndex}
                focusTargetRef={headingRef}
              />
            </section>
          </>
        )}
      </div>

      <Dialog
        open={preview !== null}
        onOpenChange={(open) => {
          if (!open) {
            closePreview()
          }
        }}
      >
        {preview !== null && (
          <DialogContent className="sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{rowName(preview)}</DialogTitle>
              <DialogDescription>{t('Фото на проверке')}</DialogDescription>
            </DialogHeader>
            <img
              src={rowUrl(preview)}
              alt={t('Фото: {name}', { name: rowName(preview) })}
              className="max-h-[70vh] w-full rounded-lg bg-muted object-contain"
            />
          </DialogContent>
        )}
      </Dialog>

      {rejecting !== null && (
        <RejectPhotoDialog
          key={rowId(rejecting)}
          row={rejecting}
          onClose={closeReject}
          onConfirm={(reason) => handleReject(rejecting, reason)}
        />
      )}
    </div>
  )
}

/**
 * Отказ с обязательной причиной.
 *
 * `alertdialog`, потому что действие меняет публичность чужого кадра. Причина
 * проверяется до запроса: пустую строку сервер отклонит, но отправлять её незачем.
 * Ошибка сервера остаётся в диалоге, пока его не закроют, — решение можно повторить.
 */
function RejectPhotoDialog({
  row,
  onClose,
  onConfirm,
}: {
  row: PhotoRow
  onClose: () => void
  /** `null` — успех; строка — русский текст ошибки, диалог остаётся открытым. */
  onConfirm: (reason: string) => Promise<string | null>
}) {
  const { t } = useI18n()
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [serverError, setServerError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const target = rowActionTarget(row)

  async function handleConfirm() {
    if (pending) {
      return
    }

    const validation = validatePhotoRejectionReason(reason)

    if (validation !== null) {
      setReasonError(validation)
      return
    }

    setPending(true)
    setReasonError(null)
    setServerError(null)

    const failure = await onConfirm(reason.trim())

    setPending(false)

    if (failure === null) {
      onClose()
      return
    }

    setServerError(failure)
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        // Пока запрос в полёте, диалог не закрывается: результат должен быть виден.
        if (!open && !pending) {
          onClose()
        }
      }}
    >
      <DialogContent role="alertdialog" className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{t('Отклонить фото?')}</DialogTitle>
          <DialogDescription>{target}</DialogDescription>
        </DialogHeader>

        <p className="text-sm text-muted-foreground">
          {t(
            'Фото останется непубличным. Владелец увидит причину и сможет загрузить другое.'
          )}
        </p>

        <Field data-invalid={Boolean(reasonError)}>
          <FieldLabel htmlFor="photo-rejection-reason">
            {t('Причина отказа')}
          </FieldLabel>
          <Textarea
            id="photo-rejection-reason"
            name="reason"
            rows={3}
            maxLength={PHOTO_REJECTION_MAX_LENGTH}
            value={reason}
            disabled={pending}
            aria-invalid={Boolean(reasonError)}
            aria-describedby={
              reasonError
                ? 'photo-rejection-reason-hint photo-rejection-reason-error'
                : 'photo-rejection-reason-hint'
            }
            onChange={(event) => {
              setReason(event.target.value)
              setReasonError(null)
              setServerError(null)
            }}
          />
          <FieldDescription id="photo-rejection-reason-hint">
            {t(
              'До {max} символов. Причина не редактируется после отказа.',
              { max: PHOTO_REJECTION_MAX_LENGTH }
            )}
          </FieldDescription>
          <FieldError id="photo-rejection-reason-error">
            {reasonError}
          </FieldError>
        </Field>

        {serverError && (
          <p role="alert" className="text-sm text-destructive">
            {serverError}
          </p>
        )}

        <DialogFooter className="flex-col sm:flex-row">
          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-8"
            disabled={pending}
            onClick={onClose}
          >
            {t('Отмена')}
          </Button>
          <Button
            type="button"
            variant="destructive"
            className="h-11 sm:h-8"
            disabled={pending}
            aria-busy={pending}
            onClick={() => {
              void handleConfirm()
            }}
          >
            {pending && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {t('Отклонить')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function PhotosSkeleton() {
  const { t } = useI18n()

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        {t('Загружаем фотографии…')}
      </p>
      <Skeleton className="h-4 w-32" aria-hidden />
      <Skeleton className="h-96 rounded-xl" aria-hidden />
    </div>
  )
}

function PhotosError({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
        <TriangleAlert className="size-4 text-destructive" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">{t('Не удалось загрузить фотографии')}</p>
        <p
          role="alert"
          className="mx-auto max-w-sm text-sm text-muted-foreground"
        >
          {message}
        </p>
      </div>
      <Button
        size="sm"
        variant="outline"
        className="h-11 sm:h-8"
        onClick={onRetry}
      >
        {t('Повторить')}
      </Button>
    </div>
  )
}

/** Пустая очередь и пустой результат поиска — разные причины, поэтому и тексты разные. */
function PhotosEmpty({
  search,
  onRefresh,
}: {
  search: string
  onRefresh: () => void
}) {
  const { t } = useI18n()

  if (search !== '') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Search className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{t('Ничего не найдено')}</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            {t(
              'По запросу «{search}» нет ни имени, ни email. Попробуйте другую строку.',
              { search }
            )}
          </p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <CheckCircle2 className="size-4 text-success" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">
          {t('Очередь пуста — все фото проверены.')}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {t(
            'Новые аватары и работы появятся здесь, как только их загрузят.'
          )}
        </p>
      </div>
      <Button
        size="sm"
        variant="outline"
        className="h-11 sm:h-8"
        onClick={onRefresh}
      >
        {t('Обновить')}
      </Button>
    </div>
  )
}
