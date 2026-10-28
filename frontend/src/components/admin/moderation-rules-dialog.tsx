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

const RULES = [
  'Отзыв должен быть о состоявшемся визите, а не о сервисе в целом.',
  'Без оскорблений, угроз и перехода на личности.',
  'Без рекламы, ссылок и чужих персональных данных.',
  'Оценка без текста — нормальный отзыв, его не нужно скрывать.',
] as const

/** Короткая памятка, чтобы решения разных админов не расходились. */
export function ModerationRulesDialog() {
  return (
    <Dialog>
      <DialogTrigger
        render={<Button variant="ghost" size="sm" className="h-11 sm:h-7" />}
      >
        Правила модерации
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Правила модерации</DialogTitle>
          <DialogDescription>
            Очередь читается человеком: автоматических «токсичных» флагов нет.
          </DialogDescription>
        </DialogHeader>
        <ul className="list-disc space-y-2 pl-5 text-sm text-muted-foreground">
          {RULES.map((rule) => (
            <li key={rule}>{rule}</li>
          ))}
        </ul>
        <DialogFooter>
          <DialogClose render={<Button variant="outline" size="sm" />}>
            Понятно
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
