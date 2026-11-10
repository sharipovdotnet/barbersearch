import { useEffect, useRef, useState } from 'react'
import {
  Inbox,
  Loader2,
  RefreshCw,
  Search,
  TriangleAlert,
  Users,
  X,
} from 'lucide-react'

import { AdminSignInGate } from '@/components/admin/admin-gate'
import {
  BarberDetail,
  BarberDetailPlaceholder,
  BarberDetailState,
} from '@/components/admin/barber-detail'
import { BarberList } from '@/components/admin/barber-list'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useAdminBarber } from '@/hooks/use-admin-barber'
import { useAdminBarberList } from '@/hooks/use-admin-barber-list'
import { useAutoRefresh } from '@/hooks/use-auto-refresh'
import { useMediaQuery } from '@/hooks/use-media-query'
import { t, useI18n } from '@/i18n'
import {
  ADMIN_BARBER_SEARCH_MAX_LENGTH,
  ADMIN_BARBER_STATUS_LABELS,
  BARBER_GRADE_LABELS,
  changeBarberGrade,
  describeAdminBarberActionError,
  hideBarberFromSearch,
  isBarberUnauthorized,
  returnBarberToSearch,
  unverifyBarber,
  verifyBarber,
  type AdminBarber,
  type AdminBarberAction,
  type AdminBarberStatusFilter,
  type BarberGrade,
} from '@/lib/admin-barbers'
import { barbersPath } from '@/lib/admin-path'
import { invalidateBarberProfile } from '@/lib/catalog'
import { formatClockTime } from '@/lib/format'
import {
  blockPortfolioPhoto,
  describePortfolioPhotoError,
  unblockPortfolioPhoto,
} from '@/lib/portfolio-photos'
import { navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

const FILTERS: readonly AdminBarberStatusFilter[] = [
  'pending',
  'all',
  'hidden',
]

type PendingAction = {
  kind: AdminBarberAction
  barberId: string
} | null

type Announcement = {
  text: string
  undo?: {
    label: string
    action: AdminBarberAction
    barberId: string
    barberName: string
  }
}

/**
 * Экран «Барберы»: очередь проверки, поиск по имени и email, карточка профиля.
 *
 * Сетка — split-view на широком экране; на телефоне список и карточка сменяют
 * друг друга, а выбранный профиль и фильтр живут в адресе, поэтому перезагрузка
 * не теряет контекст и карточка не остаётся от чужой выборки.
 *
 * Поиск отправляется с задержкой: сервер ищет по подстроке, и запрос на каждую
 * букву только грел бы лимит. Счётчиков на фильтрах нет — API не отдаёт общее число.
 */
export function BarbersScreen({
  barberId,
  status,
}: {
  barberId: string | null
  status: AdminBarberStatusFilter
}) {
  const { t } = useI18n()
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

  const list = useAdminBarberList({ status, search })
  const detail = useAdminBarber(barberId)
  const isWide = useMediaQuery('(min-width: 1024px)')
  const headingRef = useRef<HTMLHeadingElement | null>(null)

  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [actionFailure, setActionFailure] = useState<{
    barberId: string
    message: string
  } | null>(null)
  const [pendingPhotoId, setPendingPhotoId] = useState<string | null>(null)
  const [photoFailure, setPhotoFailure] = useState<{
    barberId: string
    message: string
  } | null>(null)
  const [announcement, setAnnouncement] = useState<Announcement | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)

  const barber = detail.barber
  const actionError =
    barber && actionFailure?.barberId === barber.id
      ? actionFailure.message
      : null
  const photoError =
    barber && photoFailure?.barberId === barber.id
      ? photoFailure.message
      : null
  const currentPendingAction =
    barber && pendingAction?.barberId === barber.id ? pendingAction.kind : null

  // Очередь проверки меняют и барберы, и второй администратор. Пока идёт
  // действие или догружается страница, список не трогаем.
  useAutoRefresh({
    enabled:
      list.status === 'ready' &&
      !list.requiresSignIn &&
      !list.isLoadingPage &&
      !requiresSignIn &&
      pendingAction === null &&
      pendingPhotoId === null,
    onRefresh: () => {
      list.refresh()

      if (barberId !== null && detail.status !== 'notFound') {
        detail.reload()
      }
    },
  })

  // Применённый поиск меняет выборку, но адреса не касается: карточку прошлого
  // запроса снимаем сами. Смена фильтра уходит в адрес и сбрасывает выбор там.
  const appliedSearch = useRef(search)

  useEffect(() => {
    if (appliedSearch.current === search) {
      return
    }

    appliedSearch.current = search

    if (barberId !== null) {
      navigate(barbersPath(null, status), { replace: true })
    }
  }, [search, barberId, status])

  // На широком экране первая строка открывается сразу: пустая правая колонка
  // выглядит как ошибка загрузки.
  useEffect(() => {
    if (
      !isWide ||
      barberId ||
      list.status !== 'ready' ||
      list.items.length === 0
    ) {
      return
    }

    navigate(barbersPath(list.items[0].id, status), { replace: true })
  }, [isWide, barberId, list.status, list.items, status])

  // На телефоне карточка заменяет список — переход должен начинаться сверху.
  useEffect(() => {
    if (!barberId || isWide) {
      return
    }

    window.scrollTo({ top: 0 })
  }, [barberId, isWide])

  function handleFilterChange(next: AdminBarberStatusFilter) {
    if (next === status) {
      return
    }

    // Поиск сбрасывается вместе с фильтром: иначе «На проверке» с прошлым запросом
    // выглядит как пустая очередь, хотя дело в поиске.
    setSearchInput('')
    setSearch('')

    // Фильтр — часть выборки, а карточка прошлой выборки в неё не входит: переход
    // в адрес без id закрывает её, а на широком экране автовыбор откроет первую
    // строку нового фильтра.
    navigate(barbersPath(null, next))
  }

  function handleSelect(id: string) {
    setActionFailure(null)
    setPhotoFailure(null)
    navigate(barbersPath(id, status))
  }

  async function runAction(
    kind: AdminBarberAction,
    target: { id: string; displayName: string },
    grade?: BarberGrade
  ) {
    if (pendingAction) {
      return
    }

    setPendingAction({ kind, barberId: target.id })
    setActionFailure(null)

    try {
      switch (kind) {
        case 'verify':
          await verifyBarber(target.id)
          break
        case 'unverify':
          await unverifyBarber(target.id)
          break
        case 'grade':
          if (grade) {
            await changeBarberGrade(target.id, grade)
          }
          break
        case 'hide':
          await hideBarberFromSearch(target.id)
          break
        case 'show':
          await returnBarberToSearch(target.id)
          break
      }

      setAnnouncement(announcementFor(kind, target, grade))

      // Сервер — источник правды для карточки: рейтинг, статусы и чек-лист
      // пересчитываются на его стороне. Строку списка правим локально сразу,
      // поэтому результат действия виден, даже если тихое обновление не дойдёт.
      detail.reload()

      const row = list.items.find((item) => item.id === target.id)

      if (row) {
        const next = updatedBarberRow(row, kind, grade)

        if (matchesBarberFilter(next, status)) {
          list.replaceItem(next.id, next)
        } else {
          list.removeItem(next.id)
        }
      }

      // Прежнее тихое обновление списка сохранено: «Обновлено в HH:MM» и
      // остальные строки остаются серверной правдой, а карточка не сбрасывается.
      list.refresh()
    } catch (cause) {
      if (isBarberUnauthorized(cause)) {
        setRequiresSignIn(true)
        return
      }

      setActionFailure({
        barberId: target.id,
        message: describeAdminBarberActionError(cause, kind),
      })
    } finally {
      setPendingAction(null)
    }
  }

  /**
   * Блокировка кадра: админ скрывает фотографию с публичной карточки и из раздачи.
   *
   * После `204` карточка перечитывается — сервер остаётся источником правды для
   * `isBlocked`; открытая карточка при этом не теряется (`useAdminBarber.reload`
   * сохраняет прежние данные до ответа). Кэш публичной карточки сбрасывается, иначе
   * «Открыть, как видят клиенты» показал бы старый кадр.
   */
  async function runPhotoAction(
    kind: 'block' | 'unblock',
    photoId: string
  ) {
    if (!barber || pendingPhotoId !== null) {
      return
    }

    const barberIdOfCard = barber.id

    setPendingPhotoId(photoId)
    setPhotoFailure(null)

    try {
      if (kind === 'block') {
        await blockPortfolioPhoto(barberIdOfCard, photoId)
      } else {
        await unblockPortfolioPhoto(barberIdOfCard, photoId)
      }

      invalidateBarberProfile(barberIdOfCard)
      setAnnouncement({
        text:
          kind === 'block'
            ? t('Фото скрыто: его больше нет на публичной карточке.')
            : t('Фото снова на публичной карточке.'),
      })
      detail.reload()
    } catch (cause) {
      if (isBarberUnauthorized(cause)) {
        setRequiresSignIn(true)
        return
      }

      setPhotoFailure({
        barberId: barberIdOfCard,
        message: describePortfolioPhotoError(cause).message,
      })
    } finally {
      setPendingPhotoId(null)
    }
  }

  async function handleUndo(entry: Announcement) {
    if (!entry.undo || pendingAction) {
      return
    }

    setAnnouncement(null)

    await runAction(entry.undo.action, {
      id: entry.undo.barberId,
      displayName: entry.undo.barberName,
    })
  }

  function handleBack() {
    navigate(barbersPath(null, status))

    // Фокус возвращается на заголовок, а не в <body>: на телефоне списка ещё нет,
    // пока страница не перерисовалась.
    window.requestAnimationFrame(() => {
      window.requestAnimationFrame(() => headingRef.current?.focus())
    })
  }

  const trimmedSearch = searchInput.trim()
  // Большой пустой блок — состояние «ничего не выбрано»: когда карточка открыта,
  // он дублировал бы подпись пустой страницы и показывал два пустых состояния сразу.
  const isEmpty =
    list.status === 'ready' && list.items.length === 0 && barberId === null
  const isMobileDetail = !isWide && barberId !== null
  const showGrid =
    list.status === 'ready' && (list.items.length > 0 || barberId !== null)

  if (list.requiresSignIn || requiresSignIn) {
    return (
      <div className="mx-auto w-full max-w-md py-6">
        <AdminSignInGate
          returnTo={barbersPath(barberId, status)}
          onSignedIn={() => {
            setRequiresSignIn(false)
            list.reload()
            detail.reload()
          }}
        />
      </div>
    )
  }

  return (
    <div className="space-y-5">
      {isMobileDetail && (
        <h1 ref={headingRef} tabIndex={-1} className="sr-only">
          {t('Барберы')}
        </h1>
      )}

      {!isMobileDetail && (
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1
              ref={headingRef}
              tabIndex={-1}
              className="scroll-mt-20 font-heading text-2xl font-semibold tracking-tight sm:text-3xl"
            >
              {t('Барберы')}
            </h1>
            <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
              {t(
                'Подтвердите профиль, присвойте грейд или уберите барбера из поиска. Пока профиль не подтверждён, он не появляется в выдаче.'
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
              className="h-11 sm:h-7"
              disabled={list.isRefreshing || list.status === 'loading'}
              onClick={list.refresh}
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
      )}

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
          <p className="min-w-0 flex-1">{announcement.text}</p>
          <div className="flex items-center gap-1">
            {announcement.undo && (
              <Button
                variant="outline"
                size="sm"
                className="h-11 sm:h-7"
                disabled={pendingAction !== null}
                onClick={() => {
                  void handleUndo(announcement)
                }}
              >
                {announcement.undo.label}
              </Button>
            )}
            <button
              type="button"
              aria-label={t('Скрыть сообщение')}
              className="rounded-md p-1 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
              onClick={() => setAnnouncement(null)}
            >
              <X className="size-3.5" aria-hidden />
            </button>
          </div>
        </div>
      )}

      {!isMobileDetail && (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div
            role="group"
            aria-label={t('Каких барберов показать')}
            className="inline-flex items-center gap-0.5 self-start rounded-lg bg-muted p-0.5"
          >
            {FILTERS.map((item) => {
              const isSelected = item === status

              return (
                <button
                  key={item}
                  type="button"
                  aria-pressed={isSelected}
                  onClick={() => handleFilterChange(item)}
                  className={cn(
                    'inline-flex h-11 items-center rounded-md px-3 text-sm transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-7',
                    isSelected
                      ? 'bg-background font-medium text-foreground shadow-sm ring-1 ring-foreground/10'
                      : 'text-muted-foreground hover:text-foreground'
                  )}
                >
                  {t(ADMIN_BARBER_STATUS_LABELS[item])}
                </button>
              )
            })}
          </div>

          <div className="relative w-full sm:max-w-xs">
            <label htmlFor="barbers-search" className="sr-only">
              {t('Поиск по имени или email')}
            </label>
            <Search
              className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden
            />
            <Input
              id="barbers-search"
              type="search"
              value={searchInput}
              maxLength={ADMIN_BARBER_SEARCH_MAX_LENGTH}
              placeholder={t('Имя или email')}
              className="h-11 pr-9 pl-8 sm:h-8"
              onChange={(event) => setSearchInput(event.target.value)}
            />
            {searchInput !== '' && (
              <button
                type="button"
                aria-label={t('Очистить поиск')}
                className="absolute top-1/2 right-1.5 -translate-y-1/2 rounded-md p-1 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
                onClick={() => {
                  setSearchInput('')
                  setSearch('')
                }}
              >
                <X className="size-3.5" aria-hidden />
              </button>
            )}
          </div>
        </div>
      )}

      <p role="status" className="sr-only">
        {list.status === 'ready'
          ? t('Показано {count}', { count: list.items.length })
          : ''}
      </p>

      {list.status === 'loading' && <BarbersSkeleton />}

      {list.status === 'error' && (
        <BarbersError
          message={list.error ?? t('Не удалось загрузить барберов.')}
          onRetry={list.reload}
        />
      )}

      {isEmpty && (
        <BarbersEmpty
          status={status}
          search={trimmedSearch}
          onRefresh={list.refresh}
        />
      )}

      {showGrid && (
        <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
          <div
            className={cn('min-w-0', barberId ? 'hidden lg:block' : 'block')}
          >
            {list.items.length > 0 ? (
              <BarberList
                items={list.items}
                status={status}
                selectedId={barberId}
                offset={list.offset}
                pageIndex={list.pageIndex}
                hasPrev={list.hasPrev}
                hasNext={list.hasNext}
                isLoadingPage={list.isLoadingPage}
                pageError={list.pageError}
                focusTargetRef={headingRef}
                onSelect={handleSelect}
                onPrev={list.goPrev}
                onNext={list.goNext}
              />
            ) : (
              <p className="text-sm text-muted-foreground">
                {t('В загруженной странице нет барберов.')}
              </p>
            )}
          </div>

          <div
            className={cn('min-w-0', barberId ? 'block' : 'hidden lg:block')}
          >
            {barber ? (
              <BarberDetail
                barber={barber}
                pendingAction={currentPendingAction}
                actionError={actionError}
                onVerify={() => {
                  void runAction('verify', barber)
                }}
                onUnverify={() => {
                  void runAction('unverify', barber)
                }}
                onChangeGrade={(grade) => {
                  void runAction('grade', barber, grade)
                }}
                onHide={() => {
                  void runAction('hide', barber)
                }}
                onShow={() => {
                  void runAction('show', barber)
                }}
                pendingPhotoId={pendingPhotoId}
                photoError={photoError}
                onBlockPhoto={(photoId) => {
                  void runPhotoAction('block', photoId)
                }}
                onUnblockPhoto={(photoId) => {
                  void runPhotoAction('unblock', photoId)
                }}
                onBack={handleBack}
              />
            ) : barberId ? (
              <BarberDetailState
                status={detail.status}
                error={detail.error}
                onRetry={detail.reload}
                onBack={handleBack}
              />
            ) : (
              <BarberDetailPlaceholder />
            )}
          </div>
        </div>
      )}
    </div>
  )
}

