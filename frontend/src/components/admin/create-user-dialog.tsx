import { useState, type FormEvent } from 'react'
import {
  Check,
  Copy,
  Loader2,
  Plus,
  RefreshCw,
} from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH } from '@/lib/auth'
import {
  ADMIN_USER_ROLE_LABELS,
  ADMIN_USER_ROLES,
  createUser,
  describeCreateUserError,
  type AdminUserRole,
} from '@/lib/admin-users'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

// Имена в домене короткие; длиннее сервер всё равно не примет.
const MAX_NAME_LENGTH = 100

const GENERATED_PASSWORD_LENGTH = 16

// Похожие символы (0/O, 1/l/I) исключены: пароль диктуют голосом.
const PASSWORD_ALPHABET =
  'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*-_=+'

type CreateUserErrors = {
  email?: string
  firstName?: string
  lastName?: string
  password?: string
}

type CreatedUser = {
  email: string
  role: AdminUserRole
  password: string
}

/**
 * Криптостойкий пароль без сторонних библиотек.
 *
 * Отбраковка значений больше `limit` убирает смещение по модулю: иначе первые
 * символы алфавита выпадали бы чаще остальных.
 */
function randomIndex(bound: number): number {
  const limit = Math.floor(0x1_0000_0000 / bound) * bound
  const buffer = new Uint32Array(1)

  for (;;) {
    crypto.getRandomValues(buffer)
    const value = buffer[0]

    if (value < limit) {
      return value % bound
    }
  }
}

function generatePassword(): string {
  let password = ''

  for (let index = 0; index < GENERATED_PASSWORD_LENGTH; index += 1) {
    password += PASSWORD_ALPHABET.charAt(
      randomIndex(PASSWORD_ALPHABET.length)
    )
  }

  return password
}

function validate(input: {
  email: string
  firstName: string
  lastName: string
  password: string
}): CreateUserErrors {
  const errors: CreateUserErrors = {}

  if (!input.email) {
    errors.email = 'Введите email.'
  } else if (!EMAIL_PATTERN.test(input.email)) {
    errors.email = 'Похоже, в адресе опечатка.'
  }

  if (!input.firstName) {
    errors.firstName = 'Введите имя.'
  } else if (input.firstName.length > MAX_NAME_LENGTH) {
    errors.firstName = `Максимум ${MAX_NAME_LENGTH} символов.`
  }

  if (!input.lastName) {
    errors.lastName = 'Введите фамилию.'
  } else if (input.lastName.length > MAX_NAME_LENGTH) {
    errors.lastName = `Максимум ${MAX_NAME_LENGTH} символов.`
  }

  if (!input.password) {
    errors.password = 'Введите или сгенерируйте пароль.'
  } else if (input.password.length < MIN_PASSWORD_LENGTH) {
    errors.password = `Минимум ${MIN_PASSWORD_LENGTH} символов.`
  } else if (input.password.length > MAX_PASSWORD_LENGTH) {
    errors.password = `Максимум ${MAX_PASSWORD_LENGTH} символов.`
  }

  return errors
}

/** Копирование с запасным путём: clipboard недоступен вне secure context. */
async function copyText(value: string): Promise<boolean> {
  try {
    if (navigator.clipboard) {
      await navigator.clipboard.writeText(value)
      return true
    }
  } catch {
    // Падение clipboard не должно ломать панель успеха — пробуем ниже.
  }

  return copyWithTextarea(value)
}

function copyWithTextarea(value: string): boolean {
  const area = document.createElement('textarea')
  area.value = value
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.opacity = '0'
  document.body.append(area)
  area.select()

  let isCopied = false

  try {
    isCopied = document.execCommand('copy')
  } catch {
    isCopied = false
  }

  area.remove()
  return isCopied
}

/**
 * Создание пользователя администратором.
 *
 * Пароль задаёт админ: отправки писем в сервисе нет, поэтому после создания он
 * показывается один раз с кнопкой копирования. Содержимое диалога монтируется
 * заново на каждое открытие, поэтому после закрытия пароль стирается из состояния
 * вместе с размонтированным компонентом.
 */
