import { useEffect, useRef, useState } from 'react'
import { Clock, Loader2 } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardHeader,
} from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useI18n } from '@/i18n'
import {
  addBarberService,
  describeBarberProfileError,
  removeBarberService,
  updateBarberService,
} from '@/lib/barber-profile'
import { SERVICE_CATEGORY_LABELS } from '@/lib/barber-profile-labels'
import type { BarberProfileService } from '@/lib/catalog'
import { describeApiFailure } from '@/lib/errors'
import { formatDuration, formatPrice } from '@/lib/format'
import { fetchHairServices, type HairServiceOption } from '@/lib/hair-services'

/**
 * Прайс барбера в своём кабинете.
 *
 * «Удаление» услуги — деактивация (§3.4): строка остаётся в БД, чтобы внутренние
 * join'ы броней не теряли записи. Поэтому барбер видит скрытую услугу с бейджем
 * «Скрыта» и может вернуть её тем же `POST`, который перезапишет цену и длительность.
 *
 * Справочник услуг ленивый: он запрашивается при первом открытии диалога добавления
 * и никогда — при монтировании страницы. Ошибка справочника живёт внутри диалога и
 * не прячет уже загруженный прайс.
 */

type ServiceDialog =
  | { kind: 'add' }
  | { kind: 'price'; service: BarberProfileService }
  | { kind: 'hide'; service: BarberProfileService }

type CatalogueStatus = 'idle' | 'loading' | 'ready' | 'error'

