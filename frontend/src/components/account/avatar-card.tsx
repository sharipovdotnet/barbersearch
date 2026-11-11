import { useRef, useState } from 'react'
import { CheckCircle2, Loader2 } from 'lucide-react'

import { useAccountProfile } from '@/components/account/account-profile-context'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
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
import { useI18n, t } from '@/i18n'
import {
  AVATAR_SOURCE_MAX_BYTES,
  deleteAvatar,
  describeAvatarError,
  uploadAvatarWithStatus,
} from '@/lib/avatars'
import { invalidateBarberProfile } from '@/lib/catalog'
import {
  ImagePrepError,
  SUPPORTED_IMAGE_TYPES,
  prepareAvatarImage,
} from '@/lib/image-prep'
import { profileInitials, type MyProfile } from '@/lib/my-profile'
import {
  canRenderAvatarCardPreview,
  photoModerationLabel,
} from '@/lib/photo-moderation'

/**
 * Карточка аватара в `/account/profile`.
 *
 * Файл готовит браузер (`prepareAvatarImage`: квадрат 512×512, JPEG ≤ 5 МиБ), но
 * перед декодированием карточка сама отбрасывает чужой тип и слишком большой
 * исходник — незачем читать в память то, что сервер всё равно не примет.
 *
 * Успех объявляется `role="status"`, ошибки — `role="alert"`; спиннер и `disabled`
 * живут только на время операции, а не постоянно. После загрузки или удаления
 * `setAvatar` правит кэш профиля без второго `GET users/me`, а публичная карточка
 * барбера сбрасывается через `invalidateBarberProfile`: иначе «Открыть, как видят
 * клиенты» показал бы старый кадр.
 *
 * С премодерацией загруженный кадр виден только владельцу: карточка показывает
 * превью и бейдж «На проверке». Отклонённый кадр тоже рендерится — вместе с
 * причиной отказа, — чтобы было понятно, что именно не прошло проверку. Удаление
 * доступно в любом статусе.
 *
 * Удаление необратимо, поэтому идёт через `alertdialog`, а фокус после закрытия
 * возвращается на кнопку загрузки: кнопки «Удалить» после успеха уже нет.
 */
