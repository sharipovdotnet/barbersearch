import { expect, type Locator, type Page } from '@playwright/test'

import { ListPagerComponent } from './list-pager-component'

/**
 * `/admin/photos` — очередь премодерации фото: «Аватары» и «Портфолио».
 *
 * У строки нет карточки: решение принимается прямо в списке, поэтому page object
 * описывает строку (превью, имя, email, подпись), два решения и диалог причины.
 * Вкладка живёт в `?tab=`, поэтому `goto(tab)` и `selectTab(tab)` ходят по адресам
 * `/admin/photos` и `/admin/photos?tab=portfolio` — ровно так же, как ссылки экрана.
 *
 * Кнопки решения подписаны видимым словом и названием кадра (WCAG 2.5.3): у аватара
 * это имя владельца, у портфолио — имя барбера и подпись кадра. Название строки
 * может меняться параллельным мутирующим спеком (профиль кабинета правит имя),
 * поэтому кнопки ищутся **внутри уже найденной строки** по началу имени, а не по
 * полному `aria-label` с именем. Само имя остаётся частью контракта: `approve`
 * проверяет, что кнопка начинается с «Одобрить», а не «Одобрить что-то ещё».
 *
 * Числа очереди не проверяются: соседний мутирующий спек может успеть добавить
 * строку, поэтому спеки ищут свою строку по имени или email и проверяют её
 * состояние, а не размер списка.
 */
export type PhotosTab = 'avatars' | 'portfolio'

/** Адрес вкладки: значение по умолчанию в ссылку не попадает (как в `admin-path`). */
export function photosPath(tab: PhotosTab = 'avatars'): string {
  return tab === 'avatars' ? '/admin/photos' : '/admin/photos?tab=portfolio'
}

