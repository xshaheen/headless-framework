# Dashboard front end

These rules cover every dashboard front end: the shell (`apps/shell`), the SDK (`packages/sdk`, `@headless/dashboard-sdk`), the Vite preset (`packages/vite`, `@headless/dashboard-vite`), and the modules in `src/Headless.Jobs.Dashboard/wwwroot` and `src/Headless.Messaging.Dashboard/wwwroot`. The repository-root `AGENTS.md` still applies. What a module may import, the SDK entries, list views, and the size budgets are the consumer contract in [docs/llms/dashboards.md](../../../docs/llms/dashboards.md#module-front-end); read that section before you add a view or an SDK component.

## Stack

React 19 and TypeScript in strict mode (`tsconfig.base.json`: `noUncheckedIndexedAccess`, `verbatimModuleSyntax`), built with Vite. React Router and TanStack Query come from the shell through an import map, so a module imports them only from the SDK's `/router` and `/query` entries. The SDK's components are shadcn's React Aria components, styled by a Tailwind build that compiles only the SDK's and the shell's sources. Icons are Lucide through the SDK's `/icons` entry, and charts are Recharts through `/charts`. Tests run on Vitest with Testing Library and jsdom. Every package is a member of the npm workspace at the repository root, which owns the one `package-lock.json`.

## Build and check

- Run `make dashboard-shell-test`, `make dashboard-jobs-test`, or `make dashboard-messaging-test` for the code you changed, or `make dashboards-test` for all three. Each one checks formatting, lints, type-checks, runs Vitest, and builds against the size budgets in `packages/vite/budget.json`; CI's dashboard jobs run the same targets.
- Run scripts through `make` or `npm run`, never `npx`: a wrapper on `PATH` can print over the tool's output and hide a failure.
- Node 22.18 or later. Add a package only at a version at least 7 days old, pin it exactly, and keep the lockfile change in the same commit.

## Formatting

Prettier owns formatting, configured once in the repository-root `.prettierrc.json`: no semicolons, single quotes, 120 columns. Run `make dashboards-format` before you commit instead of formatting by hand. The dashboard check fails on an unformatted file.

## Code style

- Write function components and hooks; no classes.
- Name files and folders in kebab-case (`run-page.tsx`, `date-range/`), and import local files with their extension (`'./runs.ts'`).
- A route view's file has one default-exported function component (`export default function RunsView`), because the module's `lazy: () => import(...)` loads it. Everything else uses named exports.
- Declare top-level functions with `function`, not as arrow constants, so they hoist and read as declarations.
- Use `import type` for type-only imports.
- Describe an object shape with an `interface`; keep `type` for unions and derived types.
- Use no `enum`. Write the values as an `as const` array and derive the union from it, as `runStatuses` and `RunStatus` do in the Jobs module.
- Model a value that is one of several states as a discriminated union, and handle every case.
- Treat data from the server as untrusted at the boundary: type it, and narrow `unknown` with checks rather than a cast.
- Name booleans as questions (`isPending`, `hasMore`, `canOperate`).
- Keep a file to one concern. Put a helper two views share in the module's own shared file (`parts.tsx` in Jobs); put one two modules share in the SDK.
- Server data lives in TanStack Query (the SDK's `/data` hooks); never copy it into another store. Keep other state local to the component that owns it. When client state must be shared by components that are not parent and child, hold it in a Zustand store rather than a React context or module-level variables.
- A comment says why the code is shaped this way. It never restates the code.

## UI

- A module ships no CSS: no stylesheet, no `style` attribute, and no `className`. Each module's lint configuration and the preset's manifest check enforce this. Compose SDK components; when one is missing, add it to the SDK with its tests and document it in the dashboards guide.
- Tailwind compiles only the SDK's and the shell's sources, so only SDK components and the shell write its classes, and only the semantic classes mapped to `tokens.css`. The shell has no stylesheet of its own: a rule another front end could reuse becomes an SDK token, utility, or component, and the rest are utilities on the shell's elements. Dark is the default theme and light is the other; use logical properties so a right-to-left page mirrors.
- A status is a coloured dot and a word, never an icon. Icons decorate stat cards, card titles, actions, and empty states.
- Draw a chart only from a series the server serves. A point the server did not measure is a gap, never zero, and a percentage appears only when every count behind it is exact.
- Every view handles loading, empty, error, and stale data explicitly. A failed request never shows as an empty list.
- Keep keyboard behaviour and visible focus on every control, and give each one an accessible name. A disabled action stays focusable and says why through its `disabledReason`.
- An action on an existing object (cancel, requeue, edit, delete) is a shell command, with its confirmation, reason, and operator log, and the view shows the server's answer in place, refusals included. Creating an object through a module endpoint needs operate permission.

## Performance

- Each route view is its own lazy chunk. Import the SDK's heavier entries (`/table`, `/charts`, `/date-range`) only from the views that need them, so the first page does not load them.
- Never bundle React, React Aria, React Router, or TanStack Query into a module; the preset fails the build when one appears.
- Memoize only when a measurement or a referential identity requires it.
- A budget overrun fails the build. Bring the size back under budget; raising the budget is a separate, explained change.

## Tests

- Tests live in each package's `test/` folder. A module test mounts the module with `renderModule` from `@headless/dashboard-sdk/testing` and answers its API calls from `test/fake-server.ts`.
- Test through what the user sees: roles, accessible names, and the requests sent. Do not assert a component's internals.
- A test fails when the behaviour it names breaks. Use no snapshot of rendered markup as the proof of a behaviour.
