import { chromium } from 'playwright'

const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5173'
const browser = await chromium.launch()

for (const [label, width] of [
  ['1280', 1280],
  ['320', 320],
]) {
  const context = await browser.newContext({
    baseURL,
    // Аккаунт `mutations`: его единственный барбер B1 задан в сиде с неделей
    // 09:00–18:00, поэтому в диалоге есть настоящие слоты, а не «не работает».
    storageState: 'e2e/.auth/mutations.json',
    viewport: { width, height: width === 320 ? 800 : 900 },
    reducedMotion: 'reduce',
  })
  const page = await context.newPage()

  await page.goto('/account')

  const cookieDismiss = page.getByRole('button', { name: 'Понятно' })
  if (await cookieDismiss.count()) {
    await cookieDismiss.click()
  }

  const panel = page.locator('#bookings-panel')
  const row = panel
    .locator('li')
    .filter({ has: page.getByRole('button', { name: /^Перенести запись/ }) })
    .first()
  await row.waitFor()
  await row.getByRole('button', { name: /^Перенести запись/ }).click()

  const dialog = page.getByRole('dialog', { name: 'Перенести запись' })
  await dialog.waitFor()

  // Сетка грузится запросом `reschedule-slots`: ждём честный итог дня.
  const status = dialog.getByRole('status')
  await status.filter({ hasText: /Свободно|В этот день|Свободного/ }).waitFor({
    timeout: 15_000,
  })

  const slotCount = await dialog.getByRole('radio').count()
  const layout = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }))

  console.log(
    `${label}: slots=${slotCount} status=${JSON.stringify(await status.innerText())} scrollWidth=${layout.scrollWidth} clientWidth=${layout.clientWidth} horizontalScroll=${layout.scrollWidth > layout.clientWidth}`
  )

  await page.screenshot({
    path: `verification/issue-95-dialog-${label}.png`,
    fullPage: false,
  })

  await context.close()
}

await browser.close()
