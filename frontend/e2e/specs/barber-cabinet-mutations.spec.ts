import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import {
  cancelGuestBookings,
  createGuestBookings,
  createUpcomingBooking,
  type GuestBooking,
} from '../fixtures/api'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber` — мутирующие сцены кабинета (§4.4, §9.5).
 *
 * Два сценария делят одни и те же предстоящие записи витринного барбера: второй
 * (лента дня) создаёт сегодняшние визиты, первый (пустая страница) считает строки
 * «Ближайших». Поэтому тесты идут `serial`: при `fullyParallel` они гонялись бы за
 * один и тот же список.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe.configure({ mode: 'serial' })

test.describe('кабинет барбера: пустая страница после действия', () => {
  /**
   * Ровно одно действие на прогон: сцена опустошает страницу «Ближайших», поэтому
   * в конце запись создаётся заново — следующий прогон видит тот же набор.
   */
  test('отмена последней записи страницы уводит на предыдущую, без пустого состояния', async ({
    page,
    playwright,
  }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await cabinet.waitForUpcoming()

    try {
      // Самовосстановление: если прошлый прогон не успел вернуть запись,
      // возвращаем форму «20 + одна» перед сценарием.
      if ((await cabinet.pager.root.count()) === 0) {
        await createUpcomingBooking(playwright, {
          barberProfileId: fixtures.barberProfileId,
          barberServiceId: fixtures.barberServiceId,
          salonId: fixtures.salonId,
        })
        await page.reload()
        await cabinet.waitForUpcoming()
      }

      await cabinet.pager.clickNext()
      await cabinet.pager.expectRange('21–21')
      await expect(cabinet.rows).toHaveCount(1)

      const row = cabinet.rows.first()
      await row.getByRole('button', { name: 'Отменить' }).click()

      await expect(cabinet.actionDialog).toBeVisible()
      await cabinet.actionDialog
        .getByRole('button', { name: 'Отменить запись' })
        .click()
      await expect(
        cabinet.actionDialog.getByText('Укажите причину отмены.')
      ).toBeVisible()

      await cabinet.actionDialog
        .getByLabel('Причина отмены')
        .fill('Барбер заболел')
      await cabinet.actionDialog
        .getByRole('button', { name: 'Отменить запись' })
        .click()
      await expect(cabinet.actionDialog).toBeHidden()

      // Страница опустела: показываем предыдущую, а не «Ближайших записей нет.».
      await expect(cabinet.rows).toHaveCount(20)
      // Курсоры сшиваются: Ближайшие стали одностраничными, пагинатор исчез.
      await expect(cabinet.pager.root).toHaveCount(0)
      await expect(cabinet.listHeading).toHaveText('Ближайшие записи')
      await expect(page.getByText('Ближайших записей нет.')).toHaveCount(0)
    } finally {
      await createUpcomingBooking(playwright, {
        barberProfileId: fixtures.barberProfileId,
        barberServiceId: fixtures.barberServiceId,
        salonId: fixtures.salonId,
      })
    }
  })
})

/**
 * Лента дня: сегодняшние записи, окно, завершение визита и запись в окно.
 *
 * Сегодняшних записей в `seed.sql` нет, поэтому фикстура создаётся на текущий
 * момент. Записи гостевые: они принадлежат витринному барберу и не связаны с
 * аккаунтами, поэтому кабинеты клиентов и админский поиск по email их не видят.
 * Удаления брони в API нет — `finally` отменяет созданное; отменённая строка
 * остаётся в истории барбера, поэтому сценарий не требует ровно двух строк дня,
 * а ищет свои.
 *
 * Сценарий зависит от календаря: начавшийся визит опирается на пятиминутный
 * допуск создания, а второй визит (через час) должен уместиться в те же сутки.
 * Последний час перед полуночью тест честно пропускается, а не падает на чужом
 * часовом поясе.
 */
/**
 * Хвост гостевых имён: уникален для прогона.
 *
 * Отменённая запись остаётся в истории барбера (удаления брони в API нет), поэтому
 * повторный прогон в тот же день нашёл бы две строки с одним именем. Свободный
 * интервал отмена освобождает, так что время записи менять не нужно.
 */
const RUN_TOKEN = String(Date.now() % 10_000)

/** Гость, чей визит уже начался: на нём проверяется «Завершить». */
const STARTED_LAST_NAME = `Начавшийся ${RUN_TOKEN}`
/** Гость через час: между визитами остаётся окно больше двадцати минут. */
const LATER_LAST_NAME = `Позже ${RUN_TOKEN}`
/** Гость на завтра: его нет в ленте дня, но он есть в «Ближайших». */
const TOMORROW_LAST_NAME = `Завтра ${RUN_TOKEN}`

/** `YYYY-MM-DD` в поясе браузера — так же считает «сегодня» лента дня (D8). */
function localDateKey(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')

  return `${date.getFullYear()}-${month}-${day}`
}

function nextLocalDayStart(date: Date): Date {
  const next = new Date(date)
  next.setHours(24, 0, 0, 0)

  return next
}

const now = new Date()
// Допуск создания записи — пять минут: визит уже идёт, но `end_at` ещё впереди.
// Второй визит — через час: окно (26 минут) видно, а до полуночи остаётся запас.
const startedAt = new Date(now.getTime() - 4 * 60_000)
const laterAt = new Date(now.getTime() + 60 * 60_000)
const tomorrowAt = new Date(nextLocalDayStart(now).getTime() + 10 * 60 * 60_000)

const fitsToday =
  localDateKey(startedAt) === localDateKey(now) &&
  localDateKey(laterAt) === localDateKey(now)

test.describe('кабинет барбера: лента дня', () => {
  test('две записи сегодня, окно, завершение и запись в окно', async ({
    page,
    playwright,
  }) => {
    test.skip(!fitsToday, 'до полуночи не хватает места на два визита сегодня')

    // Фикстуры через API плюс axe на широкой странице — штатных тридцати секунд мало.
    test.slow()

    const cabinet = new CabinetPage(page)
    let created: GuestBooking[] = []

    try {
      created = await createGuestBookings(playwright, [
        { startAt: startedAt, lastName: STARTED_LAST_NAME },
        { startAt: laterAt, lastName: LATER_LAST_NAME },
        { startAt: tomorrowAt, lastName: TOMORROW_LAST_NAME },
      ])

      const requests: string[] = []
      page.on('request', (request) => {
        if (new URL(request.url()).pathname === '/api/barber/bookings') {
          requests.push(request.url())
        }
      })

      await cabinet.goto()

      // 1. Вид по умолчанию — «Сегодня» (D1).
      await expect(cabinet.tab('Сегодня')).toHaveAttribute(
        'aria-selected',
        'true'
      )
      await expect(cabinet.dayHeading).toBeVisible()

      // 2. Сегодняшние строки в ленте, завтрашней нет, между визитами окно.
      await expect(
        cabinet.dayList.getByText(`E2E ${STARTED_LAST_NAME}`)
      ).toBeVisible()
      await expect(
        cabinet.dayList.getByText(`E2E ${LATER_LAST_NAME}`)
      ).toBeVisible()
      await expect(
        cabinet.dayList.getByText(`E2E ${TOMORROW_LAST_NAME}`),
        'завтрашняя запись в день не попадает'
      ).toHaveCount(0)
      await expect(
        cabinet.dayList.getByText(/свободно \d+ мин/).first()
      ).toBeVisible()
      await expect(cabinet.gapButton().first()).toBeVisible()

      // 3. «Завершить»: строка остаётся в дне, объявление прежнее.
      const startedRow = () =>
        cabinet.dayRows.filter({ hasText: `E2E ${STARTED_LAST_NAME}` })

      await startedRow().getByRole('button', { name: 'Завершить' }).click()

      await expect(
        page.getByRole('status').filter({ hasText: 'Визит завершён.' })
      ).toBeVisible()
      await expect(startedRow().getByText('Завершена')).toBeVisible()

      // 4. Окно открывает тот же диалог с датой дня и предвыбранным слотом.
      await cabinet.gapButton().first().click()

      const dialog = cabinet.bookingDialog

      await expect(dialog).toBeVisible()
      await expect(dialog.locator('#barber-booking-date')).toHaveValue(
        localDateKey(now)
      )

      // Салон и услугу барбер выбирает сам: после выбора появляется сетка, а
      // ближайший к окну слот подсвечен. Дата окна — календарный день браузера
      // (D8), а салон живёт в своём поясе: на стыке суток выбранный день может
      // оказаться вне его окна записи.
      await dialog
        .getByRole('combobox', { name: 'Услуга' })
        .selectOption({ index: 1 })

      const checkedSlot = dialog.locator(
        'input[name="barber-booking-slot"]:checked'
      )
      const noSlots = dialog.getByText(
        /Свободного времени на этот день нет\.|В этот день барбер не работает\.|Выберите дату\./
      )

      await expect
        .poll(async () => (await checkedSlot.count()) + (await noSlots.count()), {
          message: 'слоты загрузились',
        })
        .toBeGreaterThan(0)

      if ((await checkedSlot.count()) > 0) {
        await expect(checkedSlot, 'предвыбран ровно один слот').toHaveCount(1)
      }

      await page.keyboard.press('Escape')
      await expect(dialog).toBeHidden()

      // 5. «Ближайшие» показывает завтра; возврат в «Сегодня» не делает запросов.
      await cabinet.openTab('Ближайшие')
      await expect(cabinet.listHeading).toHaveText('Ближайшие записи')
      await expect(
        cabinet.panel.getByText(`E2E ${TOMORROW_LAST_NAME}`)
      ).toBeVisible()

      const beforeReturn = requests.length

      await cabinet.openTab('Сегодня')
      await expect(cabinet.dayHeading).toBeVisible()
      await expect(
        cabinet.dayList.getByText(`E2E ${LATER_LAST_NAME}`)
      ).toBeVisible()

      expect(
        requests.length,
        'переключение вкладок не перезапрашивает записи'
      ).toBe(beforeReturn)

      // 6. Ширина и axe: панель меняется внутри одного `tabpanel`.
      const overflow = await page.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth
      )

      expect(overflow).toBeLessThanOrEqual(0)
      await expectNoBlockingViolations(
        page,
        '/barber (сегодня, с записями)',
        test.info()
      )
    } finally {
      await cancelGuestBookings(
        playwright,
        created.map((booking) => booking.id)
      )
    }
  })
})