/**
 * Текст объявления после действия. Подтверждение, скрытие и грейд обратимы,
 * поэтому у каждого действия есть подсказка, как вернуть прежнее состояние.
 */
function announcementFor(
  kind: AdminBarberAction,
  target: { id: string; displayName: string },
  grade?: BarberGrade
): Announcement {
  const name = target.displayName

  switch (kind) {
    case 'verify':
      return {
        text: t('Профиль «{name}» подтверждён и появился в поиске.', { name }),
        undo: {
          label: t('Снять подтверждение'),
          action: 'unverify',
          barberId: target.id,
          barberName: name,
        },
      }
    case 'unverify':
      return {
        text: t('Подтверждение снято: профиль «{name}» больше не в поиске.', {
          name,
        }),
        undo: {
          label: t('Подтвердить снова'),
          action: 'verify',
          barberId: target.id,
          barberName: name,
        },
      }
    case 'hide':
      return {
        text: t('Профиль «{name}» скрыт из поиска.', { name }),
        undo: {
          label: t('Вернуть в поиск'),
          action: 'show',
          barberId: target.id,
          barberName: name,
        },
      }
    case 'show':
      return {
        text: t('Профиль «{name}» снова в поиске.', { name }),
        undo: {
          label: t('Скрыть из поиска'),
          action: 'hide',
          barberId: target.id,
          barberName: name,
        },
      }
    case 'grade':
      return {
        text: grade
          ? t('Грейд изменён: «{grade}».', {
              grade: t(BARBER_GRADE_LABELS[grade]),
            })
          : t('Грейд изменён.'),
      }
  }
}

