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
  createSpecialization,
  describeAdminCatalogueError,
  renameSpecialization,
  type AdminSpecialization,
} from '@/lib/admin-catalogue'
import { ApiError } from '@/lib/api'
import {
  SERVICE_CATEGORY_LABELS,
  type ServiceCategory,
} from '@/lib/barber-profile-labels'

const CATEGORIES = Object.keys(SERVICE_CATEGORY_LABELS) as ServiceCategory[]

/**
 * Создание и правка специализации.
 *
 * Имя уникально без учёта регистра: повтор возвращает `409`, и диалог показывает это
 * у формы, а не техническим текстом сервера. Категория обязательна и при создании,
 * и при правке: она задаёт фильтр витрины и категорию услуг специализации.
 */
export function SpecializationDialog({
  mode,
  specialization,
  onClose,
  onSaved,
}: {
  mode: 'create' | 'edit'
  specialization: AdminSpecialization | null
  onClose: () => void
  onSaved: (message: string) => void
}) {
  const { t } = useI18n()
  const [name, setName] = useState(specialization?.name ?? '')
  const [category, setCategory] = useState<ServiceCategory>(
    specialization?.category ?? 'Haircut'
  )
  const [description, setDescription] = useState(
    specialization?.description ?? ''
  )
  const [nameError, setNameError] = useState<string | undefined>(undefined)
  const [categoryError, setCategoryError] = useState<string | undefined>(
    undefined
  )
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()

    if (trimmedName === '') {
      setNameError(t('Введите название специализации.'))
      return
    }

    setNameError(undefined)
    setCategoryError(undefined)
    setFormError(null)
    setIsPending(true)

    const payload = {
      name: trimmedName,
      category,
      description: description.trim() === '' ? null : description.trim(),
    }

    try {
      if (mode === 'create') {
        await createSpecialization(payload)
        onSaved(t('Специализация создана'))
      } else if (specialization) {
        await renameSpecialization(specialization.id, payload)
        onSaved(t('Специализация переименована'))
      }
    } catch (cause) {
      if (
        cause instanceof ApiError
        && (cause.hasCode('specialization.category')
          || cause.hasCode('specialization.category.unknown'))
      ) {
        setCategoryError(t('Неизвестная категория. Выберите категорию из списка.'))
      }

      setFormError(
        describeAdminCatalogueError(
          cause,
          mode === 'create'
            ? t('Не удалось создать специализацию. Попробуйте ещё раз.')
            : t('Не удалось переименовать специализацию. Попробуйте ещё раз.')
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
              {mode === 'create'
                ? t('Новая специализация')
                : t('Правка специализации')}
            </DialogTitle>
            <DialogDescription>
              {t(
                'Специализация — тег барбера: выключенная строка остаётся на карточках тех, кто её указал.'
              )}
            </DialogDescription>
          </DialogHeader>

          <FieldGroup>
            <Field data-invalid={Boolean(nameError)}>
              <FieldLabel htmlFor="catalogue-specialization-name">
                {t('Название')}
              </FieldLabel>
              <Input
                id="catalogue-specialization-name"
                name="name"
                value={name}
                disabled={isPending}
                aria-invalid={Boolean(nameError)}
                aria-describedby={
                  nameError ? 'catalogue-specialization-name-error' : undefined
                }
                onChange={(event) => {
                  setName(event.target.value)
                  setNameError(undefined)
                  setFormError(null)
                }}
              />
              <FieldError id="catalogue-specialization-name-error">
                {nameError}
              </FieldError>
            </Field>

            <Field data-invalid={Boolean(categoryError)}>
              <FieldLabel htmlFor="catalogue-specialization-category">
                {t('Категория')}
              </FieldLabel>
              <select
                id="catalogue-specialization-category"
                name="category"
                value={category}
                disabled={isPending}
                aria-invalid={Boolean(categoryError)}
                aria-describedby={
                  categoryError
                    ? 'catalogue-specialization-category-hint catalogue-specialization-category-error'
                    : 'catalogue-specialization-category-hint'
                }
                onChange={(event) => {
                  setCategory(event.target.value as ServiceCategory)
                  setCategoryError(undefined)
                  setFormError(null)
                }}
                className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-8"
              >
                {CATEGORIES.map((value) => (
                  <option key={value} value={value}>
                    {t(SERVICE_CATEGORY_LABELS[value])}
                  </option>
                ))}
              </select>
              <FieldDescription id="catalogue-specialization-category-hint">
                {t(
                  'Категория специализации определяет фильтр витрины и категорию её услуг.'
                )}
              </FieldDescription>
              <FieldError id="catalogue-specialization-category-error">
                {categoryError}
              </FieldError>
            </Field>

            <Field>
              <FieldLabel htmlFor="catalogue-specialization-description">
                {t('Описание')}
              </FieldLabel>
              <Textarea
                id="catalogue-specialization-description"
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
              disabled={isPending}
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
