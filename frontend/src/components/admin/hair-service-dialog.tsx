import { Loader2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'

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
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { useI18n } from '@/i18n'
import {
  createHairService,
  describeAdminCatalogueError,
  updateHairService,
  type AdminHairService,
  type AdminSpecialization,
} from '@/lib/admin-catalogue'
import { ApiError } from '@/lib/api'
import { SERVICE_CATEGORY_LABELS } from '@/lib/barber-profile-labels'

/** Границы домена: `Constants.MIN/MAX_DURATION_MINUTES`. */
const MIN_DURATION = 1
const MAX_DURATION = 1440

/** Состояние справочника специализаций, загружаемого при открытии диалога. */
type SpecializationsStatus = 'loading' | 'ready' | 'error'

type FormErrors = {
  name?: string
  specializationId?: string
  defaultDurationMinutes?: string
}

/**
 * Создание и правка услуги справочника.
 *
 * Категория больше не выбирается: услуга ссылается на специализацию, а категория —
 * её производное. Список специализаций грузит экран каталога лениво, при открытии
 * диалога, и отдаёт его сюда вместе со статусом. Пока справочник пуст, форма
 * не отправляется: подсказка ведёт администратора во вкладку «Специализации».
 * Специализацию можно менять и при правке — домен это разрешает.
 */
export function HairServiceDialog({
  mode,
  service,
  specializations,
  specializationsStatus,
  specializationsError,
  onReloadSpecializations,
  onClose,
  onSaved,
}: {
  mode: 'create' | 'edit'
  service: AdminHairService | null
  specializations: AdminSpecialization[]
  specializationsStatus: SpecializationsStatus
  specializationsError: string | null
  onReloadSpecializations: () => void
  onClose: () => void
  onSaved: (message: string) => void
}) {
  const { t } = useI18n()
  const [name, setName] = useState(service?.name ?? '')
  const [specializationId, setSpecializationId] = useState(
    service?.specializationId ?? ''
  )
  const [duration, setDuration] = useState(
    String(service?.defaultDurationMinutes ?? 30)
  )
  const [description, setDescription] = useState(service?.description ?? '')
  const [errors, setErrors] = useState<FormErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  // Пока справочник грузится, пустой выбор показывается первым доступным:
  // эффект, синхронизирующий состояние с пропсами, здесь не нужен.
  const effectiveSpecializationId =
    specializationId || specializations[0]?.id || ''
  const selectedSpecialization = specializations.find(
    (option) => option.id === effectiveSpecializationId
  )
  // Строка может исчезнуть из ответа (например, справочник перечитали): показываем
  // текущую специализацию услуги отдельным вариантом, чтобы селект не был пустым.
  const isCurrentMissing =
    service !== null
    && !specializations.some((option) => option.id === service.specializationId)
  const isEmptyCatalogue =
    specializationsStatus === 'ready'
    && specializations.length === 0
    && !isCurrentMissing
  const canSubmit =
    specializationsStatus === 'ready'
    && (specializations.length > 0 || isCurrentMissing)
  const derivedCategory = selectedSpecialization?.category ?? service?.category

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()
    const minutes = Number(duration)
    const nextErrors: FormErrors = {}

    if (trimmedName === '') {
      nextErrors.name = t('Введите название услуги.')
    }

    if (effectiveSpecializationId === '') {
      nextErrors.specializationId = t('Выберите специализацию для услуги.')
    }

    if (
      !Number.isInteger(minutes)
      || minutes < MIN_DURATION
      || minutes > MAX_DURATION
    ) {
      nextErrors.defaultDurationMinutes = t(
        'От {min} до {max} минут.',
        { min: MIN_DURATION, max: MAX_DURATION }
      )
    }

    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      return
    }

    setIsPending(true)

    const payload = {
      name: trimmedName,
      description: description.trim() === '' ? null : description.trim(),
      defaultDurationMinutes: minutes,
      specializationId: effectiveSpecializationId,
    }

    try {
      if (mode === 'create') {
        await createHairService(payload)
        onSaved(t('Услуга создана'))
      } else if (service) {
        await updateHairService(service.id, payload)
        onSaved(t('Изменения сохранены'))
      }
    } catch (cause) {
      if (cause instanceof ApiError) {
        const serverErrors: FormErrors = {}

        if (cause.hasCode('hairService.name')) {
          serverErrors.name = t('Проверьте название услуги.')
        }

        if (cause.hasCode('hairService.specializationId')) {
          serverErrors.specializationId = t('Выберите специализацию для услуги.')
        }

        if (cause.hasCode('hairService.specialization.notFound')) {
          serverErrors.specializationId = t(
            'Специализация не найдена — обновите список.'
          )
        }

        if (cause.hasCode('hairService.defaultDurationMinutes')) {
          serverErrors.defaultDurationMinutes = t(
            'От {min} до {max} минут.',
            { min: MIN_DURATION, max: MAX_DURATION }
          )
        }

        if (Object.keys(serverErrors).length > 0) {
          setErrors(serverErrors)
        }
      }

      setFormError(
        describeAdminCatalogueError(
          cause,
          mode === 'create'
            ? t('Не удалось создать услугу. Попробуйте ещё раз.')
            : t('Не удалось сохранить услугу. Попробуйте ещё раз.')
        )
      )
    } finally {
      setIsPending(false)
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !isPending) {
          onClose()
        }
      }}
    >
      <DialogContent className="sm:max-w-md">
        <form
          className="flex flex-col gap-4"
          noValidate
          aria-busy={isPending}
          onSubmit={handleSubmit}
        >
          <DialogHeader>
            <DialogTitle>
              {mode === 'create' ? t('Новая услуга') : t('Правка услуги')}
            </DialogTitle>
            <DialogDescription>
              {t('Услуга появляется в фильтрах витрины сразу после создания.')}
            </DialogDescription>
          </DialogHeader>

          <FieldGroup>
            <Field data-invalid={Boolean(errors.name)}>
              <FieldLabel htmlFor="catalogue-service-name">
                {t('Название')}
              </FieldLabel>
              <Input
                id="catalogue-service-name"
                name="name"
                value={name}
                disabled={isPending}
                aria-invalid={Boolean(errors.name)}
                aria-describedby={
                  errors.name ? 'catalogue-service-name-error' : undefined
                }
                onChange={(event) => {
                  setName(event.target.value)
                  setErrors((current) => ({ ...current, name: undefined }))
                  setFormError(null)
                }}
              />
              <FieldError id="catalogue-service-name-error">
                {errors.name}
              </FieldError>
            </Field>

            <Field data-invalid={Boolean(errors.specializationId)}>
              <FieldLabel htmlFor="catalogue-service-specialization">
                {t('Специализация')}
              </FieldLabel>
              <select
                id="catalogue-service-specialization"
                name="specializationId"
                value={effectiveSpecializationId}
                disabled={isPending || !canSubmit}
                aria-invalid={Boolean(errors.specializationId)}
                aria-describedby={
                  errors.specializationId
                    ? 'catalogue-service-specialization-error'
                    : canSubmit
                      ? 'catalogue-service-specialization-hint'
                      : undefined
                }
                onChange={(event) => {
                  setSpecializationId(event.target.value)
                  setErrors((current) => ({
                    ...current,
                    specializationId: undefined,
                  }))
                  setFormError(null)
                }}
                className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-8"
              >
                {specializationsStatus === 'loading' && (
                  <option value="">
                    {t('Загружаем специализации…')}
                  </option>
                )}
                {isEmptyCatalogue && (
                  <option value="">
                    {t('Справочник специализаций пуст.')}
                  </option>
                )}
                {specializations.map((option) => (
                  <option key={option.id} value={option.id}>
                    {option.isActive
                      ? option.name
                      : t('{name} (выключена)', { name: option.name })}
                  </option>
                ))}
                {isCurrentMissing && service && (
                  <option value={service.specializationId}>
                    {service.specializationName}
                  </option>
                )}
              </select>
              <FieldError id="catalogue-service-specialization-error">
                {errors.specializationId}
              </FieldError>

              {specializationsStatus === 'loading' && (
                <FieldDescription id="catalogue-service-specialization-hint">
                  {t('Загружаем специализации…')}
                </FieldDescription>
              )}

              {specializationsStatus === 'error' && (
                <div className="flex flex-col items-start gap-2">
                  <FieldError>
                    {specializationsError
                      ?? t('Не удалось загрузить специализации. Попробуйте ещё раз.')}
                  </FieldError>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="h-11 sm:h-8"
                    disabled={isPending}
                    onClick={onReloadSpecializations}
                  >
                    {t('Повторить')}
                  </Button>
                </div>
              )}

              {isEmptyCatalogue && (
                <FieldDescription id="catalogue-service-specialization-hint">
                  {t(
                    'В справочнике нет специализаций. Создайте специализацию во вкладке «Специализации», чтобы добавить услугу.'
                  )}
                </FieldDescription>
              )}

              {specializationsStatus === 'ready' && derivedCategory && (
                <FieldDescription id="catalogue-service-specialization-hint">
                  {t('Категория определяется специализацией: {category}', {
                    category: t(SERVICE_CATEGORY_LABELS[derivedCategory]),
                  })}
                </FieldDescription>
              )}
            </Field>

            <Field data-invalid={Boolean(errors.defaultDurationMinutes)}>
              <FieldLabel htmlFor="catalogue-service-duration">
                {t('Длительность по умолчанию, минуты')}
              </FieldLabel>
              <Input
                id="catalogue-service-duration"
                name="defaultDurationMinutes"
                type="number"
                inputMode="numeric"
                min={MIN_DURATION}
                max={MAX_DURATION}
                value={duration}
                disabled={isPending}
                aria-invalid={Boolean(errors.defaultDurationMinutes)}
                aria-describedby={
                  errors.defaultDurationMinutes
                    ? 'catalogue-service-duration-error'
                    : undefined
                }
                onChange={(event) => {
                  setDuration(event.target.value)
                  setErrors((current) => ({
                    ...current,
                    defaultDurationMinutes: undefined,
                  }))
                  setFormError(null)
                }}
              />
              <FieldError id="catalogue-service-duration-error">
                {errors.defaultDurationMinutes}
              </FieldError>
            </Field>

            <Field>
              <FieldLabel htmlFor="catalogue-service-description">
                {t('Описание')}
              </FieldLabel>
              <Textarea
                id="catalogue-service-description"
                name="description"
                rows={3}
                value={description}
                disabled={isPending}
                onChange={(event) => setDescription(event.target.value)}
              />
            </Field>
          </FieldGroup>

          {formError && (
            <p role="alert" className="text-sm text-destructive">
              {formError}
            </p>
          )}

          <DialogFooter>
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
              type="submit"
              className="h-11 sm:h-8"
              disabled={isPending || !canSubmit}
              aria-busy={isPending}
            >
              {isPending && (
                <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
              )}
              {mode === 'create' ? t('Создать') : t('Сохранить')}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
