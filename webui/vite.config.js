import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  base: './',
  build: {
    outDir: '../WinBit.Core/WebUi/WinBitApp',
    emptyOutDir: false,
    rollupOptions: {
      output: {
        // Stable filenames prevent MSBuild's static EmbeddedResource glob from referencing
        // stale hashed filenames from a prior build when Vite rewrites the output dir.
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name].[ext]',
        manualChunks: { vendor: ['vue', 'vue-router', 'pinia'] }
      }
    }
  },
  plugins: [
    vue({
      template: {
        compilerOptions: {
          isCustomElement: tag => tag.startsWith('fluent-')
        }
      }
    })
  ]
})
