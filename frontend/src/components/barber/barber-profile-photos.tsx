import { useEffect, useRef, useState } from 'react'
import { Loader2 } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
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
import { Textarea } from '@/components/ui/textarea'
import { useI18n } from '@/i18n'
import { invalidateBarberProfile } from '@/lib/catalog'
import { ImagePrepError, preparePortfolioImage } from '@/lib/image-prep'
import {
  describePortfolioPhotoError,
  MAX_PORTFOLIO_PHOTOS,
  PORTFOLIO_DESCRIPTION_MAX,
  publishPortfolioPhoto,
  removePortfolioPhoto,
  reorderPortfolioPhotos,
  updatePortfolioPhotoDescription,
  uploadPortfolioPhoto,
  unpublishPortfolioPhoto,
  type PortfolioPhoto,
} from '@/lib/portfolio-photos'

/**
 * «Фотографии» в кабинете барбера (§5.3).
 *
 * Портфолио — единственная часть карточки, где файл готовит браузер: `preparePortfolioImage`
 * уменьшает кадр до 1600 px и перекодирует в JPEG, только потом уходит multipart-загрузка.
 * Порядок меняется одним bulk-запросом с полным массивом id: два одиночных `PUT` при
 * частичном сбое дали бы дубли `SortOrder` (§3.6).
 *
 * Ошибка мутации живёт строкой `role="alert"` над сеткой и не прячет уже загруженные
 * фотографии. Пока идёт запрос, блокируются действия одной карточки, а не страница.
 * Каждая успешная мутация сбрасывает кэш публичной карточки, иначе «Открыть, как видят
 * клиенты» показал бы старый набор.
 */

type PhotoDialog =
  | { kind: 'upload' }
  | { kind: 'caption'; photo: PortfolioPhoto }
  | { kind: 'delete'; photo: PortfolioPhoto }

