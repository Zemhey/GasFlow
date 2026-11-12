import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) }
  },
  server: {
    port: 5173,
    proxy: {
      // 后端端口默认 5080（appsettings 的口径）；本机 5080 落在 Windows 保留段绑不上，
      // 起服务时可用 GAS_API=http://127.0.0.1:10374 npx vite 覆盖，不必改代码。
      '/api': { target: process.env.GAS_API || 'http://localhost:5080', changeOrigin: true }
    }
  },
  build: {
    outDir: 'dist',
    chunkSizeWarningLimit: 1500
  }
})
