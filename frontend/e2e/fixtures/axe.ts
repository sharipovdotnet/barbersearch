import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type TestInfo } from '@playwright/test'

/**
 * Общий axe-гейт (§11, §15).
 *
 * Правило одно на все экраны и ширины: блокируют только `critical` и `serious`,
 * `moderate`/`minor` попадают в аннотации отчёта. Allow-list'а нет: любая
 * критичная находка — падающий тест, а не запись в списке исключений
 * (исключение допустимо только с номером долга в `AGENT.md` §14).
 */
const BLOCKING_IMPACTS = new Set(['critical', 'serious'])

export async function expectNoBlockingViolations(
  page: Page,
  screen: string,
  testInfo: TestInfo
): Promise<void> {
  const results = await new AxeBuilder({ page }).analyze()

  const blocking = results.violations.filter(
    (violation) =>
      violation.impact !== null &&
      violation.impact !== undefined &&
      BLOCKING_IMPACTS.has(violation.impact)
  )
  const advisory = results.violations.filter(
    (violation) => !blocking.includes(violation)
  )

  testInfo.annotations.push({
    type: 'axe-summary',
    description:
      advisory.length === 0
        ? `${screen}: находок нет`
        : `${screen}: ${advisory.map((violation) => `${violation.id} (${violation.impact})`).join(', ')}`,
  })

  for (const violation of advisory) {
    testInfo.annotations.push({
      type: 'axe-advisory',
      description: `${screen}: ${violation.id} (${violation.impact}), ${violation.nodes.length} узл. — ${violation.help}`,
    })
  }

  const summary = blocking
    .map(
      (violation) =>
        `${violation.id} (${violation.impact}): ${violation.help} — ${violation.nodes
          .map((node) => node.target.join(' '))
          .slice(0, 3)
          .join(', ')}`
    )
    .join('\n')

  expect(blocking, `${screen}: axe critical/serious\n${summary}`).toEqual([])
}
