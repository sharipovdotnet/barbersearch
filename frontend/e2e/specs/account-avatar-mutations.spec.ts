import { fileURLToPath } from 'node:url'

import type { Browser, BrowserContext, Page } from '@playwright/test'

import { expect, test } from '../fixtures/test'

import {
  baseURL,
  fixtures,
  storageStatePath,
} from '../fixtures/accounts'
import { deleteMyAvatar } from '../fixtures/api'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { AccountProfilePage } from '../pages/account-profile-page'

/**
 * Аватар пользователя (#85): загрузка, каркас, админская карточка и удаление.
 *
 * Сценарий идёт по мутирующему клиенту `mutations`: имя, отзывы и брони других
 * фикстур он не делит, а `seed.sql` сбрасывает его `avatar_url`. Витринного
 * барбера (`…0001`) не трогаем — на нём держатся поиск, карточка и брони.
 *
 * Файл `e2e/fixtures/portfolio-photo.png` — настоящий PNG 800×600: только так
 * `naturalWidth > 0` доказывает, что реальный файл прошёл клиентскую подготовку
 * (квадрат 512×512), multipart-загрузку и раздачу `/api/media/avatars/...`.
 *
 * Если сцена упала, `afterEach` удаляет аватар через API; успешные сцены передают
 * его следующей, иначе сцене удаления было бы нечего удалять.
 *
 * Сцены #87 добавляют админскую модерацию: админ в отдельном контексте скрывает
 * фото, владелец после reload видит инициалы и объяснение, а файл отвечает
 * `404` анониму и `200 private, no-store` админу. Упавшая сцена могла оставить
 * флаг блокировки, поэтому `afterEach` снимает его админским контекстом, а не
 * клиентом `mutations`: у владельца нет прав на этот эндпоинт.
 */
const AVATAR_PATH = fileURLToPath(
  new URL('../fixtures/portfolio-photo.png', import.meta.url)
)
const INITIALS = 'МП'
const MUTATIONS_USER_ID = fixtures.mutationsUserId
// Бейдж/кнопки аватара в админской карточке: заголовок диалога и его кнопка
// совпадают по видимому тексту, поэтому подтверждение ищется внутри `alertdialog`.
const HIDE_PHOTO = 'Скрыть фото'
const SHOW_PHOTO = 'Вернуть фото'

test.use({ storageState: storageStatePath('mutations') })

test.describe.configure({ mode: 'serial' })

