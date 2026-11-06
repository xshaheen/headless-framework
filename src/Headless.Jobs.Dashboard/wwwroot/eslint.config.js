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
  {
    // A module view composes SDK components, so the design system stays in one place: it ships no CSS and no
    // inline style objects.
    name: 'jobs-dashboard/modules-ship-no-styles',
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': [
        'error',
        { selector: "JSXAttribute[name.name='style']", message: 'A module view ships no style objects; compose SDK components.' },
      ],
      'no-restricted-imports': ['error', { patterns: [{ group: ['*.css'], message: 'A module ships no CSS; compose SDK components.' }] }],
    },
  },
)
