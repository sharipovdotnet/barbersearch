import { writeFileSync } from 'node:fs'

import { chromium } from 'playwright'

const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5173'
const LOG_PATH = 'verification/issue-96-shots.txt'

// Те же аккаунты, что в `e2e/fixtures/accounts.ts`. Входим заново через API,
// а не полагаемся на `storageState`: после других прогонов сессия ротируется,
// и файл `.auth/*.json` может быть уже недействительным.
const admin = {
  email: process.env.E2E_ADMIN_EMAIL ?? 'e2e-admin@barbersearch.test',
  password: process.env.E2E_ADMIN_PASSWORD ?? 'e2e-password',
}
const profileBarber = {
  email:
    process.env.E2E_PROFILE_BARBER_EMAIL ??
    'e2e-profile-barber@barbersearch.test',
  password: process.env.E2E_PROFILE_BARBER_PASSWORD ?? 'e2e-password',
}

// Профиль барбера и услуга из `e2e/fixtures/accounts.ts` / `seed.sql`.
const PROFILE_ID = '9e100000-0000-4000-8000-000000000015'
const SERVICE_NAME = 'E2E Стрижка'
const FIXTURE_PRICE = 2000
const TARGET_PRICE = 2100

const lines = []

function log(message) {
  lines.push(message)
  console.log(message)
}

/** Вход через API: antiforgery-cookie → `POST /api/auth/login/cookie`. */
async function signIn(context, account) {
  const csrf = await context.request.get('/api/auth/csrf')

  if (csrf.status() !== 200) {
    throw new Error(`GET /api/auth/csrf: ${csrf.status()}`)
  }

  const { token } = await csrf.json()

  const login = await context.request.post('/api/auth/login/cookie', {
    headers: { 'X-CSRF-TOKEN': token },
    data: {
      email: account.email,
      password: account.password,
      rememberMe: false,
    },
  })

  if (login.status() !== 204) {
    throw new Error(
      `POST /api/auth/login/cookie для ${account.email}: ${login.status()}`
    )
  }
}

/**
 * Меняет цену услуги через диалог кабинета барбера — тот же путь, что в сцене
 * #96 (`PUT /api/barber-profiles/{id}/services/{hairServiceId}`).
 *
 * Нужен только как страховка: если в журнале почему-то нет строки с ценой,
 * снимок делается после настоящей правки, а не по чужой строке.
 */
async function setPriceAsBarber(browser, price) {
  const context = await browser.newContext({
    baseURL,
    viewport: { width: 1280, height: 900 },
    reducedMotion: 'reduce',
  })

  try {
    await signIn(context, profileBarber)

    const profileResponse = await context.request.get(
      `/api/barber-profiles/${PROFILE_ID}`
    )

    if (profileResponse.status() !== 200) {
      throw new Error(
        `GET /api/barber-profiles/{id}: ${profileResponse.status()}`
      )
    }

    const profile = await profileResponse.json()

    if (!profile.services.some((service) => service.name === SERVICE_NAME)) {
      throw new Error(
        `в прайсе ${profileBarber.email} нет «${SERVICE_NAME}» — сначала прогони мутационную сцену barber-profile-mutations`
      )
    }

    const page = await context.newPage()

    await page.goto('/barber/profile')
    await page.getByRole('heading', { name: 'Профиль', level: 1 }).waitFor()

    const row = page
      .getByRole('list', { name: 'Услуги', exact: true })
      .getByRole('listitem')
      .filter({ hasText: SERVICE_NAME })

    await row.waitFor()
    await row.getByRole('button', { name: 'Изменить цену' }).click()

    const dialog = page.getByRole('dialog', { name: 'Изменить цену' })

    await dialog.waitFor()
    await dialog.getByLabel('Цена, ₽').fill(String(price))
    await dialog.getByRole('button', { name: 'Сохранить' }).click()
    await page
      .getByRole('status')
      .filter({ hasText: 'Цена обновлена.' })
      .waitFor({ timeout: 15_000 })
  } finally {
    await context.close()
  }
}

/** Открывает журнал и включает фильтр «Услуга барбера», как в сцене. */
async function openJournalWithBarberService(page) {
  await page.goto('/admin/audit')

  // Информационная плашка cookie стоит в потоке — убираем её перед снимком.
  const cookieDismiss = page.getByRole('button', { name: 'Понятно' })
  if (await cookieDismiss.count()) {
    await cookieDismiss.click()
  }

  // Ждём ответ именно на выбранный тип: до фильтра на экране строки «Все сущности».
  await Promise.all([
    page.waitForResponse((response) => {
      const url = new URL(response.url())

      return (
        url.pathname === '/api/admin/audit' &&
        url.searchParams.get('entityType') === 'barber_service' &&
        response.status() === 200
      )
    }),
    page.getByRole('combobox', { name: 'Тип сущности' }).selectOption({
      label: 'Услуга барбера',
    }),
  ])

  return page.getByRole('list', { name: 'Записи журнала' }).getByRole('listitem')
}

