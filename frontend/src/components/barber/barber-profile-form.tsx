import { useState, type FormEvent } from 'react'
import { Loader2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { t, useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  BARBER_GRADE_LABELS,
  BARBER_GRADES,
  type BarberGrade,
} from '@/lib/barber-grade'
import {
  BARBER_PROFILE_BIO_MAX,
  BARBER_PROFILE_DISPLAY_NAME_MAX,
  BARBER_PROFILE_EXPERIENCE_MAX,
  BARBER_PROFILE_EXPERIENCE_MIN,
  BARBER_PROFILE_LATE_POLICY_MAX,
  createBarberProfile,
  describeBarberProfileCreateError,
  type BarberProfileDraft,
  type BarberProfileField,
} from '@/lib/barber-profile-create'
import {
  LANGUAGE_LABELS,
  LANGUAGE_ORDER,
  PAYMENT_METHOD_LABELS,
  PAYMENT_METHOD_ORDER,
  type Language,
  type PaymentMethod,
} from '@/lib/barber-profile-labels'
import { cn } from '@/lib/utils'

/**
 * Форма профиля барбера.
 *
 * Компонент без побочных эффектов: он только собирает и проверяет значения и отдаёт
 * готовый профиль наверх. Ни сессии, ни навигации здесь нет — после `201` claim
 * `barber_profile_id` ещё старый, и продлением сессии занимается экран, который знает,
 * куда вернуться. Такую же форму переиспользует редактирование профиля.
 *
 * Клиентские правила совпадают с доменными: имя 1..120, опыт 1..70, минимум один язык
 * и выбранный грейд. `languages` пустым не отправляется, хотя сервер подставил бы `Ru`:
 * человек должен понимать, что именно попадёт в карточку. `Grade.None` в выборе нет:
 * барбер выбирает настоящий грейд, а сбросить его в «Без грейда» может администратор.
 *
 * Одобренная заявка «Стать барбером» — второй источник тех же трёх значений. Всё, что
 * в ней заполнено, приходит сюда готовым и помечается `readOnly`: одно и то же нельзя
 * спрашивать дважды. Пустое «О себе» в заявке — не данные, поэтому поле остаётся
 * редактируемым: иначе профиль навсегда остался бы без описания.
 */

type FormValues = {
  displayName: string
  yearsOfExperience: string
  grade: BarberGrade | ''
  bio: string
  languages: Language[]
  paymentMethods: PaymentMethod[]
  certificates: string
  achievements: string
  communicationFormat: string
  goodies: string
  additionalServices: string
  latePolicy: string
}

type ProfileErrors = Partial<Record<BarberProfileField, string>>

const INITIAL_VALUES: FormValues = {
  displayName: '',
  yearsOfExperience: '',
  grade: '',
  bio: '',
  languages: [],
  paymentMethods: [],
  certificates: '',
  achievements: '',
  communicationFormat: '',
  goodies: '',
  additionalServices: '',
  latePolicy: '',
}

// `None` — «грейд не присвоен»: это административное состояние, а не выбор барбера.
const SELECTABLE_GRADES = BARBER_GRADES.filter((grade) => grade !== 'None')

// Клиентские тексты совпадают с серверными кодами: одно правило — одна формулировка.
const DISPLAY_NAME_REQUIRED_ERROR = 'Укажите, как вас представить.'
const DISPLAY_NAME_TOO_LONG_ERROR = 'Не длиннее 120 символов.'
const EXPERIENCE_REQUIRED_ERROR = 'Укажите опыт работы в годах.'
const EXPERIENCE_RANGE_ERROR = 'Опыт — от 1 до 70 лет.'
const GRADE_REQUIRED_ERROR = 'Выберите грейд.'
const LANGUAGES_REQUIRED_ERROR = 'Выберите хотя бы один язык.'

const IDS = {
  displayName: 'barber-profile-display-name',
  displayNameError: 'barber-profile-display-name-error',
  displayNameLocked: 'barber-profile-display-name-locked',
  yearsOfExperience: 'barber-profile-experience',
  yearsOfExperienceError: 'barber-profile-experience-error',
  yearsOfExperienceLocked: 'barber-profile-experience-locked',
  grade: 'barber-profile-grade',
  gradeError: 'barber-profile-grade-error',
  bio: 'barber-profile-bio',
  bioLocked: 'barber-profile-bio-locked',
  languagesError: 'barber-profile-languages-error',
  paymentMethodsError: 'barber-profile-payment-methods-error',
  certificates: 'barber-profile-certificates',
  achievements: 'barber-profile-achievements',
  communicationFormat: 'barber-profile-communication',
  goodies: 'barber-profile-goodies',
  additionalServices: 'barber-profile-additional-services',
  latePolicy: 'barber-profile-late-policy',
} as const

const LOCKED_HINT = 'Перенесено из заявки — изменить нельзя.'

export type BarberProfilePrefill = {
  displayName: string
  yearsOfExperience: number
  /** `null` — в заявке «О себе» не заполняли, поле остаётся редактируемым. */
  bio: string | null
}

/** Непустое значение из заявки: пустая строка — не данные, а отсутствие ответа. */
function isFilled(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim() !== ''
}

/**
 * Начальные значения: обязательные имя и опыт всегда перекрываются заявкой, а
 * «О себе» — только когда оно в ней есть. Вычисляется при монтировании, поэтому
 * форма должна создаваться после загрузки заявки (см. `barber-onboarding-page`).
 */
function toInitialValues(
  prefill: BarberProfilePrefill | null | undefined
): FormValues {
  if (!prefill) {
    return INITIAL_VALUES
  }

  return {
    ...INITIAL_VALUES,
    displayName: prefill.displayName,
    yearsOfExperience: String(prefill.yearsOfExperience),
    bio: isFilled(prefill.bio) ? prefill.bio : '',
  }
}

/** `aria-describedby` из подсказки и ошибки: пустая строка не нужна. */
function describedBy(
  ...ids: (string | null | undefined)[]
): string | undefined {
  const value = ids.filter((id) => id !== null && id !== undefined).join(' ')

  return value === '' ? undefined : value
}

export type BarberProfileFormProps = {
  /** Профиль создан сервером: экран продлевает сессию и уходит в кабинет. */
  onCreated: (id: string) => void
  /** `409`: профиль уже есть (например, вкладку открыли дважды) — это тоже успех. */
  onAlreadyExists: () => void
  /**
   * Данные одобренной заявки. Заполненные в ней имя и опыт приходят готовыми и только
   * для чтения; «О себе» блокируется, только если заявитель его написал.
   */
  prefill?: BarberProfilePrefill | null
}

/** `''` — не заполнено, иначе целое число из поля ввода. */
function parseExperience(value: string): number | null {
  const trimmed = value.trim()

  if (!/^\d+$/.test(trimmed)) {
    return null
  }

  const years = Number(trimmed)

  return Number.isInteger(years) ? years : null
}

function validate(values: FormValues, years: number | null): ProfileErrors {
  const errors: ProfileErrors = {}
  const displayName = values.displayName.trim()

  if (!displayName) {
    errors.displayName = t(DISPLAY_NAME_REQUIRED_ERROR)
  } else if (displayName.length > BARBER_PROFILE_DISPLAY_NAME_MAX) {
    errors.displayName = t(DISPLAY_NAME_TOO_LONG_ERROR)
  }

  if (years === null) {
    errors.yearsOfExperience = t(EXPERIENCE_REQUIRED_ERROR)
  } else if (
    years < BARBER_PROFILE_EXPERIENCE_MIN ||
    years > BARBER_PROFILE_EXPERIENCE_MAX
  ) {
    errors.yearsOfExperience = t(EXPERIENCE_RANGE_ERROR)
  }

  if (values.languages.length === 0) {
    errors.languages = t(LANGUAGES_REQUIRED_ERROR)
  }

  if (values.grade === '') {
    errors.grade = t(GRADE_REQUIRED_ERROR)
  }

  return errors
}

/** «По одному пункту в строке»: обрезаем и выбрасываем пустые строки. */
function toLines(value: string): string[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
}

function toNullable(value: string): string | null {
  const trimmed = value.trim()

  return trimmed === '' ? null : trimmed
}

function toDraft(
  values: FormValues,
  years: number,
  grade: BarberGrade
): BarberProfileDraft {
  return {
    displayName: values.displayName.trim(),
    yearsOfExperience: years,
    grade,
    bio: toNullable(values.bio),
    languages: values.languages,
    certificatesAndTraining: toLines(values.certificates),
    achievements: toLines(values.achievements),
    paymentMethods: values.paymentMethods,
    communicationFormat: toLines(values.communicationFormat),
    goodies: toLines(values.goodies),
    additionalServices: toLines(values.additionalServices),
    latePolicy: toNullable(values.latePolicy),
  }
}

function toggle<T>(list: readonly T[], value: T): T[] {
  return list.includes(value)
    ? list.filter((item) => item !== value)
    : [...list, value]
}

export function BarberProfileForm({
  onCreated,
  onAlreadyExists,
  prefill = null,
}: BarberProfileFormProps) {
  const { t } = useI18n()
  const [values, setValues] = useState<FormValues>(() =>
    toInitialValues(prefill)
  )
  const [errors, setErrors] = useState<ProfileErrors>({})
  const [formError, setFormError] = useState<{
    message: string
    canRetry: boolean
  } | null>(null)
  const [isPending, setIsPending] = useState(false)

  const isDisplayNameLocked = isFilled(prefill?.displayName)
  const isExperienceLocked =
    prefill !== null && prefill.yearsOfExperience > 0
  const isBioLocked = isFilled(prefill?.bio)

  function setValue<K extends keyof FormValues>(
    key: K,
    value: FormValues[K]
  ): void {
    setValues((current) => ({ ...current, [key]: value }))
    setFormError(null)

    if (
      key === 'displayName' ||
      key === 'yearsOfExperience' ||
      key === 'grade' ||
      key === 'languages' ||
      key === 'paymentMethods'
    ) {
      setErrors((current) => ({ ...current, [key]: undefined }))
    }
  }

  async function submit(): Promise<void> {
    // Двойной клик и Enter после ответа не должны отправить второй профиль.
    if (isPending) {
      return
    }

    const years = parseExperience(values.yearsOfExperience)
    const nextErrors = validate(values, years)

    setErrors(nextErrors)
    setFormError(null)

    if (
      Object.keys(nextErrors).length > 0 ||
      years === null ||
      values.grade === ''
    ) {
      return
    }

    setIsPending(true)

    try {
      const created = await createBarberProfile(
        toDraft(values, years, values.grade)
      )
      onCreated(created.id)
    } catch (error) {
      // Профиль уже создан: это не ошибка, а гонка двух вкладок.
      if (
        error instanceof ApiError &&
        error.hasCode('barberProfile.create.alreadyExists')
      ) {
        onAlreadyExists()
        return
      }

      const failure = describeBarberProfileCreateError(error)
      const field = failure.field

      if (field) {
        setErrors((current) => ({ ...current, [field]: failure.message }))
      } else {
        setFormError({
          message: failure.message,
          canRetry: failure.canRetry,
        })
      }

      setIsPending(false)
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    await submit()
  }

  return (
    <form
      className="flex flex-col gap-5"
      noValidate
      aria-busy={isPending}
      onSubmit={(event) => {
        void handleSubmit(event)
      }}
    >
      <FieldGroup>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field data-invalid={Boolean(errors.displayName)}>
            <FieldLabel htmlFor={IDS.displayName}>
              {t('Как вас представить')}
            </FieldLabel>
            <Input
              id={IDS.displayName}
              name="displayName"
              type="text"
              autoComplete="nickname"
              maxLength={BARBER_PROFILE_DISPLAY_NAME_MAX}
              value={values.displayName}
              readOnly={isDisplayNameLocked}
              className={cn(isDisplayNameLocked && 'bg-muted/50 dark:bg-muted/40')}
              disabled={isPending}
              aria-invalid={Boolean(errors.displayName)}
              aria-describedby={describedBy(
                isDisplayNameLocked ? IDS.displayNameLocked : undefined,
                errors.displayName ? IDS.displayNameError : undefined
              )}
              onChange={(event) => {
                setValue('displayName', event.target.value)
              }}
            />
            {isDisplayNameLocked && (
              <FieldDescription id={IDS.displayNameLocked}>
                {t(LOCKED_HINT)}
              </FieldDescription>
            )}
            <FieldError id={IDS.displayNameError}>
              {errors.displayName}
            </FieldError>
          </Field>

          <Field data-invalid={Boolean(errors.yearsOfExperience)}>
            <FieldLabel htmlFor={IDS.yearsOfExperience}>
              {t('Опыт, лет')}
            </FieldLabel>
            <Input
              id={IDS.yearsOfExperience}
              name="yearsOfExperience"
              type="number"
              inputMode="numeric"
              min={BARBER_PROFILE_EXPERIENCE_MIN}
              max={BARBER_PROFILE_EXPERIENCE_MAX}
              value={values.yearsOfExperience}
              readOnly={isExperienceLocked}
              className={cn(isExperienceLocked && 'bg-muted/50 dark:bg-muted/40')}
              disabled={isPending}
              aria-invalid={Boolean(errors.yearsOfExperience)}
              aria-describedby={describedBy(
                isExperienceLocked ? IDS.yearsOfExperienceLocked : undefined,
                errors.yearsOfExperience
                  ? IDS.yearsOfExperienceError
                  : undefined
              )}
              onChange={(event) => {
                setValue('yearsOfExperience', event.target.value)
              }}
            />
            {isExperienceLocked && (
              <FieldDescription id={IDS.yearsOfExperienceLocked}>
                {t(LOCKED_HINT)}
              </FieldDescription>
            )}
            <FieldError id={IDS.yearsOfExperienceError}>
              {errors.yearsOfExperience}
            </FieldError>
          </Field>
        </div>

        <Field data-invalid={Boolean(errors.grade)}>
          <FieldLabel htmlFor={IDS.grade}>{t('Грейд')}</FieldLabel>
          <select
            id={IDS.grade}
            name="grade"
            value={values.grade}
            disabled={isPending}
            aria-invalid={Boolean(errors.grade)}
            aria-describedby={describedBy(
              errors.grade ? IDS.gradeError : undefined
            )}
            onChange={(event) => {
              setValue('grade', event.target.value as BarberGrade | '')
            }}
            className="h-11 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 sm:h-8"
          >
            <option value="" disabled>
              {t('Выберите грейд')}
            </option>
            {SELECTABLE_GRADES.map((grade) => (
              <option key={grade} value={grade}>
                {t(BARBER_GRADE_LABELS[grade])}
              </option>
            ))}
          </select>
          <FieldDescription>
            {t(
              'Грейд видят клиенты в карточке. Администратор может изменить его позже.'
            )}
          </FieldDescription>
          <FieldError id={IDS.gradeError}>{errors.grade}</FieldError>
        </Field>

        <Field>
          <FieldLabel htmlFor={IDS.bio}>{t('О себе')}</FieldLabel>
          <Textarea
            id={IDS.bio}
            name="bio"
            rows={4}
            maxLength={BARBER_PROFILE_BIO_MAX}
            value={values.bio}
            readOnly={isBioLocked}
            className={cn(isBioLocked && 'bg-muted/50 dark:bg-muted/40')}
            disabled={isPending}
            aria-describedby={isBioLocked ? IDS.bioLocked : undefined}
            onChange={(event) => {
              setValue('bio', event.target.value)
            }}
          />
          {isBioLocked ? (
            <FieldDescription id={IDS.bioLocked}>
              {t(LOCKED_HINT)}
            </FieldDescription>
          ) : (
            <FieldDescription>
              {t(
                'Пара предложений о себе: опыт, подход к работе, фирменная деталь.'
              )}
            </FieldDescription>
          )}
        </Field>

        <FieldSet
          aria-describedby={errors.languages ? IDS.languagesError : undefined}
        >
          <FieldLegend>{t('Языки общения')}</FieldLegend>
          <div className="grid gap-3 sm:grid-cols-2">
            {LANGUAGE_ORDER.map((language) => (
              <Field key={language} orientation="horizontal">
                <Checkbox
                  id={`barber-profile-language-${language}`}
                  checked={values.languages.includes(language)}
                  disabled={isPending}
                  aria-invalid={Boolean(errors.languages)}
                  aria-describedby={
                    errors.languages ? IDS.languagesError : undefined
                  }
                  onCheckedChange={() => {
                    setValue(
                      'languages',
                      toggle(values.languages, language)
                    )
                  }}
                />
                <FieldLabel
                  htmlFor={`barber-profile-language-${language}`}
                  className="font-normal"
                >
                  {LANGUAGE_LABELS[language]}
                </FieldLabel>
              </Field>
            ))}
          </div>
          <FieldDescription>
            {t('Клиент увидит языки в карточке. Выберите хотя бы один.')}
          </FieldDescription>
          <FieldError id={IDS.languagesError}>{errors.languages}</FieldError>
        </FieldSet>

        <FieldSet
          aria-describedby={
            errors.paymentMethods ? IDS.paymentMethodsError : undefined
          }
        >
          <FieldLegend>{t('Способы оплаты')}</FieldLegend>
          <div className="grid gap-3 sm:grid-cols-2">
            {PAYMENT_METHOD_ORDER.map((method) => (
              <Field key={method} orientation="horizontal">
                <Checkbox
                  id={`barber-profile-payment-${method}`}
                  checked={values.paymentMethods.includes(method)}
                  disabled={isPending}
                  aria-invalid={Boolean(errors.paymentMethods)}
                  aria-describedby={
                    errors.paymentMethods
                      ? IDS.paymentMethodsError
                      : undefined
                  }
                  onCheckedChange={() => {
                    setValue(
                      'paymentMethods',
                      toggle(values.paymentMethods, method)
                    )
                  }}
                />
                <FieldLabel
                  htmlFor={`barber-profile-payment-${method}`}
                  className="font-normal"
                >
                  {t(PAYMENT_METHOD_LABELS[method])}
                </FieldLabel>
              </Field>
            ))}
          </div>
          <FieldDescription>
            {t('Необязательно: можно оставить пустым и указать позже.')}
          </FieldDescription>
          <FieldError id={IDS.paymentMethodsError}>
            {errors.paymentMethods}
          </FieldError>
        </FieldSet>

        <ListField
          id={IDS.certificates}
          label={t('Сертификаты и обучение')}
          description={t(
            'По одному пункту в строке: курсы, школы, дипломы.'
          )}
          value={values.certificates}
          disabled={isPending}
          onChange={(value) => {
            setValue('certificates', value)
          }}
        />

        <ListField
          id={IDS.achievements}
          label={t('Достижения')}
          description={t(
            'По одному пункту в строке: конкурсы, стаж, публикации.'
          )}
          value={values.achievements}
          disabled={isPending}
          onChange={(value) => {
            setValue('achievements', value)
          }}
        />

        <ListField
          id={IDS.communicationFormat}
          label={t('Формат общения')}
          description={t(
            'По одному пункту в строке: «Люблю поболтать», «Могу помолчать».'
          )}
          value={values.communicationFormat}
          disabled={isPending}
          onChange={(value) => {
            setValue('communicationFormat', value)
          }}
        />

        <ListField
          id={IDS.goodies}
          label={t('Комфорт в кресле')}
          description={t(
            'По одному пункту в строке: кофе, музыка, журналы.'
          )}
          value={values.goodies}
          disabled={isPending}
          onChange={(value) => {
            setValue('goodies', value)
          }}
        />

        <ListField
          id={IDS.additionalServices}
          label={t('Дополнительные услуги')}
          description={t(
            'По одному пункту в строке: бритьё головы, камуфляж седины, уход за бородой.'
          )}
          value={values.additionalServices}
          disabled={isPending}
          onChange={(value) => {
            setValue('additionalServices', value)
          }}
        />

        <Field>
          <FieldLabel htmlFor={IDS.latePolicy}>{t('Опоздания')}</FieldLabel>
          <Textarea
            id={IDS.latePolicy}
            name="latePolicy"
            rows={2}
            maxLength={BARBER_PROFILE_LATE_POLICY_MAX}
            value={values.latePolicy}
            disabled={isPending}
            onChange={(event) => {
              setValue('latePolicy', event.target.value)
            }}
          />
          <FieldDescription>
            {t('Например, «Жду максимум 10 минут». Необязательно.')}
          </FieldDescription>
        </Field>
      </FieldGroup>

      {formError && (
        <div className="flex flex-col gap-2 rounded-lg border border-destructive/40 bg-destructive/5 px-3 py-2">
          <p role="alert" className="text-sm text-destructive">
            {formError.message}
          </p>
          {formError.canRetry && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="h-11 w-fit sm:h-8"
              disabled={isPending}
              onClick={() => {
                void submit()
              }}
            >
              {t('Повторить')}
            </Button>
          )}
        </div>
      )}

      <Button
        type="submit"
        className="h-11 w-full sm:h-8 sm:w-auto"
        disabled={isPending}
        aria-busy={isPending}
      >
        {isPending && (
          <Loader2
            data-icon="inline-start"
            aria-hidden
            className="animate-spin"
          />
        )}
        {isPending ? t('Создаём профиль…') : t('Создать профиль')}
      </Button>
    </form>
  )
}

/** Необязательное поле-список: одна строка — один пункт, пустые строки отбрасываются. */
function ListField({
  id,
  label,
  description,
  value,
  disabled,
  onChange,
}: {
  id: string
  label: string
  description: string
  value: string
  disabled: boolean
  onChange: (value: string) => void
}) {
  return (
    <Field>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Textarea
        id={id}
        name={id}
        rows={3}
        value={value}
        disabled={disabled}
        onChange={(event) => {
          onChange(event.target.value)
        }}
      />
      <FieldDescription>{description}</FieldDescription>
    </Field>
  )
}
