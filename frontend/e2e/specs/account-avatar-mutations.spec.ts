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
import { AdminPhotosPage } from '../pages/admin-photos-page'
import { AdminSummaryPage } from '../pages/admin-summary-page'

/**
 * Аватар пользователя (#85, #89): загрузка, премодерация, админская карточка
 * и удаление.
 *
 * Сценарий идёт по мутирующему клиенту `mutations`: имя, отзывы и брони других
 * фикстур он не делит, а `seed.sql` сбрасывает его `avatar_url` и статус
 * премодерации. Витринного барбера (`…0001`) не трогаем — на нём держатся поиск,
 * карточка и брони.
 *
 * Файл `e2e/fixtures/portfolio-photo.png` — настоящий PNG 800×600: только так
 * `naturalWidth > 0` доказывает, что реальный файл прошёл клиентскую подготовку
 * (квадрат 512×512), multipart-загрузку и раздачу `/api/media/avatars/...`.
 *
 * Премодерация (#89): после загрузки кадр ждёт проверки — владелец видит превью и
 * бейдж «На проверке», а `GET /api/users/me` отдаёт `Pending`. Публичность
 * возвращает только решение администратора в `/admin/photos?tab=avatars`:
 * «Одобрить» делает кадр публичным, «Отклонить» оставляет его непубличным и
 * показывает владельцу причину. Прежние сцены hide/unhide работают уже с
 * `Approved`, поэтому одобрение идёт до них.
 *
 * Если сцена упала, `afterEach` удаляет аватар через API и снимает флаг
 * блокировки админским контекстом: у владельца нет прав на этот эндпоинт.
 */
const AVATAR_PATH = fileURLToPath(
  new URL('../fixtures/portfolio-photo.png', import.meta.url)
)
const INITIALS = 'МП'
const MUTATIONS_USER_ID = fixtures.mutationsUserId
// Email клиента `mutations` из сида: по нему очередь ищет свою строку.
const MUTATIONS_EMAIL = 'e2e-mutations@barbersearch.test'
// Бейдж/кнопки аватара в админской карточке: заголовок диалога и его кнопка
// совпадают по видимому тексту, поэтому подтверждение ищется внутри `alertdialog`.
const HIDE_PHOTO = 'Скрыть фото'
const SHOW_PHOTO = 'Вернуть фото'
// Причина отказа: ровно она должна появиться у владельца и в `users/me`.
const REJECTION_REASON = 'E2E: кадр размыт, загрузите резче'

test.use({ storageState: storageStatePath('mutations') })

test.describe.configure({ mode: 'serial' })

