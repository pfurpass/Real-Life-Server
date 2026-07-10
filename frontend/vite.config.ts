/// <reference types="vitest/config" />
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:8080', changeOrigin: true },
      '/hubs': { target: 'http://localhost:8080', ws: true, changeOrigin: true },
      '/hls': { target: 'http://localhost:8888', changeOrigin: true }
    }
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/setupTests.ts']
  }
});
