import type { APIRequestContext, Locator, Page } from '@playwright/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import {
  cancelGuestBookings,
  createGuestBookings,
  type GuestBooking,
} from '../fixtures/api'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { expect, test } from '../fixtures/test'
import { AccountBookingsPage } from '../pages/account-bookings-page'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * Перенос визита: клиент, барбер, конфликт времени и клавиатура/axe.
 *
 * Мутационный проект идёт последним, но файлы внутри него параллельны, поэтому
 * сценарии работают с аккаунтом `mutations` и витринным барбером B1: их неделя
 * задана в сиде, а клиентский аккаунт `customer` занят `account-bookings-mutations`.
 * Лишние брони не создаются — 409 занимает слот переносом уже существующей строки,
 * поэтому `barber-cabinet-mutations` продолжает видеть те же 21 «Ближайших».
 * Внутри файла тесты `serial` — они делят один список «Предстоящих».
 *
 * Удаления брони в API нет: клиентский сценарий возвращает исходное время тем же
 * `POST .../reschedule`, конфликтный — возвращает занятую строку, барберский —
 * отменяет созданную гостевую запись.
 */
test.describe.configure({ mode: 'serial' })

type BookingDto = {
  id: string
  barberProfileId: string
  barberServiceId: string
  salonId: string
  startAt: string
  endAt: string
  price: number
  status: string
}

type BookingPageDto = {
  items: BookingDto[]
  nextCursor: string | null
}

type PublicSalonDto = {
  salonId: string
  timeZoneId: string
}

type PublicProfileDto = {
  salons: PublicSalonDto[]
}

const DIALOG_NAME = 'Перенести запись'
const GUEST_LAST_NAME = `Перенос ${String(Date.now() % 10_000)}`

/** Радиокнопка слота — единственное место, где тест берёт точный `startAt`. */
function dialogOf(page: Page) {
  return page.getByRole('dialog', { name: DIALOG_NAME })
}

/** Свои брони клиента: без `userId` администратор увидел бы весь список. */
async function fetchUpcoming(request: APIRequestContext): Promise<BookingDto[]> {
  const me = await request.get('/api/auth/me')
  expect(me.status(), 'GET /api/auth/me').toBe(200)

  const user = (await me.json()) as { id: string }
  const response = await request.get(
    `/api/bookings?scope=upcoming&pageSize=20&userId=${user.id}`
  )
  expect(response.status(), 'GET /api/bookings').toBe(200)

  const body = (await response.json()) as BookingPageDto

  return body.items
}

/** Пояс салона брони: в нём диалог считает дату, а строка показывает время. */
async function salonTimeZone(
  request: APIRequestContext,
  booking: BookingDto
): Promise<string> {
  const response = await request.get(
    `/api/barbers/${booking.barberProfileId}`
  )
  expect(response.status(), 'GET /api/barbers/{id}').toBe(200)

  const profile = (await response.json()) as PublicProfileDto
  const salon =
    profile.salons.find((item) => item.salonId === booking.salonId) ??
    profile.salons[0]

  expect(salon, 'у барбера есть салон').toBeTruthy()

  return salon.timeZoneId
}

/** `YYYY-MM-DD` в поясе браузера — так же считает «сегодня» лента дня. */
function browserDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')

  return `${date.getFullYear()}-${month}-${day}`
}

function salonDate(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    timeZone,
  }).format(new Date(iso))
}

function timeFormatter(timeZone: string): Intl.DateTimeFormat {
  return new Intl.DateTimeFormat('ru-RU', {
    hour: '2-digit',
    minute: '2-digit',
    timeZone,
  })
}

/** `2026-11-30` + 1 → `2026-12-01`; поле даты понимает только такую строку. */
function addDays(date: string, days: number): string {
  const start = Date.parse(`${date}T00:00:00Z`)

  return new Date(start + days * 24 * 60 * 60 * 1000)
    .toISOString()
    .slice(0, 10)
}

async function csrfToken(request: APIRequestContext): Promise<string> {
  const response = await request.get('/api/auth/csrf')
  expect(response.status(), 'GET /api/auth/csrf').toBe(200)

  return ((await response.json()) as { token: string }).token
}