test.describe('аватар кабинета: мутации', () => {
  // `src` передаётся из сцены загрузки в админскую, 320-ю и сцену удаления.
  let avatarSrc: string | null = null
  // URL варианта `sm` с `srcset` шапки: после замены, скрытия и удаления он
  // обязан отвечать `404`, иначе браузер покажет старый кадр из кэша.
  let avatarVariantUrl: string | null = null

  /** Первый URL из `srcset` аватара (`?size=sm 1x, …`). */
  function firstAvatarVariant(srcSet: string | null): string {
    expect(srcSet).not.toBeNull()
    expect(srcSet).toContain('?size=sm 1x')
    expect(srcSet).toContain('?size=md 2x')
    expect(srcSet).toContain('?size=lg 3x')

    const url = srcSet?.split(',')[0]?.trim().split(' ')[0]

    expect(url).toMatch(
      /^\/api\/media\/avatars\/[0-9a-f-]+\/[0-9a-f]{32}\.jpg\?size=sm$/
    )

    return url ?? ''
  }

  test.afterEach(async ({ browser, request }, testInfo) => {
    // Упавшая сцена могла оставить и аватар, и флаг блокировки: снимаем флаг
    // админским контекстом, затем убираем файл API-вызовом клиента.
    if (testInfo.status !== testInfo.expectedStatus) {
      await unblockAvatarAsAdmin(browser, MUTATIONS_USER_ID)
      await deleteMyAvatar(request)
    }
  })

  test('загрузка: превью «На проверке», объявление, каркас и reload', async ({
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

    // Премодерация: успех — это «ждёт проверки», а не «обновлён».
    await expect(
      profile.avatarStatus.filter({
        hasText: 'Аватар загружен и ждёт проверки.',
      })
    ).toBeVisible()
    await expect(profile.avatarImage).toBeVisible()
    await expect(
      profile.avatarSection.getByText('На проверке', { exact: true })
    ).toBeVisible()
    await expect(
      profile.avatarSection.getByText(
        'Кадр виден только вам, пока администратор его не одобрит.'
      )
    ).toBeVisible()

    const src = await profile.avatarImage.getAttribute('src')

    expect(src).toMatch(/^\/api\/media\/avatars\//)
    avatarSrc = src

    // Файл реально отдан API, а не подставлен строкой в разметку. Владелец
    // получает превью и до одобрения — это его собственный кадр.
    await expect
      .poll(
        () =>
          profile.avatarImage.evaluate(
            (node) => (node as HTMLImageElement).naturalWidth
          ),
        { message: 'аватар отдан API, а не только строка в ответе' }
      )
      .toBeGreaterThan(0)

    // Каркас кабинета и шапка показывают превью без перезагрузки: общий
    // `ProfileProvider` обновил всех подписчиков через `setAvatar(url, status)`.
    await expect(profile.identityAvatarImage).toHaveAttribute('src', src ?? '')
    await expect(headerImage).toHaveAttribute('src', src ?? '')
    await expect(headerImage).toHaveAttribute('alt', '')

    // Шапка 32 px просит плотности 1x/2x/3x, а не 512×512 исходник.
    avatarVariantUrl = firstAvatarVariant(
      await headerImage.getAttribute('srcset')
    )

    const variant = await page.request.get(avatarVariantUrl)

    expect(variant.status(), 'GET аватара ?size=sm').toBe(200)
    expect(variant.headers()['content-type']).toBe('image/webp')

    // Контракт: `users/me` — единственное чтение с сырым URL, и он же отдаёт
    // статус проверки. `Pending` здесь обязателен: кадр ещё не публичен.
    const me = await page.request.get('/api/users/me')

    expect(me.status(), 'GET /api/users/me').toBe(200)

    const meBody = (await me.json()) as {
      avatarUrl: string | null
      avatarModerationStatus: string
      avatarRejectionReason: string | null
    }

    expect(meBody.avatarUrl).toBe(src)
    expect(meBody.avatarModerationStatus, 'статус сразу после загрузки').toBe(
      'Pending'
    )
    expect(meBody.avatarRejectionReason).toBeNull()

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
    await expect(
      profile.avatarSection.getByText('На проверке', { exact: true })
    ).toBeVisible()
  })

  test('админская карточка пользователя видит тот же src и «На проверке»', async ({
    browser,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const adminContext = await openAdminContext(browser)

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

      // Карточка показывает статус премодерации: решение принимается и здесь,
      // но очередь остаётся основным путём (следующая сцена).
      await expect(
        detail.getByText('На проверке', { exact: true })
      ).toBeVisible()
      await expect(
        detail.getByRole('button', { name: 'Одобрить', exact: true })
      ).toBeVisible()
    } finally {
      await adminContext.close()
    }
  })

  test('очередь аватаров: поиск по email, превью крупно и «Одобрить»', async ({
    browser,
    page,
    playwright,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const adminContext = await openAdminContext(browser)

    try {
      const adminPage = await adminContext.newPage()
      const photos = new AdminPhotosPage(adminPage)

      // Сводка с реально непустой очередью: блок показывает ссылки в обе вкладки,
      // а не честный ноль. Числа не проверяем — соседний спек мог добавить свой
      // кадр; важен сам контракт адресов (`/admin/photos` и `?tab=portfolio`).
      const summary = new AdminSummaryPage(adminPage)

      await summary.goto()
      await expect(summary.photosBlock).toBeVisible()
      await expect(summary.emptyPhotos).toHaveCount(0)
      await expect(summary.photosAvatarsLink).toHaveAttribute(
        'href',
        '/admin/photos'
      )
      await expect(summary.photosPortfolioLink).toHaveAttribute(
        'href',
        '/admin/photos?tab=portfolio'
      )

      await photos.goto('avatars')
      await expect(photos.heading).toBeVisible()
      await expect(photos.tab('Аватары')).toHaveAttribute(
        'aria-selected',
        'true'
      )

      // Числа очереди не проверяем: соседний мутирующий спек может успеть
      // добавить строку. Ищем свою по email владельца.
      await photos.searchFor(MUTATIONS_EMAIL)

      const row = photos.row(MUTATIONS_EMAIL)

      await expect(row).toHaveCount(1)
      // Фамилия стабильна: параллельный `account-profile-mutations` правит
      // только имя клиента, поэтому полное «Марк Правкин» здесь ненадёжно.
      await expect(row).toContainText('Правкин')
      await expect(row.getByText('ждёт с', { exact: false })).toBeVisible()

      // Превью открывается крупно и закрывается Escape с возвратом фокуса.
      await photos.previewButton(row).click()

      const preview = photos.previewDialog

      await expect(preview).toBeVisible()
      await expect(preview.getByText('Фото на проверке')).toBeVisible()
      await expect(preview.getByRole('img', { name: /^Фото: / })).toBeVisible()

      // Очередь с реальной строкой — состояние, которого нет на пересеянной
      // базе: axe проверяет её именно здесь, без allow-list (§11).
      await expectNoBlockingViolations(
        adminPage,
        '/admin/photos?tab=avatars + строка и превью @1280',
        test.info()
      )

      await adminPage.keyboard.press('Escape')

      await expect(preview).toBeHidden()
      await expect(photos.previewButton(row)).toBeFocused()

      await photos.approve(row)

      await expect(photos.announcement('Фото одобрено.')).toBeVisible()
      await expect(row).toHaveCount(0)

      // Строка исчезла не только локально: очередь перечитана с поиском, и сервер
      // больше не отдаёт одобренный кадр.
      await expect(photos.emptySearch).toBeVisible()

      // Публичность вернулась: статус `Approved`, тот же URL.
      const me = await page.request.get('/api/users/me')

      expect(me.status(), 'GET /api/users/me после одобрения').toBe(200)

      const meBody = (await me.json()) as {
        avatarUrl: string | null
        avatarModerationStatus: string
        avatarRejectionReason: string | null
      }

      expect(meBody.avatarModerationStatus).toBe('Approved')
      expect(meBody.avatarUrl).toBe(avatarSrc)
      expect(meBody.avatarRejectionReason).toBeNull()

      // Файл стал публичным только после решения: аноним получает `200`.
      // Пустой `storageState` обязателен: `playwright.request.newContext`
      // наследует `storageState` сценария, и «аноним» иначе был бы владельцем.
      const anonymous = await playwright.request.newContext({
        baseURL,
        storageState: { cookies: [], origins: [] },
      })

      try {
        const media = await anonymous.get(avatarSrc ?? '')

        expect(media.status(), 'GET media анонимом после одобрения').toBe(200)
      } finally {
        await anonymous.dispose()
      }
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

      // Блокировать можно только одобренный кадр: после сцены одобрения кнопка
      // «Скрыть фото» на месте, статуса «На проверке» уже нет.
      await expect(
        detail.getByText('На проверке', { exact: true })
      ).toHaveCount(0)

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
          'Его не видно публично. Новое фото появится публично после проверки, а вернуть старое может только администратор.'
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
        avatarModerationStatus: string
      }

      expect(meBody.avatarUrl).toBe(avatarSrc)
      expect(meBody.isAvatarBlocked, 'users/me отдаёт флаг блокировки').toBe(
        true
      )
      // Блокировка — отдельный флаг, а не второе решение о кадре.
      expect(meBody.avatarModerationStatus).toBe('Approved')

      // Скрытый файл: анониму и владельцу — тот же 404, админу — 200 и private.
      const anonymous = await playwright.request.newContext({
        baseURL,
        storageState: { cookies: [], origins: [] },
      })

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

      // Вариант скрытого кадра подчиняется тому же правилу: владелец — `404`.
      expect(avatarVariantUrl).not.toBeNull()
      expect(
        (await page.request.get(avatarVariantUrl ?? '')).status(),
        'GET ?size=-варианта скрытого аватара'
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

      // Публичный кадр проверяется настоящим гостем: `playwright.request.newContext`
      // наследует `storageState` сценария, а запрос владельца обновляет auth-cookie,
      // и ASP.NET добавляет к ответу `no-cache,no-store` поверх `Cache-Control`.
      // Именно эта подмена и была причиной известного «флейка» строки с кэшем.
      const anonymous = await playwright.request.newContext({
        baseURL,
        storageState: { cookies: [], origins: [] },
      })

      try {
        const media = await anonymous.get(avatarSrc ?? '')

        expect(media.status(), 'GET media после возврата').toBe(200)
        expect(
          media.headers()['cache-control'],
          'Cache-Control видимого файла'
        ).toBe('public, max-age=0, must-revalidate')

        // Видимый вариант перепроверяется тем же правилом, а не живёт сутки.
        const variant = await anonymous.get(avatarVariantUrl ?? '')

        expect(variant.status(), 'GET ?size=-варианта после возврата').toBe(200)
        expect(variant.headers()['content-type']).toBe('image/webp')
        expect(variant.headers()['cache-control']).toBe(
          'public, max-age=0, must-revalidate'
        )
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

  test('очередь отклоняет новый кадр: причина у владельца и `Rejected` в users/me', async ({
    browser,
    page,
    playwright,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const profile = new AccountProfilePage(page)

    // Владелец загружает второй кадр: он заменяет одобренный URL и снова ждёт
    // проверки, поэтому очередь снова непустая — на ней и проверяется отказ.
    await profile.goto()
    await profile.waitForReady()
    await profile.uploadAvatar(AVATAR_PATH)

    await expect(
      profile.avatarStatus.filter({
        hasText: 'Аватар загружен и ждёт проверки.',
      })
    ).toBeVisible()
    await expect(profile.avatarImage).toBeVisible()

    const pendingSrc = await profile.avatarImage.getAttribute('src')

    expect(pendingSrc).toMatch(/^\/api\/media\/avatars\//)

    // Замена удаляет старый файл: его `?size=`-URL больше не отдаётся.
    expect(avatarVariantUrl).not.toBeNull()
    expect(
      (await page.request.get(avatarVariantUrl ?? '')).status(),
      'GET старого ?size=-варианта после замены'
    ).toBe(404)

    avatarVariantUrl = firstAvatarVariant(
      await profile.avatarImage.getAttribute('srcset')
    )

    const adminContext = await openAdminContext(browser)

    try {
      const adminPage = await adminContext.newPage()
      const photos = new AdminPhotosPage(adminPage)

      // 320 px: строка очереди — карточка, кнопки 44 px, без горизонтального
      // скролла. Состояние с реальной строкой проверяется здесь же.
      await adminPage.setViewportSize({ width: 320, height: 800 })
      await photos.goto('avatars')
      await photos.searchFor(MUTATIONS_EMAIL)

      const row = photos.row(MUTATIONS_EMAIL)

      await expect(row).toHaveCount(1)

      for (const button of [
        photos.approveButton(row),
        photos.rejectButton(row),
      ]) {
        const box = await button.boundingBox()

        expect(box?.height ?? 0).toBeGreaterThanOrEqual(44)
      }

      const overflow = await adminPage.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth
      )

      expect(overflow, '/admin/photos?tab=avatars @320').toBeLessThanOrEqual(0)

      await expectNoBlockingViolations(
        adminPage,
        '/admin/photos?tab=avatars + строка @320',
        test.info()
      )

      // Отказ требует причину: пустую строку клиент не отправляет.
      const dialog = await photos.openReject(row)

      await expect(dialog).toContainText(
        'Фото останется непубличным. Владелец увидит причину и сможет загрузить другое.'
      )
      await expect(photos.rejectionReason).toBeVisible()

      await expectNoBlockingViolations(
        adminPage,
        '/admin/photos + диалог «Отклонить фото?» @320',
        test.info()
      )

      await photos.rejectionReason.fill(REJECTION_REASON)
      await photos.rejectConfirm.click()

      await expect(photos.announcement('Фото отклонено.')).toBeVisible()
      await expect(row).toHaveCount(0)

      // Владелец видит бейдж «Отклонено» и причину — и не видит кадр в шапке.
      await profile.goto()
      await profile.waitForReady()

      await expect(
        profile.avatarSection.getByText('Отклонено', { exact: true })
      ).toBeVisible()
      await expect(
        profile.avatarSection.getByText(`Причина: ${REJECTION_REASON}`)
      ).toBeVisible()
      // В карточке превью остаётся: владелец должен видеть, что отклонили.
      await expect(profile.avatarImage).toBeVisible()
      await expect(profile.identityAvatarImage).toHaveCount(0)
      await expect(
        page.locator(
          'button[aria-label="Меню аккаунта"] [data-slot="avatar-image"]'
        )
      ).toHaveCount(0)

      // Контракт: статус и причина — часть `users/me`.
      const me = await page.request.get('/api/users/me')

      expect(me.status(), 'GET /api/users/me после отказа').toBe(200)

      const meBody = (await me.json()) as {
        avatarUrl: string | null
        avatarModerationStatus: string
        avatarRejectionReason: string | null
      }

      expect(meBody.avatarModerationStatus).toBe('Rejected')
      expect(meBody.avatarRejectionReason).toBe(REJECTION_REASON)
      expect(meBody.avatarUrl).toBe(pendingSrc)

      // Отклонённый кадр не публичен: аноним получает `404`, владелец — свой файл.
      // Без пустого `storageState` «аноним» унаследовал бы cookie владельца и получил
      // `200` как превью своего же кадра.
      const anonymous = await playwright.request.newContext({
        baseURL,
        storageState: { cookies: [], origins: [] },
      })

      try {
        const anonymousMedia = await anonymous.get(pendingSrc ?? '')

        expect(
          anonymousMedia.status(),
          'GET media анонимом после отказа'
        ).toBe(404)
      } finally {
        await anonymous.dispose()
      }

      expect(
        (await page.request.get(pendingSrc ?? '')).status(),
        'GET media владельцем после отказа'
      ).toBe(200)

      avatarSrc = pendingSrc
    } finally {
      await adminContext.close()
    }
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
    // Отклонённый кадр тоже можно удалить — кнопка есть на любом статусе.
    await expect(profile.avatarImage).toBeVisible()

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

    // Удаление уносит и варианты: старый `?size=` больше не отвечает.
    expect(
      (await page.request.get(avatarVariantUrl ?? '')).status(),
      'GET старый /api/media/avatars/...?size=sm'
    ).toBe(404)
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
