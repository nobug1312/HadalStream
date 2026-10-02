import type { ReactNode } from 'react'
import { cn } from 'cn'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

// Hanko seal with 深 ("deep"), matching the app icon.
export function AppLogo({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn(
        'grid size-9 shrink-0 -rotate-3 place-items-center rounded-[10px] bg-seal text-lg font-bold text-white shadow-sm ring-2 ring-white/25 ring-inset',
        className,
      )}
    >
      深
    </div>
  )
}

export function PageTitle({ title, kanji, aside }: { title: string; kanji: string; aside?: ReactNode }) {
  return (
    <div className="mb-4 flex items-baseline justify-between gap-4">
      <h1 className="flex items-baseline gap-3 text-xl font-bold tracking-tight">
        {title}
        <span aria-hidden className="font-mincho text-sm font-normal tracking-[0.25em] text-muted-foreground/70">
          {kanji}
        </span>
      </h1>
      {aside && <p className="text-sm text-muted-foreground tabular-nums">{aside}</p>}
    </div>
  )
}

export function Kbd({ children }: { children: ReactNode }) {
  return (
    <kbd className="rounded border bg-muted px-1 py-px font-sans text-[11px] font-medium text-foreground/80">{children}</kbd>
  )
}

export function IconAction({
  label,
  onClick,
  children,
  className,
  onMouseEnter,
  onMouseLeave,
  onFocus,
  onBlur,
}: {
  label: string
  onClick: () => void
  children: ReactNode
  className?: string
  onMouseEnter?: () => void
  onMouseLeave?: () => void
  onFocus?: () => void
  onBlur?: () => void
}) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label={label}
          onClick={onClick}
          onMouseEnter={onMouseEnter}
          onMouseLeave={onMouseLeave}
          onFocus={onFocus}
          onBlur={onBlur}
          className={cn('text-muted-foreground hover:text-foreground', className)}
        >
          {children}
        </Button>
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  )
}