/**
 * Строка после действия: команда меняет только своё поле, остальное берётся
 * из уже загруженной страницы. Локальная правка видна сразу, не дожидаясь
 * тихого обновления списка.
 */
function updatedBarberRow(
  barber: AdminBarber,
  kind: AdminBarberAction,
  grade?: BarberGrade
): AdminBarber {
  switch (kind) {
    case 'verify':
      return { ...barber, isVerified: true }
    case 'unverify':
      return { ...barber, isVerified: false }
    case 'grade':
      return grade ? { ...barber, grade } : barber
    case 'hide':
      return { ...barber, isActive: false }
    case 'show':
      return { ...barber, isActive: true }
  }
}

/**
 * Подходит ли строка под выбранный фильтр.
 *
 * Сервер понимает «На проверке» как `!isVerified`, а «Скрытые» как `!isActive`
 * (`BuildAdminBarbersQuery`), поэтому скрытый неподтверждённый профиль остаётся
 * в очереди проверки — ровно так его вернёт и повторный запрос.
 */
function matchesBarberFilter(
  barber: AdminBarber,
  filter: AdminBarberStatusFilter
): boolean {
  if (filter === 'pending') {
    return !barber.isVerified
  }

  if (filter === 'hidden') {
    return !barber.isActive
  }

  return true
}