/** Обратный/подготовительный перенос через API; возвращает фактический статус. */
async function rescheduleViaApi(
  request: APIRequestContext,
  bookingId: string,
  startAt: string
): Promise<number> {
  const token = await csrfToken(request)

  const response = await request.post(
    `/api/bookings/${bookingId}/reschedule`,
    {
      headers: { 'X-CSRF-TOKEN': token },
      data: { startAt },
    }
  )

  return response.status()
}

/** Открыть диалог строки и дождаться, пока сетка перестанет грузиться. */
async function openRescheduleDialog(row: Locator, page: Page) {
  await row.getByRole('button', { name: /^Перенести запись/ }).click()

  const dialog = dialogOf(page)

  await expect(dialog).toBeVisible()
  await expect(dialog.getByRole('status')).toHaveText(
    /^(Свободно|В этот день|Свободного времени)/
  )

  return dialog
}

/**
 * Дождаться сетки именно выбранной даты (или честного «слотов нет»).
 *
 * После `fill` старые радиокнопки ещё мгновение живут в DOM: без этой проверки
 * тест прочитал бы `startAt` прошлой даты и отправил перенос «на то же время».
 */
async function waitForDateSlots(dialog: Locator, date: string): Promise<number> {
  await expect
    .poll(
      async () => {
        const values = await dialog
          .getByRole('radio')
          .evaluateAll((nodes) =>
            nodes.map((node) => (node as HTMLInputElement).value)
          )

        if (values.length > 0) {
          return values.every((value) => value.startsWith(date))
        }

        return dialog
          .getByText(
            /В этот день барбер не работает\.|Свободного времени на этот день нет\./
          )
          .first()
          .isVisible()
          .catch(() => false)
      },
      { message: `сетка на ${date}` }
    )
    .toBe(true)

  return dialog.getByRole('radio').count()
}

/**
 * Первая страница «Предстоящих» пришла с сервера.
 *
 * Точное число строк не проверяем: соседний мутационный спек может в этот момент
 * отменять и восстанавливать первую запись, и счётчик на секунду станет 19.
 */
async function waitForRows(
  account: AccountBookingsPage,
  atLeast: number
): Promise<void> {
  await expect
    .poll(() => account.rows.count(), {
      message: `«Предстоящие»: минимум ${atLeast} строк`,
    })
    .toBeGreaterThanOrEqual(atLeast)
}

test.describe('клиент переносит визит', () => {
  test.use({ storageState: storageStatePath('mutations') })

  test('вторая строка: диалог, новый слот, баннер и новые startAt/endAt', async ({
    page,
  }) => {
    test.slow()

    const account = new AccountBookingsPage(page)
    const bookings = await fetchUpcoming(page.request)
    // Витринный барбер B1 (его неделя 09:00–18:00 задана в сиде) и вторая строка:
    // первая остаётся нетронутой для соседних сценариев, а клиентский аккаунт
    // `mutations` не делят `account-bookings-mutations` (тот работает с `customer`).
    const target = bookings[1]

    expect(target, 'в «Предстоящих» есть вторая строка').toBeTruthy()

    const timeZone = await salonTimeZone(page.request, target)
    const originalStartAt = target.startAt
    const originalEndAt = target.endAt
    const oldTime = timeFormatter(timeZone).format(new Date(originalStartAt))

    await account.goto()
    await waitForRows(account, 4)

    try {
      const row = account.rows.nth(1)
      const dialog = await openRescheduleDialog(row, page)
      const dateInput = dialog.getByLabel('Новая дата')
      const currentDate = await dateInput.inputValue()
      const newDate = addDays(currentDate, 2)

      await dateInput.fill(newDate)
      await waitForDateSlots(dialog, newDate)

      const radio = dialog.getByRole('radio').first()

      await expect(radio).toBeVisible()

      const newStartAt = await radio.getAttribute('value')

      expect(newStartAt, 'у слота есть startAt').toBeTruthy()

      // Клик по подписи чипа: сам `input` — `sr-only` 1×1, и принудительный клик
      // по нему не переключает состояние. `datetime` — ровно `startAt` сервера.
      await dialog.locator(`time[datetime="${newStartAt}"]`).click()
      await expect(
        dialog.getByRole('button', { name: 'Перенести визит' })
      ).toBeEnabled()
      await dialog.getByRole('button', { name: 'Перенести визит' }).click()

      await expect(dialog).toBeHidden()
      await expect(
        page.getByRole('status').filter({ hasText: 'Визит перенесён на' })
      ).toBeVisible()

      const newTime = timeFormatter(timeZone).format(
        new Date(newStartAt as string)
      )

      await expect(row).toContainText(newTime)
      if (newTime !== oldTime) {
        await expect(row).not.toContainText(oldTime)
      }

      const updated = (await fetchUpcoming(page.request)).find(
        (booking) => booking.id === target.id
      )

      expect(updated, 'бронь осталась в «Предстоящих»').toBeTruthy()
      expect(Date.parse(updated!.startAt)).toBe(Date.parse(newStartAt as string))
      expect(Date.parse(updated!.endAt)).toBeGreaterThan(
        Date.parse(updated!.startAt)
      )
      expect(updated!.price).toBe(target.price)
      expect(updated!.status).toBe(target.status)
      // Длительность сохранена: конец слота, а не пересчёт из нового прайса.
      expect(Date.parse(updated!.endAt) - Date.parse(updated!.startAt)).toBe(
        Date.parse(originalEndAt) - Date.parse(originalStartAt)
      )
    } finally {
      // Уборка — обратный перенос: исходный слот после переноса свободен.
      await rescheduleViaApi(page.request, target.id, originalStartAt)
    }
  })
})