export function BarberProfileServices({
  barberProfileId,
  services,
  onChanged,
}: {
  barberProfileId: string
  services: readonly BarberProfileService[]
  onChanged: (message: string) => void
}) {
  const { t } = useI18n()
  const [dialog, setDialog] = useState<ServiceDialog | null>(null)
  const [catalogueStatus, setCatalogueStatus] =
    useState<CatalogueStatus>('idle')
  const [catalogue, setCatalogue] = useState<HairServiceOption[]>([])
  const [catalogueError, setCatalogueError] = useState<string | null>(null)
  const [restoringId, setRestoringId] = useState<string | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  const catalogueVersion = useRef(0)

  useEffect(() => {
    return () => {
      // Поздний ответ справочника после закрытия страницы не трогает состояние.
      catalogueVersion.current += 1
    }
  }, [])

  async function loadCatalogue(): Promise<void> {
    const version = catalogueVersion.current + 1
    catalogueVersion.current = version
    setCatalogueStatus('loading')
    setCatalogueError(null)

    try {
      const items = await fetchHairServices()

      if (catalogueVersion.current !== version) {
        return
      }

      setCatalogue(items)
      setCatalogueStatus('ready')
    } catch (cause) {
      if (catalogueVersion.current !== version) {
        return
      }

      setCatalogueError(
        describeApiFailure(cause, t('Не удалось загрузить каталог услуг.'))
      )
      setCatalogueStatus('error')
    }
  }

  function openAddDialog(): void {
    setListError(null)
    setDialog({ kind: 'add' })

    // Первый запрос — при открытии диалога. После ошибки повторяет только кнопка.
    if (catalogueStatus === 'idle') {
      void loadCatalogue()
    }
  }

  function closeDialog(): void {
    setDialog(null)
  }

  async function handleRestore(service: BarberProfileService): Promise<void> {
    if (restoringId !== null) {
      return
    }

    setRestoringId(service.barberServiceId)
    setListError(null)

    try {
      // Тот же POST реактивирует строку и перезапишет цену и длительность.
      await addBarberService(barberProfileId, {
        hairServiceId: service.hairServiceId,
        price: service.price,
        durationMinutes: service.durationMinutes,
      })
      onChanged(t('Услуга снова в прайсе.'))
    } catch (cause) {
      setListError(describeBarberProfileError(cause).message)
    } finally {
      setRestoringId(null)
    }
  }

  const available = catalogue.filter(
    (option) =>
      !services.some((service) => service.hairServiceId === option.id)
  )

  return (
    <section aria-labelledby="barber-profile-services-heading">
      <Card>
        <CardHeader className="flex flex-row items-start justify-between gap-3">
          <h2
            id="barber-profile-services-heading"
            className="font-heading text-base leading-snug font-medium tracking-tight"
          >
            {t('Услуги')}
          </h2>
          <Button
            type="button"
            size="sm"
            className="h-11 shrink-0 sm:h-8"
            onClick={openAddDialog}
          >
            {t('Добавить услугу')}
          </Button>
        </CardHeader>

        <CardContent className="space-y-4">
          {services.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {t(
                'Услуг нет. Пока услуги не добавлены, вас не найдут по фильтру услуг и нельзя записаться.'
              )}
            </p>
          ) : (
            <ul aria-label={t('Услуги')} className="divide-y">
              {services.map((service) => (
                <ServiceRow
                  key={service.barberServiceId}
                  service={service}
                  isRestoring={restoringId === service.barberServiceId}
                  onEditPrice={() => {
                    setDialog({ kind: 'price', service })
                  }}
                  onHide={() => {
                    setDialog({ kind: 'hide', service })
                  }}
                  onRestore={() => {
                    void handleRestore(service)
                  }}
                />
              ))}
            </ul>
          )}

          {listError && (
            <p role="alert" className="text-sm text-destructive">
              {listError}
            </p>
          )}
        </CardContent>
      </Card>

      <Dialog
        open={dialog !== null}
        onOpenChange={(next) => {
          if (!next) {
            closeDialog()
          }
        }}
      >
        {dialog?.kind === 'add' && (
          <DialogContent>
            <AddServiceDialog
              barberProfileId={barberProfileId}
              available={available}
              status={catalogueStatus}
              error={catalogueError}
              onRetry={() => {
                void loadCatalogue()
              }}
              onClose={closeDialog}
              onAdded={() => {
                onChanged(t('Услуга добавлена.'))
                closeDialog()
              }}
            />
          </DialogContent>
        )}

        {dialog?.kind === 'price' && (
          <DialogContent>
            <PriceDialog
              barberProfileId={barberProfileId}
              service={dialog.service}
              onClose={closeDialog}
              onSaved={() => {
                onChanged(t('Цена обновлена.'))
                closeDialog()
              }}
            />
          </DialogContent>
        )}

        {dialog?.kind === 'hide' && (
          <DialogContent role="alertdialog">
            <HideServiceDialog
              barberProfileId={barberProfileId}
              service={dialog.service}
              onClose={closeDialog}
              onHidden={() => {
                onChanged(t('Услуга скрыта.'))
                closeDialog()
              }}
            />
          </DialogContent>
        )}
      </Dialog>
    </section>
  )
}