export function CreateUserDialog({ onCreated }: { onCreated?: () => void }) {
  const [isOpen, setIsOpen] = useState(false)

  return (
    <Dialog open={isOpen} onOpenChange={setIsOpen}>
      <DialogTrigger render={<Button className="h-11 sm:h-9" />}>
        <Plus data-icon="inline-start" aria-hidden />
        Создать пользователя
      </DialogTrigger>
      {isOpen && (
        <DialogContent className="sm:max-w-md">
          <CreateUserForm
            onCreated={onCreated}
            onClose={() => setIsOpen(false)}
          />
        </DialogContent>
      )}
    </Dialog>
  )
}

function CreateUserForm({
  onCreated,
  onClose,
}: {
  onCreated?: () => void
  onClose: () => void
}) {
  const [email, setEmail] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [role, setRole] = useState<AdminUserRole>('Customer')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<CreateUserErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)
  const [created, setCreated] = useState<CreatedUser | null>(null)

  const isPasswordInvalid = Boolean(errors.password)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const normalizedEmail = email.trim()
    const normalizedFirstName = firstName.trim()
    const normalizedLastName = lastName.trim()
    const nextErrors = validate({
      email: normalizedEmail,
      firstName: normalizedFirstName,
      lastName: normalizedLastName,
      password,
    })

    setErrors(nextErrors)
    setFormError(null)

    if (Object.keys(nextErrors).length > 0) {
      return
    }

    setIsPending(true)

    try {
      await createUser({
        email: normalizedEmail,
        firstName: normalizedFirstName,
        lastName: normalizedLastName,
        role,
        password,
      })

      setCreated({
        email: normalizedEmail,
        role,
        password,
      })
      onCreated?.()
    } catch (cause) {
      setFormError(describeCreateUserError(cause))
    } finally {
      setIsPending(false)
    }
  }

  if (created) {
    return <CreateUserSuccess created={created} onClose={onClose} />
  }

  return (
    <form
      className="flex flex-col gap-4"
      noValidate
      aria-busy={isPending}
      onSubmit={handleSubmit}
    >
      <DialogHeader>
        <DialogTitle>Новый пользователь</DialogTitle>
        <DialogDescription>
          Аккаунт создаётся сразу активным. Отправки писем нет: временный пароль
          придётся передать вручную.
        </DialogDescription>
      </DialogHeader>

      <FieldGroup>
        <Field data-invalid={Boolean(errors.email)}>
          <FieldLabel htmlFor="create-user-email">Email</FieldLabel>
          <Input
            id="create-user-email"
            name="email"
            type="email"
            autoComplete="off"
            inputMode="email"
            spellCheck={false}
            placeholder="user@example.com"
            value={email}
            disabled={isPending}
            aria-invalid={Boolean(errors.email)}
            aria-describedby={
              errors.email ? 'create-user-email-error' : undefined
            }
            onChange={(event) => {
              setEmail(event.target.value)
              setErrors((current) => ({ ...current, email: undefined }))
              setFormError(null)
            }}
          />
          <FieldError id="create-user-email-error">{errors.email}</FieldError>
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field data-invalid={Boolean(errors.firstName)}>
            <FieldLabel htmlFor="create-user-first-name">Имя</FieldLabel>
            <Input
              id="create-user-first-name"
              name="firstName"
              type="text"
              autoComplete="off"
              value={firstName}
              disabled={isPending}
              aria-invalid={Boolean(errors.firstName)}
              aria-describedby={
                errors.firstName ? 'create-user-first-name-error' : undefined
              }
              onChange={(event) => {
                setFirstName(event.target.value)
                setErrors((current) => ({ ...current, firstName: undefined }))
                setFormError(null)
              }}
            />
            <FieldError id="create-user-first-name-error">
              {errors.firstName}
            </FieldError>
          </Field>

          <Field data-invalid={Boolean(errors.lastName)}>
            <FieldLabel htmlFor="create-user-last-name">Фамилия</FieldLabel>
            <Input
              id="create-user-last-name"
              name="lastName"
              type="text"
              autoComplete="off"
              value={lastName}
              disabled={isPending}
              aria-invalid={Boolean(errors.lastName)}
              aria-describedby={
                errors.lastName ? 'create-user-last-name-error' : undefined
              }
              onChange={(event) => {
                setLastName(event.target.value)
                setErrors((current) => ({ ...current, lastName: undefined }))
                setFormError(null)
              }}
            />
            <FieldError id="create-user-last-name-error">
              {errors.lastName}
            </FieldError>
          </Field>
        </div>

        <Field>
          <FieldLabel htmlFor="create-user-role">Роль</FieldLabel>
          <select
            id="create-user-role"
            name="role"
            value={role}
            disabled={isPending}
            onChange={(event) => {
              setRole(event.target.value as AdminUserRole)
              setFormError(null)
            }}
            className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-8"
          >
            {ADMIN_USER_ROLES.map((value) => (
              <option key={value} value={value}>
                {ADMIN_USER_ROLE_LABELS[value]}
              </option>
            ))}
          </select>
        </Field>

        <Field data-invalid={isPasswordInvalid}>
          <FieldLabel htmlFor="create-user-password">Пароль</FieldLabel>
          <div className="flex flex-col gap-2 sm:flex-row">
            <Input
              id="create-user-password"
              name="password"
              type="text"
              autoComplete="new-password"
              spellCheck={false}
              value={password}
              disabled={isPending}
              aria-invalid={isPasswordInvalid}
              aria-describedby={
                isPasswordInvalid
                  ? 'create-user-password-error'
                  : 'create-user-password-hint'
              }
              onChange={(event) => {
                setPassword(event.target.value)
                setErrors((current) => ({ ...current, password: undefined }))
                setFormError(null)
              }}
            />
            <Button
              type="button"
              variant="outline"
              className="h-11 shrink-0 sm:h-8"
              disabled={isPending}
              onClick={() => {
                setPassword(generatePassword())
                setErrors((current) => ({ ...current, password: undefined }))
                setFormError(null)
              }}
            >
              <RefreshCw data-icon="inline-start" aria-hidden />
              Сгенерировать
            </Button>
          </div>
          <FieldDescription id="create-user-password-hint">
            От {MIN_PASSWORD_LENGTH} до {MAX_PASSWORD_LENGTH} символов. Пароль
            показывается открыто: его нужно передать пользователю.
          </FieldDescription>
          <FieldError id="create-user-password-error">
            {errors.password}
          </FieldError>
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
          className="h-11 sm:h-9"
          disabled={isPending}
          onClick={onClose}
        >
          Отмена
        </Button>
        <Button
          type="submit"
          className="h-11 sm:h-9"
          disabled={isPending}
        >
          {isPending && (
            <Loader2
              data-icon="inline-start"
              aria-hidden
              className="animate-spin"
            />
          )}
          {isPending ? 'Создаём…' : 'Создать пользователя'}
        </Button>
      </DialogFooter>
    </form>
  )
}