test.describe('аватар кабинета: мутации', () => {
  // `src` передаётся из сцены загрузки в админскую, 320-ю и сцену удаления.
  let avatarSrc: string | null = null

  test.afterEach(async ({ browser, request }, testInfo) => {
    // Упавшая сцена могла оставить и аватар, и флаг блокировки: снимаем флаг
    // админским контекстом, затем убираем файл API-вызовом клиента.
    if (testInfo.status !== testInfo.expectedStatus) {
      await unblockAvatarAsAdmin(browser, MUTATIONS_USER_ID)
      await deleteMyAvatar(request)
    }
  })

  test('загрузка: настоящий файл, объявление, каркас и reload', async ({
    page,
  }) => {
    const profile = new AccountProfilePage(page)
    const headerImage = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-image"]'
    )
    const headerFallback = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-fallback"]'
    )

    await profile.goto()
    await profile.waitForReady()

    // Сид держит аватар пустым: сцена начинается с инициалов.
    await expect(profile.avatarFallback).toContainText(INITIALS)
    await expect(profile.avatarImage).toHaveCount(0)
    // Шапка читает тот же профиль: до загрузки — инициалы, без отдельного запроса.
    await expect(headerFallback).toHaveAttribute('data-initials', INITIALS)
    await expect(headerImage).toHaveCount(0)

    await profile.uploadAvatar(AVATAR_PATH)

    await expect(
      profile.avatarStatus.filter({ hasText: 'Аватар обновлён.' })
    ).toBeVisible()
    await expect(profile.avatarImage).toBeVisible()

    const src = await profile.avatarImage.getAttribute('src')

    expect(src).toMatch(/^\/api\/media\/avatars\//)
    avatarSrc = src

    // Файл реально отдан API, а не подставлен строкой в разметку.
    await expect
      .poll(
        () =>
          profile.avatarImage.evaluate(
            (node) => (node as HTMLImageElement).naturalWidth
          ),
        { message: 'аватар отдан API, а не только строка в ответе' }
      )
      .toBeGreaterThan(0)

    // Каркас кабинета и шапка показывают тот же кадр без перезагрузки:
    // общий `ProfileProvider` обновил всех подписчиков через `setAvatar`.
    await expect(profile.identityAvatarImage).toHaveAttribute('src', src ?? '')
    await expect(headerImage).toHaveAttribute('src', src ?? '')
    await expect(headerImage).toHaveAttribute('alt', '')

    // axe на 1280: карточка и каркас в реальном состоянии, без исключений.
    await expectNoBlockingViolations(
      page,
      '/account/profile + аватар @1280',
      test.info()
    )

    await page.reload()
    await profile.waitForReady()

    await expect(profile.avatarImage).toHaveAttribute('src', src ?? '')
    await expect(headerImage).toHaveAttribute('src', src ?? '')
  })

  test('админская карточка пользователя видит тот же src', async ({
    browser,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const adminContext = await browser.newContext({
      baseURL,
      storageState: storageStatePath('admin'),
      viewport: { width: 1280, height: 900 },
      reducedMotion: 'reduce',
    })

    try {
      const adminPage = await adminContext.newPage()

      await adminPage.goto(`/admin/users/${fixtures.mutationsUserId}`)

      const detail = adminPage.locator('#user-detail')

      await expect(detail).toBeVisible()

      const image = detail.locator('[data-slot="avatar-image"]')

      await expect(image).toHaveAttribute('src', avatarSrc ?? '')
      await expect
        .poll(() =>
          image.evaluate((node) => (node as HTMLImageElement).naturalWidth)
        )
        .toBeGreaterThan(0)
    } finally {
      await adminContext.close()
    }
  })

  test('админ скрывает фото: инициалы в кабинете и шапке, media — 404 и private', async ({
    browser,
    page,
    playwright,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const profile = new AccountProfilePage(page)
    const headerImage = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-image"]'
    )
    const headerFallback = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-fallback"]'
    )
    const adminContext = await openAdminContext(browser)

    try {
      const adminPage = await adminContext.newPage()

      await adminPage.goto(`/admin/users/${MUTATIONS_USER_ID}`)

      const detail = adminPage.locator('#user-detail')

      await expect(detail).toBeVisible()
      await expect(detail.locator('[data-slot="avatar-image"]')).toHaveAttribute(
        'src',
        avatarSrc ?? ''
      )

      await detail.getByRole('button', { name: HIDE_PHOTO }).click()

      const confirm = adminPage.getByRole('alertdialog', {
        name: 'Скрыть фото?',
      })

      await expect(confirm).toBeVisible()
      await expect(confirm).toContainText(
        'Фото исчезнет из шапки, поиска, карточек и списков броней. Аккаунт останется активным, вернуть фото можно здесь же.'
      )

      // Новый `alertdialog` — отдельный слой: axe проверяет его имя и описание.
      await expectNoBlockingViolations(
        adminPage,
        '/admin/users/{id} + диалог «Скрыть фото?» @1280',
        test.info()
      )

      await confirm
        .getByRole('button', { name: HIDE_PHOTO, exact: true })
        .click()

      await expect(
        detail.getByText('Фото скрыто', { exact: true })
      ).toBeVisible()
      await expect(
        detail.getByRole('button', { name: SHOW_PHOTO })
      ).toBeVisible()
      await expect(
        adminPage
          .getByRole('status')
          .filter({ hasText: 'Фото скрыто: его больше нет в публичных местах.' })
      ).toBeVisible()

      // Админская поверхность по-прежнему показывает сырой кадр: иначе вернуть
      // фото было бы нечем.
      await expect(detail.locator('[data-slot="avatar-image"]')).toHaveAttribute(
        'src',
        avatarSrc ?? ''
      )

      // Владелец узнаёт о блокировке при следующей загрузке профиля, а не push'ем.
      // Страница сцены новая, поэтому `goto` и есть та самая загрузка после reload.
      await profile.goto()
      await profile.waitForReady()

      await expect(profile.avatarImage).toHaveCount(0)
      await expect(profile.avatarFallback).toContainText(INITIALS)
      await expect(profile.identityAvatarImage).toHaveCount(0)
      await expect(headerImage).toHaveCount(0)
      await expect(headerFallback).toHaveAttribute('data-initials', INITIALS)

      await expect(
        profile.avatarSection.getByText('Фото скрыто администратором', {
          exact: true,
        })
      ).toBeVisible()
      await expect(
        profile.avatarSection.getByText(
          'Его не видно публично. Новое фото опубликуется сразу, а вернуть старое может только администратор.'
        )
      ).toBeVisible()
      await expect(
        profile.avatarSection.getByRole('button', {
          name: 'Загрузить другое фото',
        })
      ).toBeVisible()
      // Удаление остаётся доступным: оно снимает и URL, и флаг.
      await expect(profile.deleteAvatarButton).toBeVisible()

      // `users/me` — единственное чтение с сырым URL: иначе сохранение имени
      // в кабинете стёрло бы фото (регрессия #85).
      const me = await page.request.get('/api/users/me')

      expect(me.status(), 'GET /api/users/me').toBe(200)

      const meBody = (await me.json()) as {
        avatarUrl: string | null
        isAvatarBlocked: boolean
      }

      expect(meBody.avatarUrl).toBe(avatarSrc)
      expect(meBody.isAvatarBlocked, 'users/me отдаёт флаг блокировки').toBe(
        true
      )

      // Скрытый файл: анониму и владельцу — тот же 404, админу — 200 и private.
      const anonymous = await playwright.request.newContext({ baseURL })

      try {
        const anonymousMedia = await anonymous.get(avatarSrc ?? '')

        expect(anonymousMedia.status(), 'GET media анонимом').toBe(404)
      } finally {
        await anonymous.dispose()
      }

      expect(
        (await page.request.get(avatarSrc ?? '')).status(),
        'GET media владельцем'
      ).toBe(404)

      const adminMedia = await adminPage.request.get(avatarSrc ?? '')

      expect(adminMedia.status(), 'GET media админом').toBe(200)
      expect(
        adminMedia.headers()['cache-control'],
        'Cache-Control скрытого файла'
      ).toBe('private, no-store')

      // Бейдж и объяснение — реальное состояние карточки, а не скелетон.
      await expectNoBlockingViolations(
        page,
        '/account/profile + скрытый аватар @1280',
        test.info()
      )

      // 320 px: строка админского списка с аватаром 24 px и бейджем не должна
      // выдавливать имя и давать горизонтальный скролл (§15).
      await adminPage.setViewportSize({ width: 320, height: 800 })
      await adminPage.goto('/admin/users')
      await adminPage
        .getByLabel('Поиск по имени или email')
        .fill('e2e-mutations@barbersearch.test')

      const row = adminPage
        .locator('section[aria-label="Пользователи"] li > button')
        .filter({ hasText: 'e2e-mutations@barbersearch.test' })

      await expect(row).toHaveCount(1)
      await expect(
        row.getByText('Фото скрыто', { exact: true })
      ).toBeVisible()
      await expect(row.locator('[data-slot="avatar-image"]')).toBeVisible()

      const overflow = await adminPage.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth
      )

      expect(
        overflow,
        '/admin/users @320 со скрытым аватаром'
      ).toBeLessThanOrEqual(0)
    } finally {
      await adminContext.close()
    }
  })

  test('админ возвращает фото: кадр и публичный кэш возвращаются', async ({
    browser,
    page,
    playwright,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const profile = new AccountProfilePage(page)
    const headerImage = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-image"]'
    )
    const adminContext = await openAdminContext(browser)

    try {
      const adminPage = await adminContext.newPage()

      await adminPage.goto(`/admin/users/${MUTATIONS_USER_ID}`)

      const detail = adminPage.locator('#user-detail')

      await expect(
        detail.getByText('Фото скрыто', { exact: true })
      ).toBeVisible()
      await expect(detail.locator('[data-slot="avatar-image"]')).toHaveAttribute(
        'src',
        avatarSrc ?? ''
      )

      // Возврат обратим и подтверждения не требует — как разблокировка в портфолио.
      await detail.getByRole('button', { name: SHOW_PHOTO }).click()

      await expect(
        adminPage
          .getByRole('status')
          .filter({ hasText: 'Фото снова видно всем.' })
      ).toBeVisible()
      await expect(detail.getByText('Фото скрыто', { exact: true })).toHaveCount(
        0
      )
      await expect(
        detail.getByRole('button', { name: HIDE_PHOTO })
      ).toBeVisible()

      // Следующая сцена (320 px) ждёт видимое фото: возврат должен вернуть его
      // ещё до её начала, а не оставить кабинет с инициалами. Страница сцены
      // новая, поэтому `goto` и есть загрузка после reload.
      await profile.goto()
      await profile.waitForReady()

      await expect(profile.avatarImage).toBeVisible()
      await expect(profile.avatarImage).toHaveAttribute(
        'src',
        avatarSrc ?? ''
      )
      await expect(profile.identityAvatarImage).toHaveAttribute(
        'src',
        avatarSrc ?? ''
      )
      await expect(headerImage).toHaveAttribute('src', avatarSrc ?? '')

      const anonymous = await playwright.request.newContext({ baseURL })

      try {
        const media = await anonymous.get(avatarSrc ?? '')

        expect(media.status(), 'GET media после возврата').toBe(200)
        expect(
          media.headers()['cache-control'],
          'Cache-Control видимого файла'
        ).toBe('public, max-age=86400')
      } finally {
        await anonymous.dispose()
      }
    } finally {
      await adminContext.close()
    }
  })

  test('320 px: без горизонтального скролла, кнопки 44 px и axe', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 800 })
    await page.goto('/account/profile')

    const profile = new AccountProfilePage(page)

    await profile.waitForReady()
    await expect(profile.avatarImage).toBeVisible()

    const scrollWidth = await page.evaluate(
      () => document.documentElement.scrollWidth
    )

    expect(scrollWidth).toBeLessThanOrEqual(320)

    // Основное действие карточки — 44 px, а не десктопные 32.
    for (const button of [profile.uploadAvatarButton, profile.deleteAvatarButton]) {
      const box = await button.boundingBox()

      expect(box?.height ?? 0).toBeGreaterThanOrEqual(44)
    }

    await expectNoBlockingViolations(
      page,
      '/account/profile + аватар @320',
      test.info()
    )
  })

  test('удаление через alertdialog возвращает инициалы и отдаёт 404', async ({
    page,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const profile = new AccountProfilePage(page)
    const headerImage = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-image"]'
    )
    const headerFallback = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-fallback"]'
    )

    await profile.goto()
    await profile.waitForReady()
    await expect(profile.avatarImage).toBeVisible()
    await expect(headerImage).toBeVisible()

    await profile.openAvatarDeleteDialog()

    await expect(profile.deleteAvatarDialog).toContainText(
      'Аватар исчезнет из карточки и кабинета. Файл удаляется безвозвратно.'
    )

    await profile.confirmAvatarDelete()

    await expect(
      profile.avatarStatus.filter({ hasText: 'Аватар удалён.' })
    ).toBeVisible()
    await expect(profile.deleteAvatarDialog).toBeHidden()
    await expect(profile.avatarImage).toHaveCount(0)
    await expect(profile.avatarFallback).toContainText(INITIALS)
    await expect(profile.identityAvatarImage).toHaveCount(0)
    // Шапка тоже сразу возвращается к инициалам, без перезагрузки.
    await expect(headerImage).toHaveCount(0)
    await expect(headerFallback).toHaveAttribute('data-initials', INITIALS)

    const response = await page.request.get(avatarSrc ?? '')

    expect(response.status(), 'GET старый /api/media/avatars/...').toBe(404)
  })
})

