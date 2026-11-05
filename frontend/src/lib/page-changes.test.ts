import { describe, expect, it } from 'vitest'

import { diffPageItems } from '@/lib/page-changes'

type Item = { id: string; status: 'Pending' | 'Confirmed' }

const getId = (item: Item): string => item.id
const statusChanged = (before: Item, after: Item): boolean =>
  before.status !== after.status
const pending = (id: string): Item => ({ id, status: 'Pending' })
const confirmed = (id: string): Item => ({ id, status: 'Confirmed' })

describe('diffPageItems', () => {
  it('находит новую строку, исчезнувшую и изменившуюся', () => {
    const change = diffPageItems(
      [pending('a'), pending('b'), pending('c')],
      [confirmed('b'), pending('c'), pending('new')],
      getId,
      statusChanged
    )

    expect(change.added.map(getId)).toStrictEqual(['new'])
    expect(change.removed.map(getId)).toStrictEqual(['a'])
    expect(change.updated).toStrictEqual([
      { before: pending('b'), after: confirmed('b') },
    ])
  })

  it('перестановка строк без новых id — не изменение', () => {
    const change = diffPageItems(
      [pending('a'), pending('b')],
      [pending('b'), pending('a')],
      getId,
      statusChanged
    )

    expect(change.added).toStrictEqual([])
    expect(change.removed).toStrictEqual([])
    expect(change.updated).toStrictEqual([])
  })

  it('одинаковые страницы дают пустой результат', () => {
    const items = [pending('a'), confirmed('b')]

    expect(diffPageItems(items, [...items], getId, statusChanged)).toStrictEqual(
      { added: [], removed: [], updated: [] }
    )
  })
})
