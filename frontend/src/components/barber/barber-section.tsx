import { CardHeader } from '@/components/ui/card'

/** Заголовок секции карточки барбера: один ритм у всех блоков профиля. */
export function BarberSectionHeading({
  id,
  children,
}: {
  id: string
  children: string
}) {
  return (
    <CardHeader>
      <h2
        id={id}
        className="font-heading text-base leading-snug font-medium tracking-tight"
      >
        {children}
      </h2>
    </CardHeader>
  )
}