function CreateUserSuccess({
  created,
  onClose,
}: {
  created: CreatedUser
  onClose: () => void
}) {
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>(
    'idle'
  )

  async function handleCopy() {
    const isCopied = await copyText(created.password)
    setCopyState(isCopied ? 'copied' : 'failed')
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Пользователь создан</DialogTitle>
        <DialogDescription>
          Пользователь {created.email} создан с ролью «
          {ADMIN_USER_ROLE_LABELS[created.role]}». Передайте пароль безопасным
          каналом — больше он не покажется.
        </DialogDescription>
      </DialogHeader>

      <div className="flex flex-col gap-2">
        <label htmlFor="created-user-password" className="text-sm font-medium">
          Временный пароль
        </label>
        <Input
          id="created-user-password"
          readOnly
          value={created.password}
          className="font-mono"
          onFocus={(event) => event.currentTarget.select()}
        />
        <Button
          type="button"
          variant="outline"
          className="h-11 w-full sm:h-9"
          autoFocus
          onClick={() => {
            void handleCopy()
          }}
        >
          {copyState === 'copied' ? (
            <Check data-icon="inline-start" aria-hidden />
          ) : (
            <Copy data-icon="inline-start" aria-hidden />
          )}
          {copyState === 'copied' ? 'Пароль скопирован' : 'Скопировать пароль'}
        </Button>
        <p role="status" className="text-xs text-muted-foreground">
          {copyState === 'copied'
            ? 'Пароль в буфере обмена. Передайте его безопасным каналом.'
            : copyState === 'failed'
              ? 'Не удалось скопировать. Выделите пароль и скопируйте вручную.'
              : 'Скопируйте пароль сейчас: после закрытия окна он не сохранится.'}
        </p>
      </div>

      <DialogFooter>
        <Button type="button" className="h-11 sm:h-9" onClick={onClose}>
          Готово
        </Button>
      </DialogFooter>
    </>
  )
}