/**
 * Отдельный админский контекст.
 *
 * Админ не должен делить cookie с клиентом `mutations`: иначе сцена проверяла бы
 * админскую раздачу скрытого файла от лица владельца.
 */
function openAdminContext(browser: Browser): Promise<BrowserContext> {
  return browser.newContext({
    baseURL,
    storageState: storageStatePath('admin'),
    viewport: { width: 1280, height: 900 },
    reducedMotion: 'reduce',
  })
}

/** CSRF-токен контекста: cookie-мутация без него получает `400`. */
async function adminCsrfToken(page: Page): Promise<string | null> {
  const response = await page.request.get('/api/auth/csrf')

  if (response.status() !== 200) {
    return null
  }

  const body = (await response.json()) as { token: string }

  return body.token
}

/**
 * Уборка после упавшей сцены: снять флаг блокировки может только администратор.
 *
 * Клиента `mutations` для этого недостаточно — у владельца нет прав на
 * `DELETE api/admin/users/{userId}/avatar/block`. Повторный `DELETE` идемпотентен.
 */
async function unblockAvatarAsAdmin(
  browser: Browser,
  userId: string
): Promise<void> {
  const context = await browser.newContext({
    baseURL,
    storageState: storageStatePath('admin'),
  })

  try {
    const page = await context.newPage()
    const token = await adminCsrfToken(page)

    if (token === null) {
      return
    }

    const response = await page.request.delete(
      `/api/admin/users/${userId}/avatar/block`,
      { headers: { 'X-CSRF-TOKEN': token } }
    )

    expect(
      [204, 404],
      `DELETE api/admin/users/${userId}/avatar/block: ${response.status()}`
    ).toContain(response.status())
  } finally {
    await context.close()
  }
}
