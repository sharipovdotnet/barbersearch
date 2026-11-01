import { test as base } from '@playwright/test'

/**
 * Базовый `test` этапа.
 *
 * Зачем не голый `@playwright/test`: лимитер чтений бэкенда считает запросы
 * по IP — 120 в минуту (`ReadPermitLimit`). Параллельный E2E-прогон упирается
 * в это раньше, чем в интерфейс, и падает не по делу. Поэтому каждый тест
 * получает свою партицию лимитера: dev-профиль API доверяет
 * `X-Forwarded-For` от прокси (`ForwardedHeaders:KnownProxies` = 127.0.0.1),
 * Vite прокидывает заголовок, а браузерный контекст его выставляет.
 *
 * Это изоляция тестов, а не отключение лимитера: сам лимитер и его значения
 * не меняются, и любая проверка на 429 остаётся возможной.
 */
export const test = base.extend({
  context: async ({ context }, run, testInfo) => {
    // Ключ уникален для «проект + файл + строка + название»: одного `testId`
    // мало, одинаковые сценарии в desktop и mobile делят его значение.
    const key = [
      testInfo.project.name,
      testInfo.file,
      String(testInfo.line),
      testInfo.title,
    ].join('|')

    await context.setExtraHTTPHeaders({ 'X-Forwarded-For': testIp(key) })

    await run(context)
  },
})

export { expect } from '@playwright/test'

export type { Page } from '@playwright/test'

/** Стабильный «адрес» теста: 10.x.y.z из хеша ключа. */
function testIp(id: string): string {
  let hash = 7

  for (const char of id) {
    hash = (hash * 31 + char.charCodeAt(0)) % 16_777_216
  }

  const second = 1 + (hash % 254)
  const third = 1 + (Math.floor(hash / 254) % 254)
  const fourth = 1 + (Math.floor(hash / 64_516) % 254)

  return `10.${second}.${third}.${fourth}`
}
