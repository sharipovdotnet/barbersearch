import { Component, type ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import { navigate } from '@/lib/router'

type AppErrorBoundaryProps = {
  children: ReactNode
}

type AppErrorBoundaryState = {
  hasError: boolean
}

/**
 * Граница ошибок рендера вокруг страницы.
 *
 * Это единственный класс в приложении: `getDerivedStateFromError` нельзя выразить
 * хуком, поэтому §10.4 делает для него исключение. Страховка стоит внутри `<main>`,
 * а не вокруг всего приложения, — шапка и подвал остаются рабочими, и пользователь
 * может уйти на другой адрес.
 *
 * Сброс состояния делается снаружи: `App` вешает `key` по адресу страницы, поэтому
 * переход на другой маршрут монтирует новую границу с чистым состоянием.
 */
export class AppErrorBoundary extends Component<
  AppErrorBoundaryProps,
  AppErrorBoundaryState
> {
  state: AppErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): AppErrorBoundaryState {
    return { hasError: true }
  }

  /** Снимает ошибку и уводит на витрину; повторное падение снова поймает граница. */
  private handleGoHome = () => {
    this.setState({ hasError: false })
    navigate('/')
  }

  render() {
    if (!this.state.hasError) {
      return this.props.children
    }

    return (
      <div className="mx-auto flex min-h-[60vh] w-full max-w-md flex-col items-center justify-center gap-4 px-4 py-10 text-center">
        <h1 className="text-xl font-semibold tracking-tight">
          Страница не открылась
        </h1>
        <p className="text-sm text-muted-foreground">
          Что-то сломалось при отображении этого раздела. Ваши данные не
          пострадали — обновите страницу или вернитесь на главную.
        </p>
        <div className="flex flex-wrap justify-center gap-2">
          <Button onClick={() => window.location.reload()}>
            Обновить страницу
          </Button>
          <Button variant="ghost" onClick={this.handleGoHome}>
            На главную
          </Button>
        </div>
      </div>
    )
  }
}
