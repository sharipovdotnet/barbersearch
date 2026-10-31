/**
 * Словари публичной карточки барбера.
 *
 * API отдаёт enum-значения строками (`Haircut`, `Ru`, `Sbp`), а интерфейс — русский,
 * поэтому перевод живёт рядом с типами, а не в компонентах: один словарь на все секции
 * страницы и никаких английских подписей в разметке.
 */

export type ServiceCategory =
  | 'Haircut'
  | 'Beard'
  | 'Coloring'
  | 'Styling'
  | 'HairTreatment'
  | 'Other'

/** Категория услуги: `Haircut` → «Стрижка». */
export const SERVICE_CATEGORY_LABELS: Record<ServiceCategory, string> = {
  Haircut: 'Стрижка',
  Beard: 'Борода',
  Coloring: 'Окрашивание',
  Styling: 'Укладка',
  HairTreatment: 'Уход',
  Other: 'Другое',
}

export type Language = 'Ru' | 'En' | 'De' | 'Kg' | 'Tj' | 'Uz'

/** Язык общения: значение — самоназвание, чтобы носитель узнал язык без перевода. */
export const LANGUAGE_LABELS: Record<Language, string> = {
  Ru: 'Русский',
  En: 'English',
  De: 'Deutsch',
  Kg: 'Кыргызча',
  Tj: 'Тоҷикӣ',
  Uz: 'Oʻzbekcha',
}

export type PaymentMethod = 'Cash' | 'Card' | 'DigitalWallet' | 'Sbp' | 'BankTransfer'

/** Способ оплаты: `Sbp` → «СБП». */
export const PAYMENT_METHOD_LABELS: Record<PaymentMethod, string> = {
  Cash: 'Наличные',
  Card: 'Карта',
  DigitalWallet: 'Цифровой кошелёк',
  Sbp: 'СБП',
  BankTransfer: 'Перевод',
}

export type DayOfWeek =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday'

/** День недели: `Monday` → «Понедельник». */
export const DAY_OF_WEEK_LABELS: Record<DayOfWeek, string> = {
  Sunday: 'Воскресенье',
  Monday: 'Понедельник',
  Tuesday: 'Вторник',
  Wednesday: 'Среда',
  Thursday: 'Четверг',
  Friday: 'Пятница',
  Saturday: 'Суббота',
}

/** Порядок строк расписания: неделя начинается с понедельника, как в календаре. */
export const DAY_OF_WEEK_ORDER: readonly DayOfWeek[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
]
