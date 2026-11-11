'use no memo'

// The icons are their own entry, and the shell loads them with its first page, since its toasts and filter chips draw
// the close icon. Each is re-exported by name: the shell's import-map entry keeps every export, so `export *` from
// lucide would ship all of its icons.

import type { LucideIcon } from 'lucide-react'
import {
  Ban,
  Calendar,
  CirclePlay,
  CalendarClock,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChartLine,
  Copy,
  CopyPlus,
  Eraser,
  ExternalLink,
  Funnel,
  Inbox,
  LayoutDashboard,
  List,
  Lock,
  LockOpen,
  PanelBottom,
  PanelRight,
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
  Unplug,
  Users,
  X,
  Zap,
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
function curate(glyph: LucideIcon, fixed?: string): IconComponent {
  return ({ className }) =>
    createElement(glyph, {
      'aria-hidden': true,
      focusable: false,
      strokeWidth: 1.5,
      // A caller's own text colour replaces the muted tone rather than racing it in the stylesheet's order.
      className: cn('size-(--icon) shrink-0', !className?.includes('text-') && 'text-muted-foreground', fixed, className),
    })
}

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
/** Back one step, such as the previous month; a right-to-left page mirrors it with `rtl:rotate-180`. */
export const PreviousIcon = curate(ChevronLeft)
/** Forward one step, such as the next month; a right-to-left page mirrors it with `rtl:rotate-180`. */
export const NextIcon = curate(ChevronRight)
/** Opens a list of choices under a chip or a menu button. */
export const DropdownIcon = curate(ChevronDown)
export const DeleteIcon = curate(Trash2)
/** Remove a stored object whatever its state, such as a run a worker still holds. */
export const ForceDeleteIcon = curate(Eraser)
/** Retry a run, or requeue a message. */
export const RetryIcon = curate(RotateCcw)
export const HoldIcon = curate(Lock)
export const ReleaseIcon = curate(LockOpen)
export const PingIcon = curate(Radio)
/** Resume a paused schedule; `PlayIcon` stays for running something now. */
export const ResumeIcon = curate(CirclePlay)
/** Send a stored message now, ahead of its schedule. */
export const DispatchIcon = curate(Zap)
/** Force a consumer's circuit breaker open, which stops it consuming. */
export const OpenCircuitIcon = curate(Unplug)
/** The detail panel's window controls; the side one points at the inline end, so a right-to-left page mirrors it. */
export const MoveSideIcon = curate(PanelRight, 'rtl:-scale-x-100')
export const MoveBelowIcon = curate(PanelBottom)

export const ChartIcon = curate(ChartLine)
export const ListIcon = curate(List)