test.describe('барбер переносит визит', () => {
  test.use({ storageState: storageStatePath('scheduleBarber') })

  test('гость переезжает на сегодня и появляется в ленте дня', async ({
    page,
    playwright,
  }) => {
    test.slow()

    const cabinet = new CabinetPage(page)
    const timeZone = fixtures.scheduleBarber.timeZoneId
    const now = new Date()
    const todayMoscow = salonDate(now.toISOString(), timeZone)
    const browserToday = browserDate(now)

    // Лента дня строится по календарю браузера; на стыке суток с Москвой
    // сценарий честно пропускается, а не падает на чужом поясе.
    test.skip(
      todayMoscow !== browserToday,
      'календарь браузера и салона разошлись: лента дня покажет другой день'
    )

    const tomorrow = new Date(`${addDays(todayMoscow, 1)}T10:00:00+03:00`)
    let created: GuestBooking[] = []

    try {
      created = await createGuestBookings(
        playwright,
        [{ startAt: tomorrow, lastName: GUEST_LAST_NAME }],
        {
          role: 'scheduleBarber',
          barberServiceId: fixtures.scheduleBarber.barberServiceId,
          salonId: fixtures.scheduleBarber.salonId,
        }
      )

      await cabinet.goto()
      await cabinet.openTab('Ближайшие')

      const row = cabinet.rows.filter({ hasText: `E2E ${GUEST_LAST_NAME}` })

      await expect(row).toBeVisible()

      const dialog = await openRescheduleDialog(row, page)
      const dateInput = dialog.getByLabel('Новая дата')

      await dateInput.fill(todayMoscow)
      const radioCount = await waitForDateSlots(dialog, todayMoscow)

      test.skip(radioCount === 0, 'на сегодня у барбера не осталось свободных слотов')

      const radio = dialog.getByRole('radio').first()
      const newStartAt = await radio.getAttribute('value')

      await dialog.locator(`time[datetime="${newStartAt}"]`).click()
      await expect(
        dialog.getByRole('button', { name: 'Перенести визит' })
      ).toBeEnabled()
      await dialog.getByRole('button', { name: 'Перенести визит' }).click()

      await expect(dialog).toBeHidden()
      await expect(
        page.getByRole('status').filter({ hasText: 'Визит перенесён на' })
      ).toBeVisible()

      // Строка «Ближайших» уже показывает новое время.
      const newTime = timeFormatter(timeZone).format(new Date(newStartAt as string))

      await expect(row).toContainText(newTime)

      // Лента дня пересобирается из изменившейся строки и показывает визит сегодня.
      await cabinet.openTab('Сегодня')
      await expect(
        cabinet.panel.getByText(`E2E ${GUEST_LAST_NAME}`)
      ).toBeVisible()
    } finally {
      await cancelGuestBookings(
        playwright,
        created.map((booking) => booking.id),
        'scheduleBarber'
      )
    }
  })
})

