import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
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
        target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5243',
        changeOrigin: true,
      },
    },
  },
})
