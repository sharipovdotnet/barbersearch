import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // `loadEnv` читает `.env*` файлы, которых не видит `process.env`; префикс `''`
  // нужен, чтобы цель прокси можно было задать без `VITE_`-ограничения.
  const env = loadEnv(mode, process.cwd(), '')

  return {
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      proxy: {
        // ASP.NET Core API, "http" launch profile.
        '/api': {
          target: env.VITE_API_PROXY_TARGET ?? 'http://localhost:5243',
          changeOrigin: true,
        },
      },
    },
  }
})
