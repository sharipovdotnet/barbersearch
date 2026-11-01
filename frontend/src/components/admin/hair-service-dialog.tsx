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
import { ApiError } from '@/lib/api'
import {
  createHairService,
  describeAdminCatalogueError,
  updateHairService,
  type AdminHairService,
} from '@/lib/admin-catalogue'
import {
  SERVICE_CATEGORY_LABELS,
  type ServiceCategory,
} from '@/lib/barber-profile-labels'

const CATEGORIES = Object.keys(SERVICE_CATEGORY_LABELS) as ServiceCategory[]

/** Границы домена: `Constants.MIN/MAX_DURATION_MINUTES`. */
const MIN_DURATION = 1
const MAX_DURATION = 1440

type FormErrors = {
  name?: string
  defaultDurationMinutes?: string
}

/**
 * Создание и правка услуги справочника.
 *
 * Категория выбирается только при создании: домен не даёт её поменять, потому что цена
 * и длительность предложений барберов привязаны к услуге. Длительность проверяется здесь
 * до запроса, но окончательное слово за доменом — его коды показываются у поля.
 */
export function HairServiceDialog({
  mode,
  service,
  onClose,
  onSaved,
}: {
  mode: 'create' | 'edit'
  service: AdminHairService | null
  onClose: () => void
  onSaved: (message: string) => void
}) {
  const [name, setName] = useState(service?.name ?? '')
  const [category, setCategory] = useState<ServiceCategory>(
    service?.category ?? 'Haircut'
  )
  const [duration, setDuration] = useState(
    String(service?.defaultDurationMinutes ?? 30)
  )
  const [description, setDescription] = useState(service?.description ?? '')
  const [errors, setErrors] = useState<FormErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()
    const minutes = Number(duration)
    const nextErrors: FormErrors = {}

    if (trimmedName === '') {
      nextErrors.name = 'Введите название услуги.'
    }

    if (
      !Number.isInteger(minutes)
      || minutes < MIN_DURATION
      || minutes > MAX_DURATION
    ) {
      nextErrors.defaultDurationMinutes = `От ${MIN_DURATION} до ${MAX_DURATION} минут.`
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
    }

    try {
      if (mode === 'create') {
        await createHairService({ ...payload, category })
        onSaved('Услуга создана')
      } else if (service) {
        await updateHairService(service.id, payload)
        onSaved('Изменения сохранены')
      }
    } catch (cause) {
      if (cause instanceof ApiError) {
        if (cause.hasCode('hairService.name')) {
          setErrors({ name: 'Проверьте название услуги.' })
        }

        if (cause.hasCode('hairService.defaultDurationMinutes')) {
          setErrors({ defaultDurationMinutes: `От ${MIN_DURATION} до ${MAX_DURATION} минут.` })
        }
      }

      setFormError(
        describeAdminCatalogueError(
          cause,
          mode === 'create'
            ? 'Не удалось создать услугу. Попробуйте ещё раз.'
            : 'Не удалось сохранить услугу. Попробуйте ещё раз.'
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
              {mode === 'create' ? 'Новая услуга' : 'Правка услуги'}
            </DialogTitle>
            <DialogDescription>
              Услуга появляется в фильтрах витрины сразу после создания.
            </DialogDescription>
          </DialogHeader>

          <FieldGroup>
            <Field data-invalid={Boolean(errors.name)}>
              <FieldLabel htmlFor="catalogue-service-name">Название</FieldLabel>
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

            {mode === 'create' ? (
              <Field>
                <FieldLabel htmlFor="catalogue-service-category">
                  Категория
                </FieldLabel>
                <select
                  id="catalogue-service-category"
                  name="category"
                  value={category}
                  disabled={isPending}
                  onChange={(event) =>
                    setCategory(event.target.value as ServiceCategory)
                  }
                  className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-8"
                >
                  {CATEGORIES.map((value) => (
                    <option key={value} value={value}>
                      {SERVICE_CATEGORY_LABELS[value]}
                    </option>
                  ))}
                </select>
              </Field>
            ) : (
              <Field>
                <FieldLabel htmlFor="catalogue-service-category-readonly">
                  Категория
                </FieldLabel>
                <Input
                  id="catalogue-service-category-readonly"
                  value={service ? SERVICE_CATEGORY_LABELS[service.category] : ''}
                  readOnly
                  disabled
                />
                <FieldDescription>
                  Категория не меняется после создания.
                </FieldDescription>
              </Field>
            )}

            <Field data-invalid={Boolean(errors.defaultDurationMinutes)}>
              <FieldLabel htmlFor="catalogue-service-duration">
                Длительность по умолчанию, минуты
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
                Описание
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
              Отмена
            </Button>
            <Button
              type="submit"
              className="h-11 sm:h-8"
              disabled={isPending}
              aria-busy={isPending}
            >
              {isPending && (
                <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
              )}
              {mode === 'create' ? 'Создать' : 'Сохранить'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
