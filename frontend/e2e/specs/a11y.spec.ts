import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { ProfilePage } from '../pages/profile-page'

/**
 * A11y-аудит (§4.6): только `critical` и `serious` валят прогон.
 *
 * `moderate`/`minor` попадают в отчёт Playwright (`html`), но CI не блокируют.
 * Сканируем после загрузки страницы, а не в промежуточном скелетоне.
 */
const blockingImpacts = new Set(['critical', 'serious'])

/**
 * Известные находки, заведённые долгом §14 #27.
 *
 * Правило не отключается: находки остаются в отчёте (`axe-known`), но не валят
 * прогон, пока долг открыт (§9: исключение — только с номером долга в §14).
 * Любой другой critical/serious по-прежнему блокирует.
 */
const knownViolations: Record<string, readonly string[]> = {
  // Доступное имя кнопок пагинатора и строк очереди не содержит видимый текст
  // (WCAG 2.5.3): видимые «Назад»/«Дальше» и составная подпись строки.
  'label-content-name-mismatch': ['/barbers', '/admin/reviews'],
  // На узком экране очередь рендерится без панели детали, но `aria-controls`
  // у строк по-прежнему указывает на отсутствующий `#review-detail`.
  'aria-valid-attr-value': ['/admin/reviews'],
}

function isKnown(screen: string, id: string): boolean {
  return knownViolations[id]?.some((prefix) => screen.startsWith(prefix)) === true
}

async function expectNoBlockingViolations(page: Page, screen: string): Promise<void> {
  const results = await new AxeBuilder({ page }).analyze()

  const blocking = results.violations.filter(
    (violation) => violation.impact !== null && violation.impact !== undefined &&
      blockingImpacts.has(violation.impact)
  )

  const known = blocking.filter((violation) => isKnown(screen, violation.id))
  const unknown = blocking.filter((violation) => !isKnown(screen, violation.id))

  for (const violation of known) {
    test.info().annotations.push({
      type: 'axe-known',
      description: `${screen}: ${violation.id} (${violation.impact}), ${violation.nodes.length} узл. — §14 #27`,
    })
  }

  const summary = unknown
    .map(
      (violation) =>
        `${violation.id} (${violation.impact}): ${violation.help} — ${violation.nodes
          .map((node) => node.target.join(' '))
          .slice(0, 3)
          .join(', ')}`
    )
    .join('\n')

  expect(unknown, `${screen}: axe critical/serious\n${summary}`).toEqual([])
}

test.describe('axe: публичные экраны', () => {
  test.use({ storageState: { cookies: [], origins: [] } })

  test('витрина /', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

    await expectNoBlockingViolations(page, '/')
  })

  test('профиль барбера с отзывами', async ({ page }) => {
    const profile = new ProfilePage(page)

    await profile.goto()
    await profile.waitForFirstPage()
    await expect(profile.heading).toBeVisible()

    await expectNoBlockingViolations(page, `/barbers/${fixtures.barberProfileId}`)
  })

  test('открытый диалог входа', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('button', { name: 'Войти' }).first().click()

    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()

    await expectNoBlockingViolations(page, 'LoginDialog')
  })
})

test.describe('axe: админка', () => {
  test.use({ storageState: storageStatePath('admin') })

  test('очередь отзывов', async ({ page }) => {
    await page.goto('/admin/reviews')
    await expect(page.getByRole('heading', { name: 'Отзывы', level: 1 })).toBeVisible()
    await expect(page.locator('section#reviews-list')).toBeVisible()

    await expectNoBlockingViolations(page, '/admin/reviews')
  })
})

test.describe('клавиатура', () => {
  test.use({ storageState: { cookies: [], origins: [] } })

  test('Tab до пагинатора, Enter и Пробел листают, фокус-кольцо видно', async ({ page }) => {
    const profile = new ProfilePage(page)

    await profile.goto()
    await profile.waitForFirstPage()

    const next = profile.pager.next

    // Обход по Tab, а не программный focus(): проверяем реальный порядок табуляции.
    let reached = false

    for (let i = 0; i < 250; i += 1) {
      await page.keyboard.press('Tab')

      if (await next.evaluate((element) => element === document.activeElement)) {
        reached = true
        break
      }
    }

    expect(reached, 'кнопка «Дальше» достижима с клавиатуры').toBe(true)
    await expect(next).toBeFocused()

    // Кольцо фокуса видно: у кнопки сохранён :focus-visible (§11).
    const focusVisible = await next.evaluate((element) => element.matches(':focus-visible'))

    expect(focusVisible, 'фокус-кольцо видно на кнопке пагинатора').toBe(true)

    await page.keyboard.press('Enter')
    await profile.pager.expectRange('11–12 из 12')

    // После смены страницы фокус уходит на список; Tab возвращает к пагинатору.
    await expect(profile.reviewsList).toBeFocused()

    const previous = profile.pager.previous
    let backToPager = false

    for (let i = 0; i < 10; i += 1) {
      await page.keyboard.press('Tab')

      if (await previous.evaluate((element) => element === document.activeElement)) {
        backToPager = true
        break
      }
    }

    expect(backToPager, 'Tab от списка возвращает к пагинатору').toBe(true)

    await page.keyboard.press('Space')
    await profile.pager.expectRange('1–10 из 12')
  })

  test('диалог входа закрывается Escape и возвращает фокус на триггер', async ({ page }) => {
    await page.goto('/')

    const trigger = page.getByRole('button', { name: 'Войти' }).first()
    await trigger.click()

    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()

    await page.keyboard.press('Escape')

    await expect(dialog).toBeHidden()
    await expect(trigger).toBeFocused()
  })
})
