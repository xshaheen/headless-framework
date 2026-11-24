import { useLocation, useSearchParams as useRouterSearchParams, type SetURLSearchParams } from 'react-router'

/**
 * React Router's `useSearchParams`, except that a change of the search params keeps the history state the view was
 * opened with, unless the change sets its own. A view can hold a filter in history state, such as an id that must stay
 * out of the URL, and every other control that writes the URL (a row opened, a sort, a range) keeps it.
 */
export function useSearchParams(...init: Parameters<typeof useRouterSearchParams>) {
  const [params, setRouterParams] = useRouterSearchParams(...init)
  const state: unknown = useLocation().state
  const setParams: SetURLSearchParams = (next, options) =>
    setRouterParams(next, options && 'state' in options ? options : { ...options, state })

  return [params, setParams] as const
}
