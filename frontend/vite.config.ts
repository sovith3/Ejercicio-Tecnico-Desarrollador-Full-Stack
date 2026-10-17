import { existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import react from '@vitejs/plugin-react'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'

// El archivo .env es unico y vive en la raiz del repositorio (junto a docker-compose.yml): Vite lo lee
// desde ahi (envDir) y docker compose usa ese mismo archivo para interpolar puertos y credenciales.
// Vite expone al navegador solo las variables con prefijo VITE_ (equivalente a REACT_APP_ en CRA).
const projectDir = fileURLToPath(new URL('.', import.meta.url))
const parentDir = fileURLToPath(new URL('..', import.meta.url))
const envDir = existsSync(parentDir) ? parentDir : projectDir

const mode = process.env.NODE_ENV === 'production' ? 'production' : 'development'
const env = loadEnv(mode, envDir, 'VITE_')

const readNumber = (value: string | undefined, fallback: number) => {
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

const devPort = readNumber(env.VITE_DEV_PORT ?? process.env.VITE_DEV_PORT, 5173)
const proxyTarget =
  env.VITE_API_PROXY_TARGET ?? process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5145'

export default defineConfig({
  plugins: [react()],
  envDir,
  server: {
    host: true,
    port: devPort,
    // En desarrollo el panel llama a rutas relativas (/api) y Vite las reenvia a la API .NET.
    proxy: {
      '/api': { target: proxyTarget, changeOrigin: true },
      '/health': { target: proxyTarget, changeOrigin: true }
    }
  },
  build: {
    outDir: 'dist',
    sourcemap: false
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts']
  }
})