export class AdminPhotosPage {
  readonly page: Page
  readonly pager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.pager = new ListPagerComponent(page, 'Страницы фотографий')
  }

  /**
   * Открывает вкладку и ждёт ответ её чтения: скелетон первой загрузки тоже
   * `role="status"`, поэтому ждать его бессмысленно — ждём реальный `200`.
   */
  async goto(tab: PhotosTab = 'avatars'): Promise<void> {
    await Promise.all([
      this.page.waitForResponse(
        (response) =>
          new URL(response.url()).pathname === this.listPath(tab)
          && response.status() === 200
      ),
      this.page.goto(photosPath(tab)),
    ])
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Фотографии', level: 1 })
  }

  get subtitle(): Locator {
    return this.page.getByText(
      'Очередь премодерации: новые аватары и работы не видны публично, пока вы их не одобрите. Откройте кадр крупно и примите решение.'
    )
  }

  get tablist(): Locator {
    return this.page.getByRole('tablist', { name: 'Какие фотографии показать' })
  }

  tab(name: string): Locator {
    return this.tablist.getByRole('tab', { name, exact: true })
  }

  get panel(): Locator {
    return this.page.locator('#photos-panel')
  }

  /** Поиск: `label` скрыт визуально, но остаётся программным именем поля. */
  get search(): Locator {
    return this.page.getByLabel('Поиск по имени или email')
  }

  get refresh(): Locator {
    return this.page.locator('main header').getByRole('button', {
      name: /^(Обновить|Обновляем…)$/,
    })
  }

  get list(): Locator {
    return this.page.locator('section[aria-label="Очередь фотографий"]')
  }

  get rows(): Locator {
    return this.list.getByRole('listitem')
  }

  get emptyQueue(): Locator {
    return this.panel.getByText('Очередь пуста — все фото проверены.', {
      exact: true,
    })
  }

  get emptySearch(): Locator {
    return this.panel.getByText('Ничего не найдено', { exact: true })
  }

  /** Строка очереди по имени владельца/барбера или по email. */
  row(nameOrEmail: string): Locator {
    return this.rows.filter({ hasText: nameOrEmail })
  }

  /**
   * Превью кадра в строке: `aria-label` — «Открыть фото крупно: <название>».
   * Начало имени проверяется, хвост с именем — нет: имя могло измениться.
   */
  previewButton(row: Locator): Locator {
    return row.getByRole('button', { name: /^Открыть фото крупно: / })
  }

  /** «Одобрить» в строке: `aria-label` — «Одобрить: <название кадра>». */
  approveButton(row: Locator): Locator {
    return row.getByRole('button', { name: /^Одобрить: / })
  }

  /** «Отклонить» в строке: `aria-label` — «Отклонить: <название кадра>». */
  rejectButton(row: Locator): Locator {
    return row.getByRole('button', { name: /^Отклонить: / })
  }

  /** Объявление экрана: «Фото одобрено.» / «Фото отклонено.». */
  announcement(message: string): Locator {
    return this.page.getByRole('status').filter({ hasText: message })
  }

  get error(): Locator {
    return this.page.getByRole('alert')
  }

  /**
   * Диалог кадра крупно. Его имя — название кадра (имя владельца или барбера),
   * поэтому ищем по роли: название строки может измениться параллельным спеком.
   */
  get previewDialog(): Locator {
    return this.page.getByRole('dialog')
  }

  /** Диалог отказа: `alertdialog` с обязательной причиной. */
  get rejectDialog(): Locator {
    return this.page.getByRole('alertdialog', { name: 'Отклонить фото?' })
  }

  get rejectionReason(): Locator {
    return this.rejectDialog.getByLabel('Причина отказа')
  }

  get rejectConfirm(): Locator {
    return this.rejectDialog.getByRole('button', {
      name: 'Отклонить',
      exact: true,
    })
  }

  get rejectCancel(): Locator {
    return this.rejectDialog.getByRole('button', { name: 'Отмена' })
  }

  /**
   * Переключить вкладку её ссылкой и дождаться чтения новой выборки.
   * Ссылка настоящая (`href`), поэтому клик — обычный SPA-переход.
   */
  async selectTab(tab: PhotosTab): Promise<void> {
    const name = tab === 'avatars' ? 'Аватары' : 'Портфолио'

    await Promise.all([
      this.page.waitForResponse(
        (response) =>
          new URL(response.url()).pathname === this.listPath(tab)
          && response.status() === 200
      ),
      this.tab(name).click(),
    ])

    await expect(this.page).toHaveURL(
      tab === 'avatars' ? /\/admin\/photos$/ : /\/admin\/photos\?tab=portfolio$/
    )
    await expect(this.tab(name)).toHaveAttribute('aria-selected', 'true')
  }

  /**
   * Поиск с задержкой 300 мс: ждём ответ именно с этой строкой, иначе можно
   * поймать ответ первичной загрузки без `search`.
   */
  async searchFor(text: string): Promise<void> {
    await Promise.all([
      this.page.waitForResponse((response) => {
        if (response.status() !== 200) {
          return false
        }

        const url = new URL(response.url())

        return (
          url.pathname.startsWith('/api/admin/photos/')
          && url.searchParams.get('search') === text
        )
      }),
      this.search.fill(text),
    ])
  }

  /** «Одобрить» в строке: строка исчезает, объявление говорит об успехе. */
  async approve(row: Locator): Promise<void> {
    await this.approveButton(row).click()
  }

  /**
   * «Отклонить» с причиной: открывает `alertdialog` и заполняет причину,
   * но не подтверждает. Текст последствия остаётся на проверку вызывающему тесту.
   */
  async openReject(row: Locator): Promise<Locator> {
    await this.rejectButton(row).click()
    await expect(this.rejectDialog).toBeVisible()

    return this.rejectDialog
  }

  async rejectWithReason(row: Locator, reason: string): Promise<void> {
    await this.openReject(row)
    await this.rejectionReason.fill(reason)
    await this.rejectConfirm.click()
  }

  /** Чтение вкладки: по нему `goto`/`selectTab` понимают, что список пришёл. */
  private listPath(tab: PhotosTab): string {
    return `/api/admin/photos/${tab}`
  }
}
