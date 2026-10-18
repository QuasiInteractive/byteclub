import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In development the .NET API runs on :5165; in production it serves the built site itself.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': 'http://localhost:5165' } },
  build: { outDir: '../api/wwwroot', emptyOutDir: true },
})
