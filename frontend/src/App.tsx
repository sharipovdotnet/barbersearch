import { MapPin, Scissors, Search, Star } from 'lucide-react'

import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button, buttonVariants } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'

const specializations = ['Fade', 'Beard', 'Coloring', 'Classic cut', 'Shave']

const steps = [
  {
    title: 'Найдите барбера',
    text: 'По салону, специализации или услуге — с ценой и ближайшими слотами.',
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

const barbers = [
  {
    name: 'Артём Соколов',
    salon: 'Салон «Топор»',
    grade: 'Top barber',
    rating: '4.9',
    reviews: 128,
    price: '2 400 ₽',
    initials: 'АС',
    tags: ['Fade', 'Beard'],
  },
  {
    name: 'Илья Морозов',
    salon: 'Салон «Бритва»',
    grade: 'Senior',
    rating: '4.8',
    reviews: 94,
    price: '1 900 ₽',
    initials: 'ИМ',
    tags: ['Classic cut'],
  },
  {
    name: 'Данила Ким',
    salon: 'Салон «Гранж»',
    grade: 'Barber',
    rating: '4.7',
    reviews: 61,
    price: '1 500 ₽',
    initials: 'ДК',
    tags: ['Coloring', 'Fade'],
  },
]

function App() {
  return (
    <div className="min-h-svh bg-background">
      <header className="sticky top-0 z-10 border-b bg-background/80 backdrop-blur">
        <div className="mx-auto flex h-14 max-w-5xl items-center gap-6 px-4">
          <a
            href="/"
            className="flex items-center gap-2 font-heading text-base font-semibold tracking-tight"
          >
            <Scissors className="size-4" aria-hidden />
            BarberSearch
          </a>
          <nav
            aria-label="Основная навигация"
            className="hidden gap-4 text-sm text-muted-foreground sm:flex"
          >
            <a className="transition-colors hover:text-foreground" href="#barbers">
              Барберы
            </a>
            <a className="transition-colors hover:text-foreground" href="#how">
              Как это работает
            </a>
          </nav>
          <a
            href="#barbers"
            className={cn(buttonVariants({ size: 'sm' }), 'ml-auto')}
          >
            Найти барбера
          </a>
        </div>
      </header>

      <main>
        <section className="mx-auto max-w-5xl px-4 pt-14 pb-10 sm:pt-20">
          <Badge variant="secondary" className="mb-4">
            Бета
          </Badge>
          <h1 className="max-w-2xl font-heading text-4xl font-semibold tracking-tight text-balance sm:text-5xl">
            Найди барбера и запишись за минуту
          </h1>
          <p className="mt-4 max-w-xl text-muted-foreground">
            Поиск по салонам, услугам и свободным слотам. Без звонков и переписок.
          </p>

          <form
            className="mt-8 flex max-w-xl flex-col gap-2 sm:flex-row"
            onSubmit={(event) => {
              event.preventDefault()
              document
                .getElementById('barbers')
                ?.scrollIntoView({ behavior: 'smooth' })
            }}
          >
            <div className="flex-1">
              <Label htmlFor="query" className="sr-only">
                Салон, барбер или услуга
              </Label>
              <Input
                id="query"
                name="query"
                placeholder="Салон, барбер или услуга"
                className="h-10"
              />
            </div>
            <Button type="submit" size="lg" className="h-10">
              <Search data-icon="inline-start" aria-hidden />
              Найти
            </Button>
          </form>

          <div className="mt-4 flex flex-wrap items-center gap-2">
            <span className="text-sm text-muted-foreground">Часто ищут:</span>
            {specializations.map((specialization) => (
              <Badge key={specialization} variant="outline">
                {specialization}
              </Badge>
            ))}
          </div>
        </section>

        <section id="how" className="border-y bg-muted/30">
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

        <section id="barbers" className="mx-auto max-w-5xl px-4 py-14">
          <div className="flex items-end justify-between gap-4">
            <div>
              <h2 className="font-heading text-2xl font-semibold tracking-tight">
                Барберы рядом
              </h2>
              <p className="mt-1 text-sm text-muted-foreground">
                Демо-данные: карточки ещё не подключены к API.
              </p>
            </div>
          </div>
          <Separator className="my-6" />

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {barbers.map((barber) => (
              <Card key={barber.name}>
                <CardHeader>
                  <div className="flex items-start gap-3">
                    <Avatar size="lg">
                      <AvatarFallback>{barber.initials}</AvatarFallback>
                    </Avatar>
                    <div className="min-w-0 flex-1">
                      <CardTitle>{barber.name}</CardTitle>
                      <CardDescription className="mt-1 flex items-center gap-1">
                        <MapPin className="size-3.5" aria-hidden />
                        {barber.salon}
                      </CardDescription>
                    </div>
                    <Badge variant="outline">{barber.grade}</Badge>
                  </div>
                </CardHeader>
                <CardContent className="space-y-3">
                  <p className="flex items-center gap-1 text-sm">
                    <Star className="size-4 fill-current" aria-hidden />
                    <span className="font-medium">{barber.rating}</span>
                    <span className="text-muted-foreground">
                      · {barber.reviews} отзывов
                    </span>
                  </p>
                  <div className="flex flex-wrap gap-1.5">
                    {barber.tags.map((tag) => (
                      <Badge key={tag} variant="secondary">
                        {tag}
                      </Badge>
                    ))}
                  </div>
                </CardContent>
                <CardFooter className="justify-between gap-3">
                  <div>
                    <div className="text-xs text-muted-foreground">от</div>
                    <div className="font-heading text-base font-medium">
                      {barber.price}
                    </div>
                  </div>
                  <Button size="sm">Забронировать</Button>
                </CardFooter>
              </Card>
            ))}
          </div>
        </section>
      </main>

      <footer className="border-t">
        <div className="mx-auto flex max-w-5xl flex-col gap-2 px-4 py-6 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
          <p>React 19 · Vite · Tailwind CSS v4 · shadcn/ui</p>
          <p>
            API:{' '}
            <code className="rounded bg-muted px-1 py-0.5 text-xs">/api</code> →
            localhost:5243
          </p>
        </div>
      </footer>
    </div>
  )
}

export default App
