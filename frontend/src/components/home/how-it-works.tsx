const steps = [
  {
    title: 'Найдите барбера',
    text: 'По городу и дате: подтверждённые мастера с рейтингом, стажем и салоном.',
  },
  {
    title: 'Выберите время',
    text: 'Свободные слоты строятся по рабочему расписанию барбера в салоне.',
  },
  {
    title: 'Подтвердите запись',
    text: 'Бронь приходит в статусе Pending и подтверждается барбером.',
  },
]

/** Короткое объяснение того, как устроена запись. */
export function HowItWorks() {
  return (
    <section id="how" className="scroll-mt-14 border-y bg-muted/30">
      <div className="mx-auto grid max-w-5xl gap-6 px-4 py-10 sm:grid-cols-3">
        {steps.map((step, index) => (
          <div key={step.title} className="flex gap-3">
            <div className="flex size-8 shrink-0 items-center justify-center rounded-full bg-background text-sm font-medium ring-1 ring-foreground/10">
              {index + 1}
            </div>
            <div>
              <h2 className="font-medium">{step.title}</h2>
              <p className="mt-1 text-sm text-muted-foreground">{step.text}</p>
            </div>
          </div>
        ))}
      </div>
    </section>
  )
}