/**
 * Идёт по строкам сверху вниз, раскрывает diff и возвращает первую строку
 * с изменением цены.
 *
 * После полного прогона мутационной спеки выше лежат строки `isActive`
 * («Скрыть» и «Вернуть»), поэтому «первая строка» — ещё не цена. Панель diff'а
 * скрыта атрибутом `hidden` и не читается через `innerText`, пока строка
 * не раскрыта: отсюда клик по каждой строке, а не фильтр по DOM.
 */
async function findPriceRow(rows) {
  const count = await rows.count()

  for (let index = 0; index < count; index += 1) {
    const row = rows.nth(index)
    const toggle = row.getByRole('button', { name: 'Показать изменения' })

    if ((await toggle.count()) === 0) {
      continue
    }

    await toggle.click()

    const diff = row.locator('dl')

    if ((await diff.count()) === 0) {
      await row.getByRole('button', { name: 'Скрыть изменения' }).click()
      continue
    }

    await diff.waitFor({ state: 'visible' })

    const text = await diff.innerText()

    if (text.includes('Цена') && /2\s*000/.test(text) && /2\s*100/.test(text)) {
      return { row, diff, text }
    }

    // Не наша строка — возвращаем журнал в исходное состояние и идём ниже.
    await row.getByRole('button', { name: 'Скрыть изменения' }).click()
  }

  return null
}

const browser = await chromium.launch()
let adminContext = null
let priceChanged = false

try {
  adminContext = await browser.newContext({
    baseURL,
    viewport: { width: 1280, height: 900 },
    reducedMotion: 'reduce',
  })

  await signIn(adminContext, admin)
  log(`admin: ${admin.email}`)

  for (const [label, width] of [
    ['1280', 1280],
    ['320', 320],
  ]) {
    const height = width === 320 ? 800 : 900
    const page = await adminContext.newPage()

    await page.setViewportSize({ width, height })

    let rows = await openJournalWithBarberService(page)
    let found = await findPriceRow(rows)

    if (found === null && !priceChanged) {
      // Страховка: строки с ценой нет (например, базу пересеяли после сцены).
      // Сначала ставим цену фикстуры, потом целевую — в журнале появляется
      // честный diff «2 000 ₽ → 2 100 ₽», а не чужая строка.
      log(`${label}: строки с diff цены нет — меняю цену через кабинет барбера`)
      await setPriceAsBarber(browser, FIXTURE_PRICE)
      await setPriceAsBarber(browser, TARGET_PRICE)
      priceChanged = true

      rows = await openJournalWithBarberService(page)
      found = await findPriceRow(rows)
    }

    if (found === null) {
      throw new Error(
        `${label}: в журнале нет строки «Услуга барбера» с diff «Цена: 2 000 ₽ → 2 100 ₽»`
      )
    }

    const { row, diff, text: diffText } = found
    const rowText = (await row.innerText()).replace(/\n+/g, ' | ')
    const layout = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }))

    log(
      `${label}: scrollWidth=${layout.scrollWidth} clientWidth=${layout.clientWidth} horizontalScroll=${layout.scrollWidth > layout.clientWidth}`
    )
    log(`${label}: row = ${rowText}`)
    log(`${label}: diff = ${diffText.replace(/\n+/g, ' | ')}`)

    if (width === 320 && layout.scrollWidth !== layout.clientWidth) {
      throw new Error(
        `320: горизонтальная прокрутка ${layout.scrollWidth} > ${layout.clientWidth}`
      )
    }

    await row.scrollIntoViewIfNeeded()
    await page.waitForTimeout(200)
    await page.screenshot({
      path: `verification/issue-96-audit-diff-${label}.png`,
      fullPage: false,
    })

    await page.close()
  }
} finally {
  if (priceChanged) {
    try {
      await setPriceAsBarber(browser, FIXTURE_PRICE)
      log('цена возвращена к 2 000 ₽')
    } catch (error) {
      log(`не удалось вернуть цену: ${error.message}`)
    }
  }

  await adminContext?.close()
  await browser.close()
  writeFileSync(LOG_PATH, `${lines.join('\n')}\n`, 'utf8')
}