test.describe('409: время заняли, пока диалог был открыт', () => {
  test.use({ storageState: storageStatePath('mutations') })

  test('перенос чужой брони в выбранный слот даёт «Это время уже занято»', async ({
    page,
  }) => {
    test.slow()

    const account = new AccountBookingsPage(page)
    const bookings = await fetchUpcoming(page.request)
    const target = bookings[2]
    // Слот занимает не новая бронь, а перенос уже существующей: список остаётся
    // из 21 строки, и соседний `barber-cabinet-mutations` не видит лишнюю строку.
    const source = bookings[0]

    expect(target, 'в «Предстоящих» есть третья строка').toBeTruthy()
    expect(source, 'в «Предстоящих» есть первая строка').toBeTruthy()

    await account.goto()
    await waitForRows(account, 4)

    const row = account.rows.nth(2)
    const dialog = await openRescheduleDialog(row, page)
    const dateInput = dialog.getByLabel('Новая дата')
    const currentDate = await dateInput.inputValue()

    const newDate = addDays(currentDate, 2)

    await dateInput.fill(newDate)
    await waitForDateSlots(dialog, newDate)

    const radio = dialog.getByRole('radio').first()

    await expect(radio).toBeVisible()

    const slotStartAt = await radio.getAttribute('value')

    expect(slotStartAt, 'у слота есть startAt').toBeTruthy()

    const slotRequests: string[] = []

    page.on('request', (request) => {
      if (request.url().includes('/reschedule-slots')) {
        slotRequests.push(request.url())
      }
    })

    try {
      // Занимаем выбранный слот переносом другой брони того же барбера.
      expect(
        await rescheduleViaApi(page.request, source.id, slotStartAt as string),
        'перенос второй брони в выбранный слот'
      ).toBe(204)

      const beforeSubmit = slotRequests.length

      await dialog.locator(`time[datetime="${slotStartAt}"]`).click()
      await expect(
        dialog.getByRole('button', { name: 'Перенести визит' })
      ).toBeEnabled()
      await dialog.getByRole('button', { name: 'Перенести визит' }).click()

      await expect(dialog.getByRole('alert')).toHaveText(
        'Это время уже занято. Выберите другое.'
      )

      // После `reloadSlots` сетка перезапрашивается, а выбор сбрасывается.
      await expect
        .poll(() => slotRequests.length, {
          message: 'слоты перезапрошены после 409',
        })
        .toBeGreaterThan(beforeSubmit)
      await expect(
        dialog.getByRole('button', { name: 'Перенести визит' })
      ).toBeDisabled()

      // Исходная бронь не изменилась.
      const unchanged = (await fetchUpcoming(page.request)).find(
        (booking) => booking.id === target.id
      )

      expect(unchanged?.startAt).toBe(target.startAt)
    } finally {
      // Возвращаем занятую строку на место; удаления брони в API нет.
      await rescheduleViaApi(page.request, source.id, source.startAt)
    }
  })
})

test.describe('клавиатура и axe на открытом диалоге', () => {
  test.use({ storageState: storageStatePath('customer') })

  test('1280 и 320: Escape закрывает, фокус возвращается на кнопку строки', async ({
    page,
  }) => {
    test.slow()

    const account = new AccountBookingsPage(page)

    await account.goto()
    await waitForRows(account, 4)

    const row = account.rows.last()
    const trigger = row.getByRole('button', { name: /^Перенести запись/ })

    await trigger.click()
    await expect(dialogOf(page)).toBeVisible()
    await expect(dialogOf(page).getByRole('status')).toHaveText(
      /^(Свободно|В этот день|Свободного времени)/
    )

    const wideOverflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(wideOverflow, '1280: нет горизонтального скролла').toBeLessThanOrEqual(0)
    await expectNoBlockingViolations(
      page,
      '/account (диалог переноса, 1280)',
      test.info()
    )

    await page.keyboard.press('Escape')
    await expect(dialogOf(page)).toBeHidden()
    await expect(trigger).toBeFocused()

    await page.setViewportSize({ width: 320, height: 800 })

    await trigger.click()
    await expect(dialogOf(page)).toBeVisible()

    const narrowOverflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(narrowOverflow, '320: нет горизонтального скролла').toBeLessThanOrEqual(0)
    await expectNoBlockingViolations(
      page,
      '/account (диалог переноса, 320)',
      test.info()
    )

    await page.keyboard.press('Escape')
    await expect(dialogOf(page)).toBeHidden()
    await expect(trigger).toBeFocused()
  })
})
