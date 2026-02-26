import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import routerPlugin from '@tanstack/eslint-plugin-router'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'
import eslintConfigPrettier from 'eslint-config-prettier'

export default defineConfig([
  globalIgnores(['dist', 'src/api/generated']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
      routerPlugin.configs['flat/recommended'],
    ],
    linterOptions: {
      reportUnusedDisableDirectives: 'error',
    },
    rules: {
      'react-hooks/todo': 'warn',
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: [
                '*/api/generated/*',
                '!*/api/generated/model',
                '!*/api/generated/model/*',
              ],
              message:
                'Import from api/hooks.ts instead of generated code directly. Only type imports from generated/model/ are allowed. See api/hooks.ts for available hooks.',
            },
          ],
        },
      ],
    },
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
  },
  {
    files: ['src/api/hooks.ts', 'src/routes/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': 'off',
    },
  },
  eslintConfigPrettier,
])
