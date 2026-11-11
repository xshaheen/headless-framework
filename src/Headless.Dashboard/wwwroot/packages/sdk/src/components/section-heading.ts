import { createContext, useContext } from 'react'

/**
 * The element a section heading inside a detail panel takes. Docked under a list the panel's name is an `h2`, so its
 * sections are `h3`; on the object's own page the name is the page's `h1`, so they step up to `h2` and the outline a
 * screen reader walks has no gap.
 */
export const SectionHeading = createContext<'h2' | 'h3'>('h3')

/** The section heading element for where the component renders. */
export function useSectionHeading() {
  return useContext(SectionHeading)
}