export function AvatarCard({ profile }: { profile: MyProfile }) {
  const { t } = useI18n()
  const { setAvatar } = useAccountProfile()
  const [clientError, setClientError] = useState<string | null>(null)
  const [serverError, setServerError] = useState<string | null>(null)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [isUploading, setIsUploading] = useState(false)
  const [isDeleting, setIsDeleting] = useState(false)
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const uploadButtonRef = useRef<HTMLButtonElement>(null)
  const focusUploadAfterClose = useRef(false)
  const isPending = isUploading || isDeleting

  async function handleSelectedFile(file: File | null): Promise<void> {
    if (file === null || isPending) {
      return
    }

    // Один и тот же файл можно выбрать снова: нативный input иначе не шлёт change.
    if (fileInputRef.current !== null) {
      fileInputRef.current.value = ''
    }

    setAnnouncement(null)
    setServerError(null)

    const clientMessage = describeClientCheck(file)

    if (clientMessage !== null) {
      setClientError(clientMessage)
      return
    }

    setClientError(null)
    setIsUploading(true)

    try {
      const prepared = await prepareAvatarImage(file)
      const uploaded = await uploadAvatarWithStatus(prepared)

      setAvatar(uploaded.avatarUrl, uploaded.moderationStatus)

      if (profile.barberProfileId !== null) {
        invalidateBarberProfile(profile.barberProfileId)
      }

      setAnnouncement(t('Аватар загружен и ждёт проверки.'))
    } catch (cause) {
      if (cause instanceof ImagePrepError) {
        setClientError(cause.message)
      } else {
        const failure = describeAvatarError(cause)

        if (failure.field === 'file') {
          setClientError(failure.message)
        } else {
          setServerError(failure.message)
        }
      }
    } finally {
      setIsUploading(false)
    }
  }

  async function handleDelete(): Promise<void> {
    if (isDeleting) {
      return
    }

    setIsDeleting(true)
    setDeleteError(null)
    setAnnouncement(null)

    try {
      await deleteAvatar()

      setAvatar(null, 'Approved')

      if (profile.barberProfileId !== null) {
        invalidateBarberProfile(profile.barberProfileId)
      }

      setAnnouncement(t('Аватар удалён.'))
      // Кнопки «Удалить» после успеха нет: возвращаем фокус на загрузку.
      focusUploadAfterClose.current = true
      setIsDeleteDialogOpen(false)
    } catch (cause) {
      setDeleteError(describeAvatarError(cause).message)
    } finally {
      setIsDeleting(false)
    }
  }

  return (
    <section
      aria-labelledby="account-avatar-heading"
      aria-busy={isPending || undefined}
    >
      <Card>
        <CardHeader>
          <h2
            id="account-avatar-heading"
            className="font-heading text-base leading-snug font-medium tracking-tight"
          >
            {t('Аватар')}
          </h2>
          <CardDescription>
            {t(
              'Фото профиля видят вы и администратор. У барбера оно показывается в открытой карточке.'
            )}
          </CardDescription>
        </CardHeader>

        <CardContent>
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center">
            <Avatar size="lg" className="size-24" aria-hidden>
              {/* Заблокированный кадр не рендерим даже при сыром `avatarUrl`:
                  браузер показал бы старый кадр из 24-часового кэша. Отклонённый
                  кадр в кабинете виден — рядом с ним причина отказа. */}
              {profile.avatarUrl !== null &&
                canRenderAvatarCardPreview(profile) && (
                  <AvatarImage src={profile.avatarUrl} alt="" />
                )}
              <AvatarFallback className="text-2xl">
                {profileInitials(profile)}
              </AvatarFallback>
            </Avatar>

            <div className="min-w-0 space-y-2">
              {profile.isAvatarBlocked && (
                <div className="space-y-1">
                  <p className="text-sm font-medium text-warning">
                    {t('Фото скрыто администратором')}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {t(
                      'Его не видно публично. Новое фото появится публично после проверки, а вернуть старое может только администратор.'
                    )}
                  </p>
                </div>
              )}

              {!profile.isAvatarBlocked &&
                profile.avatarModerationStatus === 'Pending' && (
                  <div className="space-y-1">
                    <Badge variant="secondary">
                      {photoModerationLabel('Pending')}
                    </Badge>
                    <p className="text-xs text-muted-foreground">
                      {t(
                        'Кадр виден только вам, пока администратор его не одобрит.'
                      )}
                    </p>
                  </div>
                )}

              {!profile.isAvatarBlocked &&
                profile.avatarModerationStatus === 'Rejected' && (
                  <div className="space-y-1">
                    <Badge variant="destructive">
                      {photoModerationLabel('Rejected')}
                    </Badge>
                    {profile.avatarRejectionReason !== null && (
                      <p className="text-sm break-words">
                        {t('Причина: {reason}', {
                          reason: profile.avatarRejectionReason,
                        })}
                      </p>
                    )}
                    <p className="text-xs text-muted-foreground">
                      {t(
                        'Кадр не прошёл проверку. Загрузите другое фото или удалите его.'
                      )}
                    </p>
                  </div>
                )}

              <input
                ref={fileInputRef}
                id="account-avatar-file"
                type="file"
                accept={SUPPORTED_IMAGE_TYPES.join(',')}
                className="hidden"
                aria-label={t('Файл')}
                aria-invalid={clientError !== null}
                onChange={(event) => {
                  void handleSelectedFile(event.target.files?.[0] ?? null)
                }}
              />

              <div className="flex flex-wrap gap-2">
                <Button
                  ref={uploadButtonRef}
                  type="button"
                  className="h-11 sm:h-8"
                  disabled={isPending}
                  aria-busy={isUploading || undefined}
                  onClick={() => fileInputRef.current?.click()}
                >
                  {isUploading && (
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                  )}
                  {profile.isAvatarBlocked ||
                  profile.avatarModerationStatus === 'Rejected'
                    ? t('Загрузить другое фото')
                    : profile.avatarUrl
                      ? t('Изменить фото')
                      : t('Загрузить фото')}
                </Button>

                {profile.avatarUrl && (
                  <Button
                    type="button"
                    variant="outline"
                    className="h-11 text-destructive hover:text-destructive sm:h-8"
                    disabled={isPending}
                    onClick={() => {
                      setDeleteError(null)
                      setIsDeleteDialogOpen(true)
                    }}
                  >
                    {t('Удалить')}
                  </Button>
                )}
              </div>

              <p className="text-xs text-muted-foreground">
                {t(
                  'JPEG, PNG или WebP до 5 МБ. Квадрат 512×512 подготовит браузер.'
                )}
              </p>

              {clientError && (
                <p role="alert" className="text-sm text-destructive">
                  {clientError}
                </p>
              )}

              {serverError && (
                <p role="alert" className="text-sm text-destructive">
                  {serverError}
                </p>
              )}

              {announcement && (
                <p
                  role="status"
                  className="flex items-center gap-1.5 text-sm text-success"
                >
                  <CheckCircle2 className="size-4" aria-hidden />
                  {announcement}
                </p>
              )}
            </div>
          </div>
        </CardContent>
      </Card>

      <Dialog
        open={isDeleteDialogOpen}
        onOpenChange={(open) => {
          if (!open) {
            setIsDeleteDialogOpen(false)
          }
        }}
        onOpenChangeComplete={(open) => {
          if (!open && focusUploadAfterClose.current) {
            focusUploadAfterClose.current = false
            uploadButtonRef.current?.focus()
          }
        }}
      >
        <DialogContent role="alertdialog">
          <DialogHeader>
            <DialogTitle>{t('Удалить аватар?')}</DialogTitle>
            <DialogDescription>
              {t(
                'Аватар исчезнет из карточки и кабинета. Файл удаляется безвозвратно.'
              )}
            </DialogDescription>
          </DialogHeader>

          {deleteError && (
            <p role="alert" className="text-sm text-destructive">
              {deleteError}
            </p>
          )}

          <DialogFooter className="flex-col sm:flex-row">
            <Button
              type="button"
              variant="outline"
              className="h-11 sm:h-8"
              disabled={isDeleting}
              onClick={() => setIsDeleteDialogOpen(false)}
            >
              {t('Отмена')}
            </Button>
            <Button
              type="button"
              variant="destructive"
              className="h-11 sm:h-8"
              disabled={isDeleting}
              aria-busy={isDeleting}
              onClick={() => {
                void handleDelete()
              }}
            >
              {isDeleting && (
                <Loader2
                  data-icon="inline-start"
                  aria-hidden
                  className="animate-spin"
                />
              )}
              {t('Удалить')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}

/**
 * Проверка до подготовки: тип и исходный размер.
 *
 * Сообщения совпадают с `ImagePrepError`, потому что дальше сработает та же
 * проверка: пользователь не должен увидеть два разных текста об одном отказе.
 */
function describeClientCheck(file: File): string | null {
  if (!SUPPORTED_IMAGE_TYPES.includes(file.type)) {
    return t('Поддерживаются только JPEG, PNG и WebP.')
  }

  if (file.size > AVATAR_SOURCE_MAX_BYTES) {
    return t('Файл больше 15 МБ. Выберите фото поменьше.')
  }

  return null
}