function ServiceRow({
  service,
  isRestoring,
  onEditPrice,
  onHide,
  onRestore,
}: {
  service: BarberProfileService
  isRestoring: boolean
  onEditPrice: () => void
  onHide: () => void
  onRestore: () => void
}) {
  const { t } = useI18n()
  const duration = formatDuration(service.durationMinutes)

  return (
    <li className="flex flex-col gap-3 py-3 first:pt-0 last:pb-0 sm:flex-row sm:items-center sm:justify-between sm:gap-6">
      <div className="min-w-0 space-y-1.5">
        <p className="font-medium break-words">{service.name}</p>
        <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <Badge variant="outline">
            {t(SERVICE_CATEGORY_LABELS[service.category])}
          </Badge>
          {duration !== '' && (
            <span className="inline-flex items-center gap-1">
              <Clock className="size-3.5" aria-hidden />
              {duration}
            </span>
          )}
          <span className="tabular-nums">{formatPrice(service.price)}</span>
          {!service.isActive && (
            <Badge variant="secondary">{t('Скрыта')}</Badge>
          )}
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {service.isActive ? (
          <>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              onClick={onEditPrice}
            >
              {t('Изменить цену')}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-11 text-destructive hover:text-destructive sm:h-8"
              onClick={onHide}
            >
              {t('Скрыть')}
            </Button>
          </>
        ) : (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            disabled={isRestoring}
            aria-busy={isRestoring}
            onClick={onRestore}
          >
            {isRestoring && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {t('Вернуть')}
          </Button>
        )}
      </div>
    </li>
  )
}

function AddServiceDialog({
  barberProfileId,
  available,
  status,
  error,
  onRetry,
  onClose,
  onAdded,
}: {
  barberProfileId: string
  available: readonly HairServiceOption[]
  status: CatalogueStatus
  error: string | null
  onRetry: () => void
  onClose: () => void
  onAdded: () => void
}) {
  const { t } = useI18n()
  const [hairServiceId, setHairServiceId] = useState('')
  const [price, setPrice] = useState('')
  const [duration, setDuration] = useState('')
  const [isPending, setIsPending] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const parsedPrice = Number(price)
  const parsedDuration = Number(duration)
  const isPriceValid = price.trim() !== '' && Number.isFinite(parsedPrice) && parsedPrice >= 0
  const isDurationValid =
    Number.isInteger(parsedDuration) &&
    parsedDuration >= 1 &&
    parsedDuration <= 1440
  const canSubmit =
    hairServiceId !== '' && isPriceValid && isDurationValid && !isPending

  async function handleSubmit(): Promise<void> {
    if (!canSubmit) {
      return
    }

    setIsPending(true)
    setSubmitError(null)

    try {
      await addBarberService(barberProfileId, {
        hairServiceId,
        price: parsedPrice,
        durationMinutes: parsedDuration,
      })
      onAdded()
    } catch (cause) {
      setSubmitError(describeBarberProfileError(cause).message)
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t('Добавить услугу')}</DialogTitle>
        <DialogDescription>
          {t(
            'Выберите услугу из справочника и задайте свою цену и длительность.'
          )}
        </DialogDescription>
      </DialogHeader>

      {status === 'loading' || status === 'idle' ? (
        <p
          role="status"
          className="flex items-center gap-2 text-sm text-muted-foreground"
        >
          <Loader2 className="size-4 animate-spin" aria-hidden />
          {t('Загружаем каталог…')}
        </p>
      ) : status === 'error' ? (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={onRetry}
          >
            {t('Повторить')}
          </Button>
        </div>
      ) : available.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {t('Все услуги из справочника уже есть в прайсе.')}
        </p>
      ) : (
        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label htmlFor="barber-profile-service-hair-service">
              {t('Услуга')}
            </Label>
            <select
              id="barber-profile-service-hair-service"
              value={hairServiceId}
              className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8"
              onChange={(event) => {
                const nextId = event.target.value
                const option = available.find((item) => item.id === nextId)

                setHairServiceId(nextId)
                setDuration(
                  option ? String(option.defaultDurationMinutes) : ''
                )
                setSubmitError(null)
              }}
            >
              <option value="" disabled>
                {t('Выберите услугу')}
              </option>
              {available.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.name}
                </option>
              ))}
            </select>
          </div>

          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="barber-profile-service-price">
                {t('Цена, ₽')}
              </Label>
              <Input
                id="barber-profile-service-price"
                type="number"
                min={0}
                step="0.01"
                inputMode="decimal"
                value={price}
                className="h-11 sm:h-8"
                onChange={(event) => {
                  setPrice(event.target.value)
                  setSubmitError(null)
                }}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="barber-profile-service-duration">
                {t('Длительность, мин')}
              </Label>
              <Input
                id="barber-profile-service-duration"
                type="number"
                min={1}
                max={1440}
                inputMode="numeric"
                value={duration}
                className="h-11 sm:h-8"
                onChange={(event) => {
                  setDuration(event.target.value)
                  setSubmitError(null)
                }}
              />
            </div>
          </div>
        </div>
      )}

      {submitError && (
        <p role="alert" className="text-sm text-destructive">
          {submitError}
        </p>
      )}

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          onClick={onClose}
          disabled={isPending}
        >
          {t('Отмена')}
        </Button>
        <Button
          type="button"
          className="h-11 sm:h-8"
          disabled={!canSubmit}
          aria-busy={isPending}
          onClick={() => {
            void handleSubmit()
          }}
        >
          {isPending && (
            <Loader2
              data-icon="inline-start"
              aria-hidden
              className="animate-spin"
            />
          )}
          {t('Добавить')}
        </Button>
      </DialogFooter>
    </>
  )
}

