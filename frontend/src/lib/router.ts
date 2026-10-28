import { useSyncExternalStore } from 'react'

/**
 * Минимальный клиентский роутер без зависимости от библиотеки.
 *
 * Приложение знает ровно два маршрута: витрину (`/`) и кабинет клиента (`/account`).
 * Тянуть ради этого react-router не нужно: хватает History API и подписки на `popstate`.
 * Ссылки остаются обычными `<a href>`, поэтому средний клик и «открыть в новой вкладке»
 * работают, а переход внутри приложения перехватывает `navigate`.
 */

const NAVIGATE_EVENT = 'barbersearch:navigate'

function subscribe(onStoreChange: () => void) {
  window.addEventListener('popstate', onStoreChange)
  window.addEventListener(NAVIGATE_EVENT, onStoreChange)

  return () => {
    window.removeEventListener('popstate', onStoreChange)
    window.removeEventListener(NAVIGATE_EVENT, onStoreChange)
  }
}

function readPathname() {
  const { pathname } = window.location

  if (pathname.length > 1 && pathname.endsWith('/')) {
    return pathname.slice(0, -1)
  }

  return pathname || '/'
}

/** Текущий путь без завершающего слэша; сервером не запрашивается. */
export function getPathname(): string {
  return readPathname()
}

/** Подписка на смену пути: компонент перерисовывается и на `pushState`, и на «назад». */
export function usePathname(): string {
  return useSyncExternalStore(subscribe, readPathname, () => '/')
}

function readLocation(): string {
  return `${readPathname()}${window.location.search}`
}

/**
 * Текущий адрес с query string.
 *
 * Нужен разделу администрирования: вкладка отзывов и выбранная карточка живут в URL,
 * а `usePathname` поиск сознательно не отслеживает.
 */
export function useLocation(): string {
  return useSyncExternalStore(subscribe, readLocation, () => '/')
}

/** Переход внутри приложения. Абсолютный адрес на чужой origin игнорируется. */
export function navigate(to: string, options: { replace?: boolean } = {}) {
  const url = new URL(to, window.location.origin)

  if (url.origin !== window.location.origin) {
    return
  }

  const next = `${url.pathname}${url.search}${url.hash}`
  const current = `${window.location.pathname}${window.location.search}${window.location.hash}`

  if (next === current) {
    return
  }

  if (options.replace) {
    window.history.replaceState(null, '', next)
  } else {
    window.history.pushState(null, '', next)
  }

  window.dispatchEvent(new Event(NAVIGATE_EVENT))
}

/** Прокрутка к секции витрины; `false`, если такой секции на странице нет. */
export function scrollToSection(id: string): boolean {
  const target = document.getElementById(id)
  if (!target) {
    return false
  }

  target.scrollIntoView({ behavior: 'smooth', block: 'start' })
  return true
}

/**
 * Переход к секции витрины с любой страницы.
 *
 * С витрины секция уже на экране — прокручиваем. С кабинета сначала возвращаемся на `/`
 * и ждём кадр, в котором React успел смонтировать секцию.
 */
export function goToSection(id: string) {
  if (readPathname() === '/') {
    scrollToSection(id)
    return
  }

  navigate(`/#${id}`)
  window.requestAnimationFrame(() => {
    window.requestAnimationFrame(() => {
      scrollToSection(id)
    })
  })
}
