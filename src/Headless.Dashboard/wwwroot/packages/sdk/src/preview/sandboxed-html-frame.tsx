// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

import { useState } from 'react'
import { SegmentedControl } from '../components/display/segmented-control.tsx'
import { Skeleton } from '../components/display/skeleton.tsx'
import { useDashboard } from '../context.tsx'
import { apiUrl, type ApiRequest } from '../data/api.ts'

/** `desktop` fills the column; `phone` draws the document at a phone's width. */
export type PreviewWidth = 'desktop' | 'phone'

const widths: readonly { value: PreviewWidth; label: string }[] = [
  { value: 'desktop', label: 'Desktop' },
  { value: 'phone', label: 'Phone' },
]

export interface SandboxedHtmlFrameProps {
  /** The module API path that answers with `DashboardResults.HtmlPreview`, as `api.request` takes it: `inbox/emails/42/html`. */
  path: string
  query?: ApiRequest['query']
  /** The frame's accessible name, such as the email's subject. */
  title: string
  /** The width the preview opens at; `desktop` by default. */
  defaultWidth?: PreviewWidth
}

/**
 * Captured HTML, such as an email, in a frame that runs none of it: the document loads from a module endpoint that
 * serves it under its own restrictive policy, so its styles apply and its scripts, forms, and remote loads do not. A
 * `Desktop` / `Phone` toggle sets the width, and the document scrolls inside a fixed height, because a sandboxed
 * document cannot report its own. The frame cannot tell a failed load either, so read the captured message through
 * the API first and mount the frame only for one that exists.
 */
export function SandboxedHtmlFrame({ path, query, title, defaultWidth = 'desktop' }: SandboxedHtmlFrameProps) {
  const { apiBase } = useDashboard()
  const [width, setWidth] = useState(defaultWidth)
  const src = apiUrl(apiBase, path, query)

  return (
    <div className="flex min-w-0 flex-col gap-2.5">
      <SegmentedControl label="Preview width" options={widths} value={width} onChange={setWidth} />
      {/* Keyed by its address, so another document starts behind the skeleton again. */}
      <Frame key={src} src={src} title={title} width={width} />
    </div>
  )
}

function Frame({ src, title, width }: { src: string; title: string; width: PreviewWidth }) {
  const [isLoaded, setIsLoaded] = useState(false)

  return (
    // The edge is a ring, not a border, so it takes no width and the phone document measures the full 375 px.
    // No fill of its own: the browser paints a document with no colour scheme on an opaque white canvas under the
    // dark theme, and the light card shows through under the light one, so captured text reads in both.
    <div
      className={`relative h-128 max-w-full overflow-hidden rounded-lg ring-1 ring-border ${width === 'phone' ? 'w-[375px]' : 'w-full'}`}
    >
      {isLoaded ? null : (
        <div className="absolute inset-0 bg-card p-3.5">
          <Skeleton label={`Loading ${title}`} shape="lines" rows={6} />
        </div>
      )}
      {/* The frame mounts at once, under the skeleton, because a frame that is not in the page never loads. */}
      <iframe
        src={src}
        title={title}
        sandbox=""
        referrerPolicy="no-referrer"
        className="block size-full"
        onLoad={() => setIsLoaded(true)}
      />
    </div>
  )
}