export function BarberProfilePhotos({
  barberProfileId,
  photos,
  onChanged,
}: {
  barberProfileId: string
  photos: readonly PortfolioPhoto[]
  onChanged: (message: string) => void
}) {
  const { t } = useI18n()
  const [dialog, setDialog] = useState<PhotoDialog | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  const [pendingPhotoId, setPendingPhotoId] = useState<string | null>(null)
  const [isReordering, setIsReordering] = useState(false)
  const isAtLimit = photos.length >= MAX_PORTFOLIO_PHOTOS

  function closeDialog(): void {
    setDialog(null)
  }

  async function handleMove(
    photo: PortfolioPhoto,
    direction: -1 | 1
  ): Promise<void> {
    if (isReordering) {
      return
    }

    const index = photos.findIndex((item) => item.id === photo.id)
    const target = index + direction

    if (index === -1 || target < 0 || target >= photos.length) {
      return
    }

    // Один запрос на перестановку: сервер присваивает SortOrder по порядку массива.
    const nextIds = photos.map((item) => item.id)
    const moved = nextIds[index]
    nextIds[index] = nextIds[target]
    nextIds[target] = moved

    setIsReordering(true)
    setListError(null)

    try {
      await reorderPortfolioPhotos(barberProfileId, nextIds)
      invalidateBarberProfile(barberProfileId)
      onChanged(t('Порядок изменён.'))
    } catch (cause) {
      setListError(describePortfolioPhotoError(cause).message)
    } finally {
      setIsReordering(false)
    }
  }

  async function handlePublication(photo: PortfolioPhoto): Promise<void> {
    if (pendingPhotoId !== null) {
      return
    }

    setPendingPhotoId(photo.id)
    setListError(null)

    try {
      if (photo.isPublished) {
        await unpublishPortfolioPhoto(barberProfileId, photo.id)
      } else {
        await publishPortfolioPhoto(barberProfileId, photo.id)
      }

      invalidateBarberProfile(barberProfileId)
      onChanged(
        photo.isPublished
          ? t('Фото скрыто.')
          : t('Фото снова в карточке.')
      )
    } catch (cause) {
      setListError(describePortfolioPhotoError(cause).message)
    } finally {
      setPendingPhotoId(null)
    }
  }

  return (
    <section aria-labelledby="barber-profile-photos-heading">
      <Card>
        <CardHeader className="flex flex-row items-start justify-between gap-3">
          <div className="min-w-0 space-y-1">
            <h2
              id="barber-profile-photos-heading"
              className="font-heading text-base leading-snug font-medium tracking-tight"
            >
              {t('Фотографии')}
            </h2>
            <p className="text-sm text-muted-foreground">
              {t(
                'Работы видны в вашей карточке. Первое фото показывается первым.'
              )}
            </p>
          </div>
          <Button
            type="button"
            size="sm"
            className="h-11 shrink-0 sm:h-8"
            disabled={isAtLimit}
            onClick={() => {
              setListError(null)
              setDialog({ kind: 'upload' })
            }}
          >
            {t('Добавить фото')}
          </Button>
        </CardHeader>

        <CardContent className="space-y-4">
          {isAtLimit && (
            <p className="text-sm text-muted-foreground">
              {t(
                'В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.'
              )}
            </p>
          )}

          {listError && (
            <p role="alert" className="text-sm text-destructive">
              {listError}
            </p>
          )}

          {photos.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {t(
                'Фотографий нет. Добавьте работы, чтобы клиенты видели ваш стиль.'
              )}
            </p>
          ) : (
            <ul
              aria-label={t('Фотографии')}
              className="grid gap-3 sm:grid-cols-2"
            >
              {photos.map((photo, index) => (
                <PhotoCard
                  key={photo.id}
                  photo={photo}
                  number={index + 1}
                  isFirst={index === 0}
                  isLast={index === photos.length - 1}
                  isPending={pendingPhotoId === photo.id}
                  isReordering={isReordering}
                  onMove={(direction) => {
                    void handleMove(photo, direction)
                  }}
                  onEditCaption={() => {
                    setListError(null)
                    setDialog({ kind: 'caption', photo })
                  }}
                  onTogglePublication={() => {
                    void handlePublication(photo)
                  }}
                  onDelete={() => {
                    setListError(null)
                    setDialog({ kind: 'delete', photo })
                  }}
                />
              ))}
            </ul>
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
        {dialog?.kind === 'upload' && (
          <DialogContent>
            <UploadPhotoDialog
              barberProfileId={barberProfileId}
              onClose={closeDialog}
              onUploaded={() => {
                onChanged(t('Фото добавлено.'))
                closeDialog()
              }}
            />
          </DialogContent>
        )}

        {dialog?.kind === 'caption' && (
          <DialogContent>
            <CaptionDialog
              barberProfileId={barberProfileId}
              photo={dialog.photo}
              onClose={closeDialog}
              onSaved={() => {
                onChanged(t('Подпись сохранена.'))
                closeDialog()
              }}
            />
          </DialogContent>
        )}

        {dialog?.kind === 'delete' && (
          <DialogContent role="alertdialog">
            <DeletePhotoDialog
              barberProfileId={barberProfileId}
              photo={dialog.photo}
              onClose={closeDialog}
              onDeleted={() => {
                onChanged(t('Фото удалено.'))
                closeDialog()
              }}
            />
          </DialogContent>
        )}
      </Dialog>
    </section>
  )
}

/** Карточка одной фотографии: превью, подпись, статус и действия 44 px. */
function PhotoCard({
  photo,
  number,
  isFirst,
  isLast,
  isPending,
  isReordering,
  onMove,
  onEditCaption,
  onTogglePublication,
  onDelete,
}: {
  photo: PortfolioPhoto
  number: number
  isFirst: boolean
  isLast: boolean
  isPending: boolean
  isReordering: boolean
  onMove: (direction: -1 | 1) => void
  onEditCaption: () => void
  onTogglePublication: () => void
  onDelete: () => void
}) {
  const { t } = useI18n()
  const caption = photo.description?.trim() ?? ''
  // Имя кнопки начинается с видимого текста и содержит подпись фото (WCAG 2.5.3).
  const label = caption === '' ? t('фото {number}', { number }) : caption
  const ariaLabel = (action: string): string =>
    t('{action}: {caption}', { action, caption: label })

  return (
    <li
      className="space-y-2 rounded-lg border p-2"
      aria-busy={isPending || isReordering || undefined}
    >
      <img
        src={photo.url}
        alt={caption === '' ? t('Работа {number}', { number }) : caption}
        loading="lazy"
        className="aspect-square w-full rounded-md bg-muted object-cover"
      />

      <div className="space-y-1.5">
        <p className="text-sm break-words">
          {caption === '' ? t('Без подписи') : caption}
        </p>

        {photo.isBlocked && (
          <Badge variant="destructive">{t('Скрыто администратором')}</Badge>
        )}
        {!photo.isBlocked && !photo.isPublished && (
          <Badge variant="secondary">{t('Скрыто')}</Badge>
        )}
      </div>

      <div className="flex flex-wrap gap-1.5">
        {/* Заблокированную администратором работу владелец может только удалить. */}
        {!photo.isBlocked && (
          <>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              aria-label={ariaLabel(t('Выше'))}
              disabled={isFirst || isReordering}
              onClick={() => onMove(-1)}
            >
              {t('Выше')}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              aria-label={ariaLabel(t('Ниже'))}
              disabled={isLast || isReordering}
              onClick={() => onMove(1)}
            >
              {t('Ниже')}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 sm:h-8"
              aria-label={ariaLabel(t('Изменить подпись'))}
              disabled={isPending}
              onClick={onEditCaption}
            >
              {t('Изменить подпись')}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-11 sm:h-8"
              aria-label={ariaLabel(
                photo.isPublished ? t('Скрыть') : t('Вернуть')
              )}
              disabled={isPending}
              aria-busy={isPending || undefined}
              onClick={onTogglePublication}
            >
              {isPending && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {photo.isPublished ? t('Скрыть') : t('Вернуть')}
            </Button>
          </>
        )}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-11 text-destructive hover:text-destructive sm:h-8"
          aria-label={ariaLabel(t('Удалить'))}
          disabled={isPending}
          onClick={onDelete}
        >
          {t('Удалить')}
        </Button>
      </div>
    </li>
  )
}

/** Загрузка: файл, превью, подпись со счётчиком и ошибки, которые не закрывают диалог. */
function UploadPhotoDialog({
  barberProfileId,
  onClose,
  onUploaded,
}: {
  barberProfileId: string
  onClose: () => void
  onUploaded: () => void
}) {
  const { t } = useI18n()
  const [file, setFile] = useState<File | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [caption, setCaption] = useState('')
  const [fileError, setFileError] = useState<string | null>(null)
  const [captionError, setCaptionError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)
  const previewRef = useRef<string | null>(null)

  // Object URL живёт до закрытия диалога или размонтирования страницы: иначе
  // выбранный файл остаётся в памяти браузера до перезагрузки.
  useEffect(() => {
    return () => {
      if (previewRef.current !== null) {
        URL.revokeObjectURL(previewRef.current)
        previewRef.current = null
      }
    }
  }, [])

  function selectFile(next: File | null): void {
    if (previewRef.current !== null) {
      URL.revokeObjectURL(previewRef.current)
      previewRef.current = null
    }

    setFile(next)
    setFileError(null)
    setFormError(null)

    if (next === null) {
      setPreviewUrl(null)
      return
    }

    const url = URL.createObjectURL(next)
    previewRef.current = url
    setPreviewUrl(url)
  }

  async function handleSubmit(): Promise<void> {
    if (isPending) {
      return
    }

    if (file === null) {
      setFileError(t('Выберите файл.'))
      return
    }

    setIsPending(true)
    setFileError(null)
    setCaptionError(null)
    setFormError(null)

    try {
      const prepared = await preparePortfolioImage(file)
      await uploadPortfolioPhoto(barberProfileId, prepared, caption)
      invalidateBarberProfile(barberProfileId)
      onUploaded()
    } catch (cause) {
      if (cause instanceof ImagePrepError) {
        setFileError(cause.message)
      } else {
        const failure = describePortfolioPhotoError(cause)

        if (failure.field === 'file') {
          setFileError(failure.message)
        } else if (failure.field === 'description') {
          setCaptionError(failure.message)
        } else {
          setFormError(failure.message)
        }
      }

      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t('Добавить фото')}</DialogTitle>
        <DialogDescription>
          {t(
            'JPEG, PNG или WebP до 15 МБ. Перед загрузкой фото уменьшится до 1600 px по длинной стороне.'
          )}
        </DialogDescription>
      </DialogHeader>

      <div className="space-y-3">
        <div className="space-y-1.5">
          <Label htmlFor="barber-profile-photo-file">{t('Файл')}</Label>
          <Input
            id="barber-profile-photo-file"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            className="h-11 sm:h-8"
            aria-invalid={fileError !== null}
            aria-describedby={
              fileError === null ? undefined : 'barber-profile-photo-file-error'
            }
            onChange={(event) => {
              selectFile(event.target.files?.[0] ?? null)
            }}
          />
          {fileError && (
            <p
              id="barber-profile-photo-file-error"
              role="alert"
              className="text-sm text-destructive"
            >
              {fileError}
            </p>
          )}
        </div>

        {previewUrl && (
          <img
            src={previewUrl}
            alt={t('Предпросмотр выбранного фото')}
            className="aspect-square w-full max-w-48 rounded-lg object-cover"
          />
        )}

        <div className="space-y-1.5">
          <Label htmlFor="barber-profile-photo-caption">
            {t('Подпись')}
          </Label>
          <Textarea
            id="barber-profile-photo-caption"
            value={caption}
            maxLength={PORTFOLIO_DESCRIPTION_MAX}
            aria-invalid={captionError !== null}
            onChange={(event) => {
              setCaption(event.target.value)
              setCaptionError(null)
            }}
          />
          <p className="text-xs text-muted-foreground tabular-nums">
            {caption.length}/{PORTFOLIO_DESCRIPTION_MAX}
          </p>
          {captionError && (
            <p role="alert" className="text-sm text-destructive">
              {captionError}
            </p>
          )}
        </div>

        {formError && (
          <p role="alert" className="text-sm text-destructive">
            {formError}
          </p>
        )}
      </div>

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          disabled={isPending}
          onClick={onClose}
        >
          {t('Отмена')}
        </Button>
        <Button
          type="button"
          className="h-11 sm:h-8"
          disabled={isPending}
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
          {t('Загрузить')}
        </Button>
      </DialogFooter>
    </>
  )
}

/** Правка подписи: пустая строка стирает подпись (сервер хранит `null`). */
function CaptionDialog({
  barberProfileId,
  photo,
  onClose,
  onSaved,
}: {
  barberProfileId: string
  photo: PortfolioPhoto
  onClose: () => void
  onSaved: () => void
}) {
  const { t } = useI18n()
  const [value, setValue] = useState(photo.description ?? '')
  const [isPending, setIsPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(): Promise<void> {
    if (isPending) {
      return
    }

    setIsPending(true)
    setError(null)

    try {
      await updatePortfolioPhotoDescription(barberProfileId, photo.id, value)
      invalidateBarberProfile(barberProfileId)
      onSaved()
    } catch (cause) {
      setError(describePortfolioPhotoError(cause).message)
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t('Изменить подпись')}</DialogTitle>
        <DialogDescription>
          {t('Подпись видна под фото в карточке барбера.')}
        </DialogDescription>
      </DialogHeader>

      <div className="space-y-1.5">
        <Label htmlFor="barber-profile-photo-caption-edit">
          {t('Подпись')}
        </Label>
        <Textarea
          id="barber-profile-photo-caption-edit"
          value={value}
          maxLength={PORTFOLIO_DESCRIPTION_MAX}
          aria-invalid={error !== null}
          onChange={(event) => {
            setValue(event.target.value)
            setError(null)
          }}
        />
        <p className="text-xs text-muted-foreground tabular-nums">
          {value.length}/{PORTFOLIO_DESCRIPTION_MAX}
        </p>
        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}
      </div>

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          disabled={isPending}
          onClick={onClose}
        >
          {t('Отмена')}
        </Button>
        <Button
          type="button"
          className="h-11 sm:h-8"
          disabled={isPending}
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

/** Удаление необратимо: `alertdialog` с точным текстом последствия. */
function DeletePhotoDialog({
  barberProfileId,
  photo,
  onClose,
  onDeleted,
}: {
  barberProfileId: string
  photo: PortfolioPhoto
  onClose: () => void
  onDeleted: () => void
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
      await removePortfolioPhoto(barberProfileId, photo.id)
      invalidateBarberProfile(barberProfileId)
      onDeleted()
    } catch (cause) {
      setError(describePortfolioPhotoError(cause).message)
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{t('Удалить фото?')}</DialogTitle>
        <DialogDescription>
          {t('Фото исчезнет из карточки и поиска. Это необратимо.')}
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
          disabled={isPending}
          onClick={onClose}
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
          {t('Удалить')}
        </Button>
      </DialogFooter>
    </>
  )
}
