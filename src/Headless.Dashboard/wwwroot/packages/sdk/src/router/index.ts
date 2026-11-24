// Modules reach React Router only through this entry, so the shell's one copy, and its router context, serves all.
export {
  createBrowserRouter,
  createMemoryRouter,
  generatePath,
  isRouteErrorResponse,
  Link,
  matchPath,
  Navigate,
  NavLink,
  Outlet,
  RouterProvider,
  useBeforeUnload,
  useBlocker,
  useHref,
  useLocation,
  useMatch,
  useNavigate,
  useParams,
  useResolvedPath,
  useRouteError,
} from 'react-router'
export { useSearchParams } from './search-params.ts'
export type {
  LinkProps,
  Location,
  NavLinkProps,
  NavigateFunction,
  Params,
  RouteObject,
  SetURLSearchParams,
  To,
} from 'react-router'
