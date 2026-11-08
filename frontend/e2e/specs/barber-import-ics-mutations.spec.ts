import { expect, test } from '../fixtures/test'
import type { Page, TestInfo } from '@playwright/test'

import { storageStatePath } from '../fixtures/accounts'
import { cancelBookingsByGuestName } from '../fixtures/api'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { BarberImportPage } from '../pages/barber-import-page'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber/import` — перенос из календаря (`.ics`): файл → проверка → записи.
 *
 * Файл собирается в тесте с датой `сегодня + 2 дня` в поясе салона фикстур:
 * фиксированная дата «протухла» бы, как только вышла из окна записи. Один визит
 * попадает в предпросмотр, all-day и прошедшее события — в «Пропущено».
 *
 * Ключ идемпотентности календаря детерминирован по `UID`, поэтому повторная
 * загрузка того же файла не создаёт дубль: сценарий делает это явно и проверяет
 * `toHaveCount(1)`. Созданная запись — гостевая, её id из UI не виден, поэтому
 * `finally` отменяет строки по уникальной метке в фамилии гостя.
 */

/** Услуга фикстур: `hair_services.name` в `seed.sql` (`E2E Стрижка`). */
const SERVICE_NAME = 'E2E Стрижка'

/** Пояс салона фикстур: `salons.time_zone_id` в `seed.sql`. */
const SALON_TIME_ZONE = 'Europe/Moscow'

const GUEST_FIRST_NAME = 'Иван'

/**
 * Метка прогона в фамилии: `cancelBookingsByGuestName` ищет запись по ней, а
 * `toHaveCount(1)` доказывает, что повторный импорт не создал вторую строку.
 * В фамилии, а не в середине SUMMARY: `splitName` оставляет только первое и
 * последнее слово, поэтому метка из середины до кабинета не доехала бы.
 */
const RUN_MARK = String(Date.now())
/**
 * Фамилия не должна содержать метку соседнего текстового импорта (`Импортов`):
 * его `finally` отменяет всё, что находит по этой подстроке, и погасил бы нашу
 * запись между двумя загрузками файла.
 */
const GUEST_LAST_NAME = `Календарев${RUN_MARK}`
const GUEST_FULL_NAME = `${GUEST_FIRST_NAME} ${GUEST_LAST_NAME}`

const PHONE = '+79990001122'
const FILE_NAME = `e2e-import-${RUN_MARK}.ics`

/** `YYYY-MM-DD` сегодняшнего дня в поясе салона. */
const salonDayFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: SALON_TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

function addDays(day: string, days: number): string {
  const [year, month, date] = day.split('-').map(Number)
  const moment = new Date(Date.UTC(year, month - 1, date))

  moment.setUTCDate(moment.getUTCDate() + days)

  return moment.toISOString().slice(0, 10)
}

/** `YYYYMMDD` для `DTSTART`/`DTEND`. */
function compactDay(day: string): string {
  return day.replaceAll('-', '')
}

const todayInSalon = salonDayFormatter.format(new Date())
/** Основной визит — через два дня, в 10:00 салона: дата всегда в окне записи. */
const visitDay = addDays(todayInSalon, 2)
/** All-day событие и прошедший визит — только для «Пропущено». */
const allDayDay = addDays(todayInSalon, 1)
const pastDay = addDays(todayInSalon, -2)

/**
 * Один и тот же файл на оба импорта: `UID` и `RECURRENCE-ID` дают тот же ключ
 * идемпотентности, а `DTSTAMP` не входит в идентичность события.
 */
function buildCalendar(): string {
  const stamp = new Date()
    .toISOString()
    .replace(/[-:]/g, '')
    .replace(/\.\d{3}Z$/, 'Z')

  const lines = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//BarberSearch//E2E ICS import//RU',
    'CALSCALE:GREGORIAN',
    'BEGIN:VEVENT',
    `UID:e2e-import-${RUN_MARK}@barbersearch.test`,
    `DTSTAMP:${stamp}`,
    `DTSTART;TZID=${SALON_TIME_ZONE}:${compactDay(visitDay)}T100000`,
    `DTEND;TZID=${SALON_TIME_ZONE}:${compactDay(visitDay)}T104500`,
    `SUMMARY:${GUEST_FIRST_NAME} ${GUEST_LAST_NAME} ${SERVICE_NAME}`,
    `DESCRIPTION:тел. ${PHONE}`,
    'STATUS:CONFIRMED',
    'END:VEVENT',
    // All-day: визитом не считается, парсер жёстко кладёт его в «Пропущено».
    'BEGIN:VEVENT',
    `UID:e2e-import-${RUN_MARK}-allday@barbersearch.test`,
    `DTSTAMP:${stamp}`,
    `DTSTART;VALUE=DATE:${compactDay(allDayDay)}`,
    `SUMMARY:Отпуск E2E ${RUN_MARK}`,
    'END:VEVENT',
    // Прошедшее событие: сервер запрещает запись в прошлом.
    'BEGIN:VEVENT',
    `UID:e2e-import-${RUN_MARK}-past@barbersearch.test`,
    `DTSTAMP:${stamp}`,
    `DTSTART;TZID=${SALON_TIME_ZONE}:${compactDay(pastDay)}T100000`,
    `DTEND;TZID=${SALON_TIME_ZONE}:${compactDay(pastDay)}T104500`,
    `SUMMARY:Прошедшая встреча E2E ${RUN_MARK}`,
    'END:VEVENT',
    'END:VCALENDAR',
  ]

  return lines.join('\r\n')
}

const CALENDAR = buildCalendar()

/** axe и горизонтальный скролл на 1280 и 320: один и тот же гейт для трёх шагов. */
async function scanAtBothWidths(
  page: Page,
  screen: string,
  testInfo: TestInfo
): Promise<void> {
  await expectNoBlockingViolations(page, screen, testInfo)

  await page.setViewportSize({ width: 320, height: 800 })

  const overflow = await page.evaluate(
    () =>
      document.documentElement.scrollWidth -
      document.documentElement.clientWidth
  )

  expect(overflow).toBeLessThanOrEqual(0)
  await expectNoBlockingViolations(page, `${screen} @320`, testInfo)

  await page.setViewportSize({ width: 1280, height: 900 })
}

test.use({ storageState: storageStatePath('barber') })

test.describe('перенос из календаря', () => {
  test('файл .ics, повторная загрузка не создаёт дубль, axe на 1280 и 320', async ({
    page,
  }, testInfo) => {
    // Два импорта, две записи через API и два axe-скана не укладываются в штатные
    // тридцать секунд на медленной машине.
    test.slow()

    const cabinet = new CabinetPage(page)
    const screen = new BarberImportPage(page)

    try {
      // 1. Вход в перенос из кабинета: CTA больше не говорит «из Google Docs».
      await cabinet.goto()
      await expect(cabinet.heading).toBeVisible()

      await page
        .getByRole('link', { name: 'Перенести записи', exact: true })
        .click()

      await expect(page).toHaveURL(/\/barber\/import$/)
      await expect(screen.heading).toBeVisible()
      await expect(screen.cabinetHeading).toBeVisible()
      await expect(screen.sourceGroup).toBeVisible()

      // 2. Источник по умолчанию — файл календаря; период — 30 дней.
      await expect(screen.calendarSource).toBeChecked()
      await expect(screen.periodSelect).toHaveValue('30')

      // 3. Клавиатура: Tab доводит до скрытого input файла, кольцо фокуса видно
      //    на зоне через `focus-within` (§4.2, §11).
      let reachedFileInput = false

      for (let attempt = 0; attempt < 250; attempt += 1) {
        await page.keyboard.press('Tab')

        const focused = await screen.fileInput.evaluate(
          (element) => element === document.activeElement
        )

        if (focused) {
          reachedFileInput = true
          break
        }
      }

      expect(
        reachedFileInput,
        'скрытый input файла достижим с клавиатуры'
      ).toBe(true)
      await expect(screen.fileInput).toBeFocused()

      const focusVisible = await screen.fileInput.evaluate((element) =>
        element.matches(':focus-visible')
      )

      expect(focusVisible, 'фокус-кольцо видно у поля файла').toBe(true)

      const zoneFocused = await screen.fileInput.evaluate(
        (element) => element.closest('label')?.matches(':focus-within') ?? false
      )

      expect(zoneFocused, 'зона файла подсвечена focus-within').toBe(true)

      // 4. Файл выбран вручную: до «Разобрать файл» форма ничего не отправляет.
      await screen.uploadCalendar(CALENDAR, FILE_NAME)
      await expect(page.getByText(FILE_NAME)).toBeVisible()

      // 5. axe на 1280 и 320: форма с выбранным файлом не расширяет страницу.
      await scanAtBothWidths(page, '/barber/import (файл)', testInfo)

      // 6. Разбор: услуга вырезана из SUMMARY, имя осталось; два события
      //    пропущены с причинами.
      await screen.parseCalendar()

      const previewRow = screen.row(1)

      await expect(screen.icsSummary).toHaveText(/^Выбрано 1 из 1/)
      await expect(screen.table).toBeVisible()
      await expect(previewRow).toContainText(GUEST_FIRST_NAME)
      await expect(previewRow).toContainText(SERVICE_NAME)
      await expect(previewRow).toContainText(PHONE)
      await expect(previewRow).toContainText('10:00–10:45')
      await expect(previewRow).toContainText('Готово')

      await expect(screen.skipped).toHaveText('Пропущено 2 события')

      await screen.skipped.click()
      await expect(page.getByText('1 прошедшее', { exact: true })).toBeVisible()
      await expect(page.getByText('1 на весь день', { exact: true })).toBeVisible()

      // 7. Предпросмотр не должен ломать axe и ширину ни на одной из двух
      //    проверяемых ширин.
      await scanAtBothWidths(page, '/barber/import (предпросмотр)', testInfo)

      // 8. Создание: точная подпись кнопки считается `formatBookingsCount`.
      await expect(screen.createButton).toHaveText('Создать 1 запись')
      await screen.createButton.click()

      await expect(screen.report).toHaveText(
        'Перенесено 1 из 1 выбранных записей.'
      )

      // 9. Отчёт тоже проверяется на обеих ширинах.
      await scanAtBothWidths(page, '/barber/import (отчёт)', testInfo)

      await screen.openBookingsButton.click()
      await expect(page).toHaveURL(/\/barber$/)
      await cabinet.openTab('Ближайшие')
      await expect(cabinet.panel.getByText(GUEST_FULL_NAME)).toBeVisible()

      // 10. Тот же файл ещё раз: ключ по UID возвращает ту же запись.
      await screen.goto()
      await expect(screen.heading).toBeVisible()
      await expect(screen.calendarSource).toBeChecked()

      await screen.uploadCalendar(CALENDAR, FILE_NAME)
      await screen.parseCalendar()

      await expect(screen.icsSummary).toHaveText(/^Выбрано 1 из 1/)
      await screen.createButton.click()

      await expect(screen.report).toHaveText(
        'Перенесено 1 из 1 выбранных записей.'
      )

      await screen.openBookingsButton.click()
      await cabinet.openTab('Ближайшие')
      await expect(cabinet.panel.getByText(GUEST_FULL_NAME)).toHaveCount(1)
    } finally {
      // 11. Уборка: отменить созданное, иначе следующая сцена увидит лишнюю строку.
      await cancelBookingsByGuestName(page.request, GUEST_LAST_NAME)
    }
  })
})
