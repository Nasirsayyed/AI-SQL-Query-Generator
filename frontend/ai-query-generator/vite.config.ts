/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// VITE_BASE_PATH controls the public base path so the SPA can be hosted at the site root ("/")
// or in an IIS sub-application ("/aiqg/"). It must start and end with "/".
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const base = env.VITE_BASE_PATH || '/';
  return {
    base,
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': { target: env.VITE_DEV_API_PROXY || 'http://localhost:5017', changeOrigin: true },
      },
    },
    build: {
      outDir: 'dist',
      sourcemap: false,
      chunkSizeWarningLimit: 4000,
      rollupOptions: {
        output: {
          manualChunks: {
            monaco: ['monaco-editor/editor/editor.api'],
            echarts: ['echarts'],
            grid: ['@mui/x-data-grid'],
          },
        },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test/setup.ts'],
      css: false,
    },
  };
});
