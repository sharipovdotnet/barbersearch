import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { BarberNotificationsPage } from '../pages/barber-notifications-page'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber/notifications` — мутирующие сцены (§8).
 *
 * Проект `mutations-chromium` идёт последним, поэтому читающие спеки видят
 * исходные «1 непрочитанное» и `leadMinutes = 30`. Файл `serial`: обе сцены
 * меняют состояние одного и того же барбера.
 *
 * `read-all` необратим, а `seed.sql` возвращает «1 непрочитанное» и 08:00/30,
 * поэтому повторный прогон без пересева ожидает `npm run test:e2e:seed` — как
 * и у остальных мутирующих сцен.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe.configure({ mode: 'serial' })

test.describe('уведомления барбера: мутации', () => {
  test('«Прочитать все» гасит бейдж и точки, повторный вызов идемпотентен', async ({
    page,
  }) => {
    const notifications = new BarberNotificationsPage(page)

    await notifications.goto()
    await notifications.waitForReady()

    await expect(notifications.unreadDots).toHaveCount(1)
    await expect(notifications.markAllButton).toBeEnabled()

    const readAll = page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname ===
          '/api/barber/notifications/read-all' &&
        response.request().method() === 'POST'
    )

    await notifications.markAllButton.click()

    expect((await readAll).status(), 'POST read-all').toBe(204)
    await expect(notifications.announcement).toBeVisible()
    await expect(notifications.unreadDots).toHaveCount(0)
    await expect(notifications.markAllButton).toBeDisabled()

    // Повторный вызов сервер выполняет идемпотентно: 204, ничего не меняется.
    const csrf = await page.request.get('/api/auth/csrf')

    expect(csrf.status(), 'GET /api/auth/csrf').toBe(200)

    const { token } = (await csrf.json()) as { token: string }

    for (let attempt = 0; attempt < 2; attempt += 1) {
      const repeat = await page.request.post(
        '/api/barber/notifications/read-all',
        { headers: { 'X-CSRF-TOKEN': token } }
      )

      expect(repeat.status(), 'повторный POST read-all').toBe(204)
    }

    // Перезагрузка читает серверное состояние, а не только локальную отметку.
    await page.reload()
    await notifications.waitForReady()
    await expect(notifications.unreadDots).toHaveCount(0)
    await expect(notifications.markAllButton).toBeDisabled()

    // Колокольчик в кабинете: бейджа «1» больше нет.
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await expect(cabinet.notificationsBell).toHaveAttribute(
      'aria-label',
      'Уведомления, непрочитанных: 0'
    )
    await expect(
      cabinet.notificationsBell.getByText('1', { exact: true })
    ).toHaveCount(0)
  })

  test('leadMinutes = 60 сохраняется одной PUT и переживает перезагрузку', async ({
    page,
  }) => {
    const notifications = new BarberNotificationsPage(page)

    await notifications.goto()
    await notifications.waitForReady()

    await expect(notifications.leadSelect).toHaveValue('30')

    const save = page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname ===
          '/api/barber/notification-settings' &&
        response.request().method() === 'PUT'
    )

    await notifications.leadSelect.selectOption('60')

    const response = await save

    expect(response.status(), 'PUT notification-settings').toBe(200)

    const body: Record<string, unknown> = response.request().postDataJSON()

    expect(body, 'тело PUT несёт выбранные 60 минут').toMatchObject({
      leadMinutes: 60,
    })
    await expect(notifications.leadSelect).toHaveValue('60')

    await page.reload()
    await notifications.waitForReady()
    await expect(notifications.leadSelect).toHaveValue('60')

    // Возврат к серверному дефолту: `read-all` этим не откатить, но настройки
    // сцена не оставляет за собой даже при прогоне без пересева.
    const restore = page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname ===
          '/api/barber/notification-settings' &&
        response.request().method() === 'PUT'
    )

    await notifications.leadSelect.selectOption('30')

    expect((await restore).status(), 'возврат leadMinutes = 30').toBe(200)
    await expect(notifications.leadSelect).toHaveValue('30')
  })
})
