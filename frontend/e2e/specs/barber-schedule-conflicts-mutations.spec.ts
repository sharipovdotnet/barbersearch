import { expect, test, type Page } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import {
  cancelGuestBookings,
  createGuestBookings,
  putWorkingSchedules,
  type GuestBooking,
  type WorkingDayPayload,
} from '../fixtures/api'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber/schedule` — конфликт правки расписания с активным визитом (#94).
 *
 * Сцена идёт по отдельному барберу `e2e-schedule-barber`: витринного барбера
 * (`…0001`) трогать нельзя, у него записи в горизонте, и любое сохранение недели
 * под его аккаунтом теперь получает `409`. Профиль сцены выключен в сиде, поэтому
 * не двигает поиск, `usageCount` и сводку админки (`seed.sql`, 4.3).
 *
 * Гостевая запись создаётся через API на завтра, 10:00 в поясе салона: в старые
 * 09:00–18:00 она помещается, а в новые 12:00–18:00 — нет. Поэтому первое
 * сохранение отвечает `409` со списком ровно этого визита, а `force=true`
 * сохраняет часы, не отменяя визит.
 *
 * Сцена живёт в мутационном проекте (`mutations-chromium`) и обязана оканчиваться
 * на `-mutations.spec.ts`: только так она не пересекается с читающими спеками.
 */
test.use({ storageState: storageStatePath('scheduleBarber') })

const SALON_TIME_ZONE = fixtures.scheduleBarber.timeZoneId
/** Москва не переходит на летнее время: смещение постоянно (сид, `Europe/Moscow`). */
const SALON_UTC_OFFSET = '+03:00'

/** Хвост имени гостя: уникален для прогона, чтобы строка не путалась с прошлой. */
const RUN_TOKEN = String(Date.now() % 10_000)
const GUEST_LAST_NAME = `Расписание ${RUN_TOKEN}`
const GUEST_NAME = `E2E ${GUEST_LAST_NAME}`

const DAY_LABELS = [
  'Понедельник',
  'Вторник',
  'Среда',
  'Четверг',
  'Пятница',
  'Суббота',
  'Воскресенье',
] as const

/** Сидовая неделя барбера сцены: ровно то, что должно вернуться после прогона. */
const OLD_WEEK: WorkingDayPayload[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
].map((dayOfWeek) => ({
  dayOfWeek,
  isDayOff: false,
  startTime: '09:00:00',
  endTime: '18:00:00',
}))

/**
 * Завтра, 10:00 в поясе салона — момент визита.
 *
 * `Intl` с `timeZone` даёт календарную дату завтрашнего дня именно в салоне;
 * прибавлять сутки к UTC-моменту нельзя: на стыке суток даты расходятся.
 * В 10:00 визит помещается в сидовые 09:00–18:00 и выходит за новые 12:00–18:00.
 */
function tomorrowAtTenInSalon(): Date {
  const today = new Intl.DateTimeFormat('en-CA', {
    timeZone: SALON_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date())

  const [year, month, day] = today.split('-').map(Number)
  const tomorrow = new Date(Date.UTC(year, month - 1, day + 1))

  return new Date(
    `${tomorrow.toISOString().slice(0, 10)}T10:00:00${SALON_UTC_OFFSET}`
  )
}

/** Ставит начало рабочего дня всем семи строкам: так правка задевает день визита. */
async function setWeekStart(page: Page, value: string): Promise<void> {
  for (const day of DAY_LABELS) {
    await page.getByLabel(`Начало работы, ${day}`).fill(value)
  }
}

test.describe('расписание: конфликт с активным визитом', () => {
  test('409, отмена, повтор с force и живой визит', async ({
    page,
    playwright,
  }) => {
    // API-фикстуры, два сохранения, axe в двух ширинах и перечитывание расписания
    // не укладываются в штатные тридцать секунд.
    test.slow()

    const cabinet = new CabinetPage(page)
    const save = page.getByRole('button', { name: 'Сохранить расписание' })
    const dialog = page.getByRole('alertdialog')
    let created: GuestBooking[] = []

    try {
      created = await createGuestBookings(
        playwright,
        [{ startAt: tomorrowAtTenInSalon(), lastName: GUEST_LAST_NAME }],
        {
          role: 'scheduleBarber',
          barberServiceId: fixtures.scheduleBarber.barberServiceId,
          salonId: fixtures.scheduleBarber.salonId,
        }
      )

      await page.goto('/barber/schedule')

      await expect(
        page.getByRole('heading', { name: 'Расписание', level: 1 })
      ).toBeVisible()
      await expect(
        page.getByLabel('Начало работы, Понедельник')
      ).toHaveValue('09:00')

      // 1. Первое сохранение: сервер не пишет, а объясняет, кого задевает.
      await setWeekStart(page, '12:00')
      await save.click()

      await expect(dialog).toBeVisible()
      await expect(dialog).toHaveAccessibleName('Записи вне новых часов')
      await expect(dialog.getByRole('listitem')).toHaveCount(1)
      await expect(dialog.getByText(GUEST_NAME)).toBeVisible()
      await expect(dialog.getByText('E2E Стрижка')).toBeVisible()
      await expect(dialog.getByText(/^1 запись\./)).toBeVisible()

      // 2. Диалог доступен в обеих ширинах матрицы: список прокручивается внутри
      // `<ul>`, а длинные имя и услуга переносятся, не расширяя страницу.
      await expectNoBlockingViolations(
        page,
        '/barber/schedule, диалог конфликта @1280',
        test.info()
      )

      // Скриншот для отчёта этапа: диалог с реальным визитом на широком экране.
      await page.screenshot({
        path: 'verification/issue-94-dialog-1280.png',
      })

      await page.setViewportSize({ width: 320, height: 800 })

      const overflow = await page.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth
      )

      expect(overflow, 'на 320 px нет горизонтального скролла').toBeLessThanOrEqual(
        0
      )
      await expectNoBlockingViolations(
        page,
        '/barber/schedule, диалог конфликта @320',
        test.info()
      )

      // Второй кадр — та же выборка на 320: горизонтального скролла нет.
      await page.screenshot({
        path: 'verification/issue-94-dialog-320.png',
      })

      await page.setViewportSize({ width: 1280, height: 900 })

      // 3. «Отмена» ничего не сохраняет и не теряет введённое.
      await dialog.getByRole('button', { name: 'Отмена' }).click()
      await expect(dialog).toBeHidden()
      await expect(
        page.getByLabel('Начало работы, Понедельник')
      ).toHaveValue('12:00')

      // Страница не перечитывает расписание на отмену, поэтому «часы на экране
      // прежние» проверяются перезагрузкой: сервер по-прежнему отдаёт 09:00.
      await page.reload()
      await expect(
        page.getByLabel('Начало работы, Понедельник')
      ).toHaveValue('09:00')

      // 4. Второе сохранение: Escape закрывает диалог и возвращает фокус на кнопку,
      // которая его открыла, — запись при этом не форсируется.
      await setWeekStart(page, '12:00')
      await save.click()
      await expect(dialog).toBeVisible()
      await page.keyboard.press('Escape')
      await expect(dialog).toBeHidden()
      await expect(save, 'фокус вернулся на «Сохранить расписание»').toBeFocused()

      // 5. Третье сохранение и осознанное «Всё равно сохранить»: `force=true`.
      await save.click()
      await expect(dialog).toBeVisible()
      await dialog
        .getByRole('button', { name: 'Всё равно сохранить' })
        .click()

      await expect(
        page.getByRole('status').filter({ hasText: 'Расписание сохранено.' })
      ).toBeVisible()

      // Карточка перечитала расписание: новые часы пришли с сервера, а не остались
      // локальным состоянием формы.
      for (const day of DAY_LABELS) {
        await expect(page.getByLabel(`Начало работы, ${day}`)).toHaveValue(
          '12:00'
        )
        await expect(page.getByLabel(`Окончание работы, ${day}`)).toHaveValue(
          '18:00'
        )
      }

      // 6. Визит остался живым: кабинет показывает его в «Ближайших» с прежним
      // статусом. Диалог ничего не отменяет и не переносит.
      await cabinet.goto()
      await cabinet.openTab('Ближайшие')

      const row = cabinet.panel.locator('li').filter({ hasText: GUEST_NAME })

      await expect(row).toBeVisible()
      await expect(row.getByText('Подтверждена')).toBeVisible()
    } finally {
      // Сначала освобождаем время визита, потом возвращаем неделю: даже если
      // отмена не удалась, `?force=true` в уборке не оставит фикстуру с 12:00.
      await cancelGuestBookings(
        playwright,
        created.map((booking) => booking.id),
        'scheduleBarber'
      )
      await putWorkingSchedules(
        playwright,
        {
          role: 'scheduleBarber',
          barberProfileId: fixtures.scheduleBarber.profileId,
          salonId: fixtures.scheduleBarber.salonId,
        },
        OLD_WEEK
      )
    }
  })
})
