import tseslint from 'typescript-eslint'

export default tseslint.config(
  {
    name: 'messaging-dashboard/files-to-ignore',
    ignores: ['**/dist/**', '**/coverage/**', '**/node_modules/**'],
  },
  {
    name: 'messaging-dashboard/files-to-lint',
    files: ['**/*.{ts,tsx,js}'],
    extends: [tseslint.configs.recommended],
  },
  {
    // A module view composes SDK components, so the design system stays in one place: it ships no CSS and no
    // inline style objects.
    name: 'messaging-dashboard/modules-ship-no-styles',
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': [
        'error',
        { selector: "JSXAttribute[name.name='style']", message: 'A module view ships no style objects; compose SDK components.' },
        {
          selector: "JSXAttribute[name.name='className'] Literal[value=/hd-/]",
          message:
            'A module view names no SDK class: the hd-* classes are SDK internals, so naming one couples the view to markup the SDK may change. Compose SDK components such as Banner, Text, Fields, or Skeleton.',
        },
        {
          selector: "JSXAttribute[name.name='className'] TemplateElement[value.raw=/hd-/]",
          message:
            'A module view names no SDK class: the hd-* classes are SDK internals, so naming one couples the view to markup the SDK may change. Compose SDK components such as Banner, Text, Fields, or Skeleton.',
        },
      ],
      'no-restricted-imports': ['error', { patterns: [{ group: ['*.css'], message: 'A module ships no CSS; compose SDK components.' }] }],
    },
  },
)
