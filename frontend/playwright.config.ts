import { defineConfig, devices } from '@playwright/test'

/**
 * E2E-конфиг. Бэкенд Playwright не поднимает: API должен уже работать на
 * `http://localhost:5243` (профиль `http`), а Vite проксирует на него `/api` (§5.1).
 * Готовность API проверяет `globalSetup` — понятной ошибкой, а не молчаливым skip.
 */
const isCI = process.env.CI !== undefined && process.env.CI !== ''
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5173'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCI,
  retries: isCI ? 1 : 0,
  workers: isCI ? 2 : undefined,
  timeout: 30_000,
  expect: { timeout: 7_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  globalSetup: './e2e/global-setup.ts',

  use: {
    baseURL,
    // Анимации не должны влиять на синхронизацию тестов (§5).
    reducedMotion: 'reduce',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  projects: [
    // Вход через API и storageState на роль: UI-вход проверяется одним сценарием,
    // а не в каждой спеке (§3.5).
    {
      name: 'auth',
      testMatch: /auth\.setup\.ts/,
    },
    {
      name: 'desktop-chromium',
      dependencies: ['auth'],
      testIgnore: [/pager-320\.spec\.ts/, /mutations\.spec\.ts/],
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } },
    },
    {
      name: 'mobile-chromium',
      dependencies: ['auth'],
      // Мутирующие сцены меняют фикстуры — гоняем их один раз, на десктопе.
      testIgnore: [/pager-320\.spec\.ts/, /mutations\.spec\.ts/],
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 375, height: 812 },
        isMobile: true,
        hasTouch: true,
        deviceScaleFactor: 2,
      },
    },
    // Мутации идут последними: читающие спеки не должны видеть изменённые фикстуры,
    // даже если параллельный прогон случайно совпал по данным.
    {
      name: 'mutations-chromium',
      dependencies: ['desktop-chromium', 'mobile-chromium'],
      testMatch: /mutations\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } },
    },
    // Узкий экран — только там, где он важен: одна строка пагинатора и 44×44.
    {
      name: 'narrow-320',
      dependencies: ['auth'],
      testMatch: /pager-320\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 320, height: 800 },
        isMobile: true,
        hasTouch: true,
        deviceScaleFactor: 2,
      },
    },
  ],

  webServer: {
    command: 'npm run dev',
    url: baseURL,
    reuseExistingServer: !isCI,
    timeout: 120_000,
  },
})
