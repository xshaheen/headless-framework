// The React Compiler's memo caches cost the shell more bytes than these rare renders save, and the shell's assets
// sit near their size budget.
'use no memo'

import { Link } from '@headless/dashboard-sdk/router'
import type { BootstrapProduct } from '../bootstrap.ts'
import { productHome } from '../route-handles.ts'
import { useShell } from '../shell-context.tsx'
import type { AttentionItem, ModuleSection } from '../shell-data.ts'
import { productAttention } from './attention-popover.tsx'

interface ProductTabsProps {
  current: BootstrapProduct | undefined
  attention: ModuleSection<AttentionItem>[] | undefined
}

/**
 * One tab per product a host registered, in the dashboard's product order. Each leads with the product's worst
 * attention as a dot, and says it in words for a screen reader, so the state never rests on colour.
 */
export function ProductTabs({ current, attention }: ProductTabsProps) {
  const { state } = useShell()

  return (
    // The bar keeps every other item at full width, so the tabs alone shrink, scrolling in their track.
    <nav
      className="flex min-w-0 shrink! gap-0.5 overflow-x-auto rounded-xl border bg-background p-0.5 [scrollbar-width:none]"
      aria-label="Products"
    >
      {state.products.map((product) => {
        const { role, text } = productAttention(state, product, attention)

        return (
          <Link
            key={product.id}
            to={productHome(product)}
            // The fill alone is too faint a cue, so the active tab also carries an accent bottom edge.
            className={`inline-flex h-(--field-height) flex-none items-center gap-2 rounded-md px-3.5 font-medium text-muted-foreground no-underline hover:text-foreground current:bg-muted current:text-foreground current:accent-bar-bottom hd-tone-${role}`}
            aria-current={product.id === current?.id ? 'page' : undefined}
          >
            <span className="hd-mark" aria-hidden="true" />
            <span aria-hidden="true">{product.label}</span>
            <span className="sr-only">{text}</span>
          </Link>
        )
      })}
    </nav>
  )
}
