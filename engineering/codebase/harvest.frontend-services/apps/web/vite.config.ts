import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import { colorModeScript } from '@wow-two-beta/fe/foundation/primitives';
import { defineConfig, type Plugin } from 'vite';
import mkcert from 'vite-plugin-mkcert';
import { ThemeStorageKey } from './src/bootstrap/theme';

/** Holds the build's version: CI passes `APP_VERSION`; a local build is `dev`. */
const version = process.env.APP_VERSION || 'dev';

/**
 * Serves and emits `/theme.js`: the fe sdk's pre-paint colour script for the console's storage key, so a dark console
 * never flashes light. A same-origin file rather than an inline script, so a strict Content-Security-Policy needs no
 * hash for it.
 */
function themeScript(): Plugin {
  const source = colorModeScript({ storageKey: ThemeStorageKey });
  return {
    name: 'harvest-theme-script',
    configureServer(server) {
      server.middlewares.use('/theme.js', (_request, response) => {
        response.setHeader('Content-Type', 'text/javascript');
        response.end(source);
      });
    },
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'theme.js', source });
    },
  };
}

export default defineConfig(({ command, mode }) => {
  const https = !['http', 'demo'].includes(mode) && process.env.VITE_HTTPS !== 'false';

  return {
    base: '/',
    define: { __APP_VERSION__: JSON.stringify(version) },
    plugins: [
      vue(),
      tailwindcss(),
      themeScript(),
      ...(command === 'serve' && https ? [mkcert()] : []),
      {
        name: 'harvest-console-manifest',
        generateBundle() {
          this.emitFile({
            type: 'asset',
            fileName: 'harvest-console.json',
            source: JSON.stringify({
              contract: 'harvest.console.v1',
              version,
              sourceRevision: process.env.HARVEST_SOURCE_REVISION ?? process.env.VCS_REF ?? null,
            }),
          });
        },
      },
    ],
    build: { outDir: 'dist', emptyOutDir: true, target: 'es2022' },
    server: {
      host: 'localhost',
      port: 8398,
      strictPort: true,
      https: https ? {} : undefined,
      proxy: {
        '/api': {
          target: process.env.VITE_API_TARGET ?? 'https://localhost:8396',
          changeOrigin: false,
          secure: false,
        },
      },
    },
  };
});
