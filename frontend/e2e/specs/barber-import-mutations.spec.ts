import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { cancelBookingsByGuestName } from '../fixtures/api'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { BarberImportPage } from '../pages/barber-import-page'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * Перенос старых записей: `/barber` → «Перенести записи из Google Docs» → пачка.
 *
 * Сцена мутирующая: создаёт две гостевые записи через интерфейс. Их id из UI не
 * видны, поэтому уборка в `finally` ищет строки по метке в фамилии гостя и
 * отменяет их: второго прогона без пересеянной базы это не ломает.
 */

const GUEST_MARK = 'Импортов'

/** Дата-заголовок и две записи на завтра; услуга — имя услуги фикстур. */
const PASTE = [
  'завтра',
  `15:00 Анна ${GUEST_MARK}а +79990001122 E2E Стрижка`,
  `16:00 Пётр ${GUEST_MARK} +79990003344 E2E Стрижка`,
].join('\n')

test.use({ storageState: storageStatePath('barber') })

test.describe('перенос записей', () => {
  test('вставка, проверка и создание пачки', async ({ page }, testInfo) => {
    const cabinet = new CabinetPage(page)
    const screen = new BarberImportPage(page)

    try {
      // 1. Вход в перенос из кабинета: обычная ссылка, не диалог.
      await cabinet.goto()
      await expect(cabinet.heading).toBeVisible()

      await page
        .getByRole('link', { name: 'Перенести записи из Google Docs' })
        .click()

      await expect(page).toHaveURL(/\/barber\/import$/)
      await expect(screen.heading).toBeVisible()
      await expect(screen.cabinetHeading).toBeVisible()
      await expect(screen.privacyLink).toBeVisible()

      // 2. Разбор: сводка считает обе строки готовыми без ручных правок.
      await screen.parse(PASTE)

      await expect(screen.summary).toHaveText('Готово 2 из 2')
      await expect(screen.table).toBeVisible()
      await expect(screen.row(2)).toContainText('Анна')
      await expect(screen.row(3)).toContainText('Пётр')
      await expect(screen.createButton).toHaveText('Создать 2 записи')

      // 3. Доступность на 1280 и 320: карточки не дают горизонтального скролла.
      await expectNoBlockingViolations(
        page,
        '/barber/import (проверка)',
        testInfo
      )

      await page.setViewportSize({ width: 320, height: 800 })

      const overflow = await page.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth
      )

      expect(overflow).toBeLessThanOrEqual(0)
      await expect(screen.row(2)).toBeVisible()
      await expectNoBlockingViolations(page, '/barber/import @320', testInfo)

      await page.setViewportSize({ width: 1280, height: 900 })

      // 4. Создание: последовательная отправка и отчёт.
      await screen.createButton.click()

      await expect(screen.report).toHaveText('Перенесено 2 из 2 записей.')

      // 5. Обе записи на месте: «Ближайшие» кабинета.
      await screen.openBookingsButton.click()
      await expect(page).toHaveURL(/\/barber$/)
      await cabinet.openTab('Ближайшие')

      await expect(page.getByText(`Анна ${GUEST_MARK}а`).first()).toBeVisible()
      await expect(page.getByText(`Пётр ${GUEST_MARK}`).first()).toBeVisible()
    } finally {
      // 6. Уборка: отменить созданное, иначе следующий прогон увидит лишние строки.
      await cancelBookingsByGuestName(page.request, GUEST_MARK)
    }
  })
})
