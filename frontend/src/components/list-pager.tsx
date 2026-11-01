import { ChevronLeft, ChevronRight, Loader2, TriangleAlert } from 'lucide-react'
import { useEffect, useRef, type RefObject } from 'react'

import { Button } from '@/components/ui/button'

type ListPagerProps = {
  /** Имя навигации для скринридера: «Страницы отзывов». */
  label: string
  /** Подпись диапазона: «11–20 из 32» или «21–40», если общего числа нет. */
  rangeLabel: string
  hasPrev: boolean
  hasNext: boolean
  isLoading: boolean
  /** Ошибка следующей страницы: текущая страница остаётся на экране. */
  error: string | null
  onPrev: () => void
  onNext: () => void
  /** Номер показанной страницы, с нуля: смена двигает фокус на список. */
  pageIndex: number
  /** Куда вернуть фокус после смены страницы. */
  focusTargetRef: RefObject<HTMLElement | null>
  /** Куда прокрутить; по умолчанию — туда же, где оказался фокус. */
  scrollTargetRef?: RefObject<HTMLElement | null>
}

/**
 * Футер постраничного списка: «Назад» / диапазон / «Дальше».
 *
 * Один компонент на все списки. Диапазон живёт здесь, а не в шапке экрана,
 * поэтому подпись не дублируется. Фокус и прокрутка срабатывают только после
 * нажатия кнопки — не на первом рендере и не после смены фильтра.
 */
export function ListPager({
  label,
  rangeLabel,
  hasPrev,
  hasNext,
  isLoading,
  error,
  onPrev,
  onNext,
  pageIndex,
  focusTargetRef,
  scrollTargetRef,
}: ListPagerProps) {
  // Фокус двигаем только по действию пользователя: первый рендер не должен
  // выдёргивать страницу к списку. Ожидаемый номер страницы защищает от прыжка
  // после смены фильтра или вкладки, когда номер меняет не пагинатор.
  const expectedPageIndex = useRef<number | null>(null)
  const previousPageIndex = useRef(pageIndex)

  useEffect(() => {
    if (previousPageIndex.current === pageIndex) {
      return
    }

    previousPageIndex.current = pageIndex

    if (expectedPageIndex.current !== pageIndex) {
      expectedPageIndex.current = null
      return
    }

    expectedPageIndex.current = null

    const target = focusTargetRef.current
    if (target === null) {
      return
    }

    target.focus({ preventScroll: true })
    const scrollTarget = scrollTargetRef?.current ?? target
    scrollTarget.scrollIntoView({
      block: 'start',
      behavior: prefersReducedMotion() ? 'auto' : 'smooth',
    })
  }, [focusTargetRef, pageIndex, scrollTargetRef])

  if (!hasPrev && !hasNext) {
    return null
  }

  const handlePrev = () => {
    expectedPageIndex.current = pageIndex - 1
    onPrev()
  }

  const handleNext = () => {
    expectedPageIndex.current = pageIndex + 1
    onNext()
  }

  return (
    <nav
      aria-label={label}
      aria-busy={isLoading}
      className="space-y-3 border-t pt-4"
    >
      {error !== null && (
        <p
          role="alert"
          className="flex items-start gap-2 text-sm text-destructive"
        >
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
          {error}
        </p>
      )}

      {rangeLabel !== '' && (
        <p aria-live="polite" className="sr-only">
          {`${label}: ${rangeLabel}`}
        </p>
      )}

      <div className="flex items-center justify-between gap-2">
        <Button
          type="button"
          variant="outline"
          // Имя начинается с видимого слова: голосовое управление и диктовка
          // ищут «Назад», а не «Предыдущая страница» (WCAG 2.5.3). Текст скрыт
          // на узком экране через CSS, но имя одно на обе ширины.
          aria-label="Назад — предыдущая страница"
          className="h-11 w-11 px-0 sm:h-9 sm:w-auto sm:px-3"
          disabled={!hasPrev}
          onClick={handlePrev}
        >
          <ChevronLeft data-icon="inline-start" aria-hidden />
          <span className="hidden sm:inline">Назад</span>
        </Button>

        <p className="min-w-0 truncate text-center text-xs text-muted-foreground tabular-nums">
          {rangeLabel}
        </p>

        <Button
          type="button"
          variant="secondary"
          aria-label={
            error === null
              ? 'Дальше — следующая страница'
              : 'Повторить — загрузить следующую страницу'
          }
          aria-busy={isLoading}
          className="h-11 w-11 px-0 sm:h-9 sm:w-auto sm:px-3"
          disabled={!hasNext}
          onClick={handleNext}
        >
          <span className="hidden sm:inline">
            {error === null ? 'Дальше' : 'Повторить'}
          </span>
          {isLoading ? (
            <Loader2
              data-icon="inline-end"
              aria-hidden
              className="animate-spin"
            />
          ) : (
            <ChevronRight data-icon="inline-end" aria-hidden />
          )}
        </Button>
      </div>
    </nav>
  )
}

/** Плавная прокрутка отключается для тех, кто просил меньше движения. */
function prefersReducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
