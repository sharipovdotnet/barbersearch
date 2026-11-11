import { useState } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { useI18n } from '@/i18n'
import { portfolioPhotoSource } from '@/lib/media'
import type { PortfolioPhoto } from '@/lib/portfolio-photos'

/**
 * Публичная галерея работ на `/barbers/{id}` (§5.4).
 *
 * Секция живёт между hero и сеткой слотов и исчезает целиком при пустом портфолио:
 * пустая карточка «Работы» хуже, чем её отсутствие. Сетка — 2 колонки уже на 320 px,
 * фиксированный `aspect-square`/`object-cover` держит нулевой CLS, а первое фото
 * грузится жадно (`eager`, `high`) — это самая крупная картинка первого экрана.
 *
 * Лайтбокс — `Dialog` с подписью, счётчиком и стрелками: `ArrowLeft`/`ArrowRight`
 * листают, `Escape` закрывает (фокус-ловушка и возврат фокуса — на стороне Base UI).
 */

export function BarberPortfolio({
  photos,
  barberName,
}: {
  photos: readonly PortfolioPhoto[]
  barberName: string
}) {
  const { t } = useI18n()
  const [openIndex, setOpenIndex] = useState<number | null>(null)
  const [failedIds, setFailedIds] = useState<readonly string[]>([])

  // Пустое портфолио — отсутствие секции, а не пустая карточка.
  if (photos.length === 0) {
    return null
  }

  const failed = new Set(failedIds)

  function markFailed(photoId: string): void {
    setFailedIds((current) =>
      current.includes(photoId) ? current : [...current, photoId]
    )
  }

  function photoAlt(photo: PortfolioPhoto, index: number): string {
    const caption = photo.description?.trim() ?? ''

    return caption === ''
      ? t('Работа барбера {name}, фото {number}', {
          name: barberName,
          number: index + 1,
        })
      : caption
  }

  const current = openIndex === null ? null : photos[openIndex] ?? null
  const total = photos.length

  function goTo(index: number): void {
    if (index < 0 || index >= total) {
      return
    }

    setOpenIndex(index)
  }

  return (
    <section aria-labelledby="barber-portfolio-heading">
      <h2
        id="barber-portfolio-heading"
        className="font-heading text-base leading-snug font-medium tracking-tight"
      >
        {t('Работы')}
      </h2>

      <ul className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
        {photos.map((photo, index) => {
          const alt = photoAlt(photo, index)
          const isFailed = failed.has(photo.id)

          return (
            <li key={photo.id}>
              <button
                type="button"
                aria-label={t('Открыть фото: {caption}', { caption: alt })}
                className="block w-full overflow-hidden rounded-lg border transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 hover:opacity-90"
                onClick={() => {
                  setOpenIndex(index)
                }}
              >
                {isFailed ? (
                  <span
                    role="img"
                    aria-label={alt}
                    className="block aspect-square w-full bg-muted"
                  />
                ) : (
                  <img
                    {...portfolioPhotoSource(photo.url)}
                    alt={alt}
                    loading={index === 0 ? 'eager' : 'lazy'}
                    fetchPriority={index === 0 ? 'high' : undefined}
                    decoding="async"
                    className="aspect-square w-full object-cover"
                    onError={() => {
                      markFailed(photo.id)
                    }}
                  />
                )}
              </button>
            </li>
          )
        })}
      </ul>

      <Dialog
        open={openIndex !== null}
        onOpenChange={(next) => {
          if (!next) {
            setOpenIndex(null)
          }
        }}
      >
        {current !== null && openIndex !== null && (
          <DialogContent
            className="sm:max-w-3xl"
            onKeyDown={(event) => {
              if (event.key === 'ArrowLeft') {
                event.preventDefault()
                goTo(openIndex - 1)
              } else if (event.key === 'ArrowRight') {
                event.preventDefault()
                goTo(openIndex + 1)
              }
            }}
          >
            <DialogHeader>
              <DialogTitle className="sr-only">{t('Фото барбера')}</DialogTitle>
              <DialogDescription className="sr-only">
                {t('Просмотр работ барбера')}
              </DialogDescription>
            </DialogHeader>

            <div className="space-y-3">
              <div className="flex items-center justify-center">
                {failed.has(current.id) ? (
                  <div
                    role="img"
                    aria-label={photoAlt(current, openIndex)}
                    className="flex max-h-[75vh] min-h-48 w-full items-center justify-center rounded-lg bg-muted p-6 text-center text-sm text-muted-foreground"
                  >
                    {photoAlt(current, openIndex)}
                  </div>
                ) : (
                  <img
                    src={current.url}
                    alt={photoAlt(current, openIndex)}
                    className="max-h-[75vh] w-full rounded-lg object-contain"
                    onError={() => {
                      markFailed(current.id)
                    }}
                  />
                )}
              </div>

              <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                {current.description ? (
                  <p className="min-w-0 text-sm break-words">
                    {current.description}
                  </p>
                ) : (
                  <span />
                )}
                <p className="text-xs text-muted-foreground tabular-nums">
                  {t('{current} из {total}', {
                    current: openIndex + 1,
                    total,
                  })}
                </p>
              </div>

              <div className="flex items-center justify-between gap-2">
                <Button
                  type="button"
                  variant="outline"
                  className="h-11 sm:h-8"
                  aria-label={t('Назад — фото {number} из {total}', {
                    number: Math.max(1, openIndex),
                    total,
                  })}
                  disabled={openIndex === 0}
                  onClick={() => {
                    goTo(openIndex - 1)
                  }}
                >
                  {t('Назад')}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  className="h-11 sm:h-8"
                  aria-label={t('Дальше — фото {number} из {total}', {
                    number: Math.min(total, openIndex + 2),
                    total,
                  })}
                  disabled={openIndex === total - 1}
                  onClick={() => {
                    goTo(openIndex + 1)
                  }}
                >
                  {t('Дальше')}
                </Button>
              </div>
            </div>
          </DialogContent>
        )}
      </Dialog>
    </section>
  )
}
