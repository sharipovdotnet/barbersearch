import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { useI18n } from '@/i18n'

const RULES = [
  'Отзыв должен быть о состоявшемся визите, а не о сервисе в целом.',
  'Без оскорблений, угроз и перехода на личности.',
  'Без рекламы, ссылок и чужих персональных данных.',
  'Оценка без текста — нормальный отзыв, его не нужно скрывать.',
] as const

/** Короткая памятка, чтобы решения разных админов не расходились. */
export function ModerationRulesDialog() {
  const { t } = useI18n()

  return (
    <Dialog>
      <DialogTrigger
        render={<Button variant="ghost" size="sm" className="h-11 sm:h-7" />}
      >
        {t('Правила модерации')}
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t('Правила модерации')}</DialogTitle>
          <DialogDescription>
            {t('Очередь читается человеком: автоматических «токсичных» флагов нет.')}
          </DialogDescription>
        </DialogHeader>
        <ul className="list-disc space-y-2 pl-5 text-sm text-muted-foreground">
          {RULES.map((rule) => (
            <li key={rule}>{t(rule)}</li>
          ))}
        </ul>
        <DialogFooter>
          <DialogClose render={<Button variant="outline" size="sm" />}>
            {t('Понятно')}
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
