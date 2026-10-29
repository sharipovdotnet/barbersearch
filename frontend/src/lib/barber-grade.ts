/** Грейды приходят строками — так же, как в публичной карточке. */
export type BarberGrade =
  | 'None'
  | 'Intern'
  | 'JuniorBarber'
  | 'MiddleBarber'
  | 'SeniorBarber'
  | 'ArtDirector'
  | 'BarberSchoolTrainer'
  | 'Manager'
  | 'CompetitionBarber'
  | 'BrandAmbassador'
  | 'BarbershopOwner'

/** Русские подписи грейдов: значения из API остаются английскими. */
export const BARBER_GRADE_LABELS: Record<BarberGrade, string> = {
  None: 'Без грейда',
  Intern: 'Стажёр',
  JuniorBarber: 'Младший барбер',
  MiddleBarber: 'Барбер',
  SeniorBarber: 'Старший барбер',
  ArtDirector: 'Арт-директор',
  BarberSchoolTrainer: 'Тренер школы',
  Manager: 'Управляющий',
  CompetitionBarber: 'Соревновательный барбер',
  BrandAmbassador: 'Амбассадор бренда',
  BarbershopOwner: 'Владелец барбершопа',
}

/** Порядок в выборе грейда: от младшего к старшему, как в домене. */
export const BARBER_GRADES = Object.keys(
  BARBER_GRADE_LABELS
) as readonly BarberGrade[]
