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
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import {
  createSpecialization,
  describeAdminCatalogueError,
  renameSpecialization,
  type AdminSpecialization,
} from '@/lib/admin-catalogue'

/**
 * Создание и правка специализации.
 *
 * Имя уникально без учёта регистра: повтор возвращает `409`, и диалог показывает это
 * у формы, а не техническим текстом сервера.
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
  const [name, setName] = useState(specialization?.name ?? '')
  const [description, setDescription] = useState(
    specialization?.description ?? ''
  )
  const [nameError, setNameError] = useState<string | undefined>(undefined)
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmedName = name.trim()

    if (trimmedName === '') {
      setNameError('Введите название специализации.')
      return
    }

    setNameError(undefined)
    setFormError(null)
    setIsPending(true)

    const payload = {
      name: trimmedName,
      description: description.trim() === '' ? null : description.trim(),
    }

    try {
      if (mode === 'create') {
        await createSpecialization(payload)
        onSaved('Специализация создана')
      } else if (specialization) {
        await renameSpecialization(specialization.id, payload)
        onSaved('Специализация переименована')
      }
    } catch (cause) {
      setFormError(
        describeAdminCatalogueError(
          cause,
          mode === 'create'
            ? 'Не удалось создать специализацию. Попробуйте ещё раз.'
            : 'Не удалось переименовать специализацию. Попробуйте ещё раз.'
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
              {mode === 'create' ? 'Новая специализация' : 'Правка специализации'}
            </DialogTitle>
            <DialogDescription>
              Специализация — тег барбера: выключенная строка остаётся на карточках тех,
              кто её указал.
            </DialogDescription>
          </DialogHeader>

          <FieldGroup>
            <Field data-invalid={Boolean(nameError)}>
              <FieldLabel htmlFor="catalogue-specialization-name">
                Название
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

            <Field>
              <FieldLabel htmlFor="catalogue-specialization-description">
                Описание
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