function PriceDialog({
  barberProfileId,
  service,
  onClose,
  onSaved,
}: {
  barberProfileId: string
  service: BarberProfileService
  onClose: () => void
  onSaved: () => void
}) {
  const { t } = useI18n()
  const [price, setPrice] = useState(String(service.price))
  const [duration, setDuration] = useState(String(service.durationMinutes))
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const parsedPrice = Number(price)
  const parsedDuration = Number(duration)
  const isPriceValid = price.trim() !== '' && Number.isFinite(parsedPrice) && parsedPrice >= 0
  const isDurationValid =
    Number.isInteger(parsedDuration) &&
    parsedDuration >= 1 &&
    parsedDuration <= 1440
  const canSubmit = isPriceValid && isDurationValid && !isPending

  async function handleSubmit(): Promise<void> {
    if (!canSubmit) {
      return
    }

    setIsPending(true)
    setError(null)

    try {
      await updateBarberService(barberProfileId, service.hairServiceId, {
        price: parsedPrice,
        durationMinutes: parsedDuration,
      })
      onSaved()
    } catch (cause) {
      setError(describeBarberProfileError(cause).message)
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t('Изменить цену')}</DialogTitle>
        <DialogDescription>{service.name}</DialogDescription>
      </DialogHeader>

      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label htmlFor="barber-profile-price">{t('Цена, ₽')}</Label>
          <Input
            id="barber-profile-price"
            type="number"
            min={0}
            step="0.01"
            inputMode="decimal"
            value={price}
            className="h-11 sm:h-8"
            onChange={(event) => {
              setPrice(event.target.value)
              setError(null)
            }}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="barber-profile-duration">
            {t('Длительность, мин')}
          </Label>
          <Input
            id="barber-profile-duration"
            type="number"
            min={1}
            max={1440}
            inputMode="numeric"
            value={duration}
            className="h-11 sm:h-8"
            onChange={(event) => {
              setDuration(event.target.value)
              setError(null)
            }}
          />
        </div>
      </div>

      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          onClick={onClose}
          disabled={isPending}
        >
          {t('Отмена')}
        </Button>
        <Button
          type="button"
          className="h-11 sm:h-8"
          disabled={!canSubmit}
          aria-busy={isPending}
          onClick={() => {
            void handleSubmit()
          }}
        >
          {isPending && (
            <Loader2
              data-icon="inline-start"
              aria-hidden
              className="animate-spin"
            />
          )}
          {t('Сохранить')}
        </Button>
      </DialogFooter>
    </>
  )
}

function HideServiceDialog({
  barberProfileId,
  service,
  onClose,
  onHidden,
}: {
  barberProfileId: string
  service: BarberProfileService
  onClose: () => void
  onHidden: () => void
}) {
  const { t } = useI18n()
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleConfirm(): Promise<void> {
    if (isPending) {
      return
    }

    setIsPending(true)
    setError(null)

    try {
      await removeBarberService(barberProfileId, service.hairServiceId)
      onHidden()
    } catch (cause) {
      setError(describeBarberProfileError(cause).message)
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t('Скрыть услугу?')}</DialogTitle>
        <DialogDescription>
          {t(
            'Услуга исчезнет из карточки и поиска. Записи, где она была, останутся в истории.'
          )}
        </DialogDescription>
      </DialogHeader>

      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          onClick={onClose}
          disabled={isPending}
        >
          {t('Отмена')}
        </Button>
        <Button
          type="button"
          variant="destructive"
          className="h-11 sm:h-8"
          disabled={isPending}
          aria-busy={isPending}
          onClick={() => {
            void handleConfirm()
          }}
        >
          {isPending && (
            <Loader2
              data-icon="inline-start"
              aria-hidden
              className="animate-spin"
            />
          )}
          {t('Скрыть')}
        </Button>
      </DialogFooter>
    </>
  )
}