function BarbersSkeleton() {
  const { t } = useI18n()

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        {t('Загружаем барберов…')}
      </p>
      <Skeleton className="h-4 w-32" aria-hidden />
      <Skeleton className="h-96 rounded-xl" aria-hidden />
    </div>
  )
}

function BarbersError({
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
        <p className="font-medium">{t('Не удалось загрузить барберов')}</p>
        <p role="alert" className="mx-auto max-w-sm text-sm text-muted-foreground">
          {message}
        </p>
      </div>
      <Button
        size="sm"
        variant="outline"
        className="h-11 sm:h-7"
        onClick={onRetry}
      >
        {t('Повторить')}
      </Button>
    </div>
  )
}

function BarbersEmpty({
  status,
  search,
  onRefresh,
}: {
  status: AdminBarberStatusFilter
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

  if (status === 'pending') {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Users className="size-4 text-success" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">{t('Очередь проверки пуста')}</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            {t(
              'Все профили подтверждены. Новые появятся здесь, когда барберы зарегистрируются.'
            )}
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onRefresh}
        >
          {t('Обновить')}
        </Button>
      </div>
    )
  }

  return (
    <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Inbox className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">
          {status === 'hidden'
            ? t('Скрытых профилей нет')
            : t('Барберов пока нет')}
        </p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          {status === 'hidden'
            ? t(
                'Никто не убран из поиска: все профили либо подтверждены, либо ждут проверки.'
              )
            : t(
                'Профили появятся здесь, как только барберы их создадут.'
              )}
        </p>
      </div>
    </div>
  )
}
