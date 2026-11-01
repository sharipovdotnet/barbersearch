import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// Отдельный конфиг: юнит-тестам не нужны Tailwind и dev-прокси из `vite.config.ts`,
// а прод-сборка не должна знать про Vitest.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    // jsdom даёт `document.cookie` и DOM для RTL; MSW перехватывает сеть на уровне fetch.
    environment: 'jsdom',
    environmentOptions: {
      jsdom: {
        // Относительные `/api/...` и cookie должны вести себя как в приложении за прокси.
        url: 'http://localhost:5173/',
      },
    },
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    exclude: ['**/node_modules/**', '**/dist/**'],
    // Глобальные `describe`/`it` требуют расширения `types` в `tsconfig.app.json`;
    // импортируем их из `vitest` явно (AGENT.md §10.2).
    globals: false,
    css: false,
    restoreMocks: true,
    clearMocks: true,
    unstubGlobals: true,
  },
})
