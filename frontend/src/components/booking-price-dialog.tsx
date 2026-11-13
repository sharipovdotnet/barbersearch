import { useState } from 'react'
import { Loader2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
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
import { formatPrice } from '@/lib/format'

/**
 * Максимум суммы визита: предел `numeric(10,2)`, как в домене.
 *
 * Клиент повторяет серверную границу, чтобы опечатка в 9 цифрах не уходила
 * запросом и не возвращалась английским `detail`.
 */
export const MAX_BOOKING_PRICE = 99_999_999.99

/** Не больше двух знаков после запятой: база молча округлила бы `1500.999`. */
const PRICE_PATTERN = /^\d+(?:\.\d{1,2})?$/

/**
 * Диалог правки цены визита — общий для кабинета барбера и карточки администратора.
 *
 * Повторяет `PriceDialog` прайса: одно поле, текущее значение видно, сохранение
 * недоступно при неизменной или невалидной сумме. Содержимое монтируется на каждое
 * открытие (родитель рендерит компонент только при выбранной брони), поэтому ошибка
 * прошлой строки не появляется над следующей.
 */
export function BookingPriceDialog({
  description,
  currentPrice,
  isPending,
  error,
  onSubmit,
  onClose,
}: {
  /** `{when} · {customerName}` — контекст визита под заголовком. */
  description: string
  currentPrice: number
  isPending: boolean
  /** Текст ошибки сервера; `null`, пока её нет. */
  error: string | null
  onSubmit: (price: number) => void
  onClose: () => void
}) {
  const { t } = useI18n()
  const [value, setValue] = useState(String(currentPrice))

  const trimmed = value.trim()
  const parsed = Number(trimmed)
  const isShapeValid =
    trimmed !== '' && PRICE_PATTERN.test(trimmed) && Number.isFinite(parsed)
  const isInRange = isShapeValid && parsed >= 0 && parsed <= MAX_BOOKING_PRICE
  const isChanged = isShapeValid && parsed !== currentPrice
  const canSubmit = isInRange && isChanged && !isPending

  function handleSubmit(): void {
    if (!canSubmit) {
      return
    }

    onSubmit(parsed)
  }

  return (
    <Dialog
      open
      onOpenChange={(next) => {
        // Пока запрос в полёте, диалог не закрывается: результат должен быть виден.
        if (!next && !isPending) {
          onClose()
        }
      }}
    >
      <DialogContent>
        <form
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault()
            handleSubmit()
          }}
        >
          <DialogHeader>
            <DialogTitle>{t('Изменить цену визита')}</DialogTitle>
            <DialogDescription>{description}</DialogDescription>
          </DialogHeader>

          <div className="space-y-1.5">
            <Label htmlFor="booking-price">{t('Цена, ₽')}</Label>
            <Input
              id="booking-price"
              type="number"
              min={0}
              step="0.01"
              inputMode="decimal"
              value={value}
              className="h-11 sm:h-8"
              disabled={isPending}
              onChange={(event) => {
                setValue(event.target.value)
              }}
            />
            <p className="text-xs text-muted-foreground">
              {t('Сейчас: {price}.', { price: formatPrice(currentPrice) })}
            </p>
            <p className="text-xs text-muted-foreground">
              {t('Клиент увидит новую цену в своих записях.')}
            </p>
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
              disabled={isPending}
              onClick={onClose}
            >
              {t('Отмена')}
            </Button>
            <Button
              type="submit"
              className="h-11 sm:h-8"
              disabled={!canSubmit}
              aria-busy={isPending}
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
        </form>
      </DialogContent>
    </Dialog>
  )
}
