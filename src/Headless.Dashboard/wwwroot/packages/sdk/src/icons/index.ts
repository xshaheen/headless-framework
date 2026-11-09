// The React Compiler's memo caches cost the SDK more bytes than these renders save (the file runs no effect that a
// changed identity could re-trigger), and the shell's assets sit near their size budget.
'use no memo'

// The icons are their own entry, so lucide's code loads with the first view or SDK chart that draws one, not with the
// shell. Each is re-exported by name: the shell's import-map entry keeps every export, so `export *` from lucide would
// ship all of its icons.

import type { LucideIcon } from 'lucide-react'
import {
  Ban,
  Calendar,
  CalendarClock,
  ChartLine,
  Copy,
  CopyPlus,
  ExternalLink,
  Funnel,
  Inbox,
  LayoutDashboard,
  List,
  Lock,
  LockOpen,
  Pause,
  Pencil,
  Play,
  Plus,
  Radio,
  RefreshCw,
  Repeat,
  RotateCcw,
  Rows3,
  ScrollText,
  Search,
  Send,
  Server,
  SquareFunction,
  Trash2,
  TriangleAlert,
  Users,
  X,
} from 'lucide-react'
import { createElement } from 'react'
import { cn } from '../components/cn.ts'

export interface IconProps {
  /** For SDK components only, such as a button that paints its icon in its own text colour. */
  className?: string
}

/** One of the dashboard's icons, as `StatCard`, `Card`, `EmptyState`, and `Button` take it in their `icon` slot. */
export type IconComponent = (props: IconProps) => React.ReactElement

/**
 * Fixes a lucide glyph to the dashboard's icon: 15 px, a 1.5 stroke like the sidebar's, the muted text tone through
 * `currentColor`, and hidden from assistive technology. An icon never carries a state, which stays a dot and a word,
 * so it is never the only thing that names something: a button that shows only an icon names itself in `aria-label`.
 */
function curate(glyph: LucideIcon): IconComponent {
  return ({ className }) =>
    createElement(glyph, {
      'aria-hidden': true,
      focusable: false,
      strokeWidth: 1.5,
      className: cn('size-(--icon) shrink-0 text-muted-foreground', className),
    })
}

// Pages and their summaries.
export const DashboardIcon = curate(LayoutDashboard)
export const RunsIcon = curate(Rows3)
export const RecurringIcon = curate(Repeat)
export const FunctionIcon = curate(SquareFunction)
export const NodeIcon = curate(Server)
export const PublishedIcon = curate(Send)
export const ReceivedIcon = curate(Inbox)
export const ScheduledIcon = curate(CalendarClock)
export const ConsumerIcon = curate(Users)
export const OperatorLogIcon = curate(ScrollText)
export const AttentionIcon = curate(TriangleAlert)

// Actions.
export const SearchIcon = curate(Search)
export const FilterIcon = curate(Funnel)
export const CalendarIcon = curate(Calendar)
export const RefreshIcon = curate(RefreshCw)
export const PauseIcon = curate(Pause)
export const PlayIcon = curate(Play)
export const CopyIcon = curate(Copy)
export const ExternalLinkIcon = curate(ExternalLink)
export const AddIcon = curate(Plus)
export const EditIcon = curate(Pencil)
export const DuplicateIcon = curate(CopyPlus)
export const CancelIcon = curate(Ban)
export const CloseIcon = curate(X)
export const DeleteIcon = curate(Trash2)
/** Retry a run, or requeue a message. */
export const RetryIcon = curate(RotateCcw)
export const HoldIcon = curate(Lock)
export const ReleaseIcon = curate(LockOpen)
export const PingIcon = curate(Radio)

// A chart's toggle between the drawing and the list of the same data.
export const ChartIcon = curate(ChartLine)
export const ListIcon = curate(List)
