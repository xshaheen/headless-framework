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
  useSearchParams,
} from 'react-router'
export type { LinkProps, NavLinkProps, NavigateFunction, Params, RouteObject, To } from 'react-router'
