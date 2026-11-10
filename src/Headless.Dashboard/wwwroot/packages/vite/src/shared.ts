/**
 * The bare specifiers the shell's import map resolves to its single copy, so every separately built module shares
 * one React and one SDK. The React Compiler emits imports of `react/compiler-runtime`, so it is shared too.
 */
export const reactSpecifiers = [
  'react',
  'react/jsx-runtime',
  'react-dom',
  'react-dom/client',
  'react/compiler-runtime',
] as const

export const sdkPackage = '@headless/dashboard-sdk'

/** Whether a module build leaves `specifier` to the import map instead of bundling it. */
export function isSharedSpecifier(specifier: string) {
  return (
    (reactSpecifiers as readonly string[]).includes(specifier) ||
    specifier === sdkPackage ||
    specifier.startsWith(`${sdkPackage}/`)
  )
}

/**
 * The libraries whose code must never land in a module's bundle. React and React DOM would be a second copy with
 * its own hook dispatcher; React Aria, TanStack Query, and React Router hold React context, so a module's own copy
 * would not see the shell's providers. Modules reach them only through the SDK's exports.
 */
export const isolatedLibraries: readonly { name: string; packages: readonly string[] }[] = [
  { name: 'React', packages: ['react', 'react-dom', 'scheduler'] },
  {
    name: 'React Aria',
    packages: ['react-aria-components', 'react-aria', 'react-stately', '@react-aria/', '@react-stately/'],
  },
  { name: 'TanStack Query', packages: ['@tanstack/react-query', '@tanstack/query-core'] },
  { name: 'React Router', packages: ['react-router', 'react-router-dom'] },
]
