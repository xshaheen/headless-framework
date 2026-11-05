import tseslint from 'typescript-eslint'

export default tseslint.config(
  {
    name: 'jobs-dashboard/files-to-ignore',
    ignores: ['**/dist/**', '**/coverage/**', '**/node_modules/**'],
  },
  {
    name: 'jobs-dashboard/files-to-lint',
    files: ['**/*.{ts,tsx,js}'],
    extends: [tseslint.configs.recommended],
  },
)
