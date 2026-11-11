import { useEffect, useState } from 'react'
import { create } from 'zustand'

/** What the mounted view calls its own page, for the shell's document title. */
export interface ViewTitle {
  /**
   * Whether a mounted view names its page. While it does and `title` is still unset, the shell waits for the name
   * before it says the new page, so a screen reader hears the run's name rather than the list it came from.
   */
  isNamed: boolean
  /** The view's name for its page, such as `billing.charge run`; unset while the view still loads what it names. */
  title?: string
}

interface ViewTitleState extends ViewTitle {
  owner?: symbol
}

/**
 * The name the mounted view gives its page, read by the shell; a module sets it through `usePageTitle`. A Zustand
 * store rather than a context, because the shell's title sits above the route outlet that renders the view.
 */
export const useViewTitle = create<ViewTitleState>(() => ({ isNamed: false }))

/**
 * Names the calling view's page while it is mounted: the shell titles the document `<title> · <product> · <served
 * title>` in place of the sidebar entry's label, and says that title once when the route changes. Pass `undefined`
 * while the view loads what it names, and a fallback such as `Run not found` once the load fails, so the shell does
 * not wait for a name that never comes.
 */
export function usePageTitle(title: string | undefined) {
  const [owner] = useState(() => Symbol('page title'))

  useEffect(() => {
    useViewTitle.setState({ owner, isNamed: true, title }, true)
  }, [owner, title])

  // Only the view that named the page clears it: a view mounted in the same commit may already have named its own.
  useEffect(
    () => () => {
      if (useViewTitle.getState().owner === owner) {
        useViewTitle.setState({ isNamed: false }, true)
      }
    },
    [owner],
  )
}
