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
        {
          selector: "JSXAttribute[name.name='className']",
          message:
            'A module view sets no className: Tailwind compiles only SDK sources, so a class here has no CSS, and the hd-* classes are SDK internals. Compose SDK components such as Banner, Text, Fields, or Skeleton.',
        },
      ],
      'no-restricted-imports': ['error', { patterns: [{ group: ['*.css'], message: 'A module ships no CSS; compose SDK components.' }] }],
    },
  },
)
