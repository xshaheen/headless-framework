import tseslint from 'typescript-eslint'

export default tseslint.config(
  {
    name: 'dashboard/files-to-ignore',
    ignores: ['**/dist/**', '**/coverage/**', '**/node_modules/**'],
  },
  {
    name: 'dashboard/files-to-lint',
    files: ['**/*.{ts,tsx,js}'],
    extends: [tseslint.configs.recommended],
  },
)
