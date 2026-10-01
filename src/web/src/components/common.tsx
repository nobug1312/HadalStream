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

// Seigaiha (blue sea waves): rows of concentric circles, each row tucked over the one above.
export function Seigaiha({ className }: { className?: string }) {
  const r = 14
  const cells = Array.from({ length: 9 }, (_, row) => Array.from({ length: 10 }, (_, col) => ({ row, col }))).flat()
  return (
    <svg aria-hidden viewBox="0 0 240 84" className={cn('stroke-primary/25', className)} fill="none" strokeWidth="1">
      {cells.map(({ row, col }) => {
        const cx = col * 2 * r - (row % 2) * r
        const cy = r + (row * r) / 2
        return (
          <g key={`${row}-${col}`}>
            <circle cx={cx} cy={cy} r={r} className="fill-sidebar" />
            <circle cx={cx} cy={cy} r={r * 0.68} />
            <circle cx={cx} cy={cy} r={r * 0.36} />
          </g>
        )
      })}
    </svg>
  )
}

// Paper boat on a calm sea: sun and clouds by day, moon and stars by night.
export function PaperBoat({ className }: { className?: string }) {
  return (
    <svg aria-hidden viewBox="0 0 200 110" className={className} fill="none" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      <g className="dark:hidden">
        <circle cx="152" cy="30" r="12" className="fill-warning/20 stroke-warning/70" />
        <path d="M34 40h30a7 7 0 0 0-7-9 10 10 0 0 0-18 1 6 6 0 0 0-5 8z" className="fill-card stroke-muted-foreground/50 motion-safe:animate-drift" />
      </g>
      <g className="hidden dark:inline">
        <circle cx="152" cy="30" r="12" className="fill-primary/25 stroke-primary/70" />
        <circle cx="158" cy="25" r="10" className="fill-background" />
        <path d="M42 20v6m-3-3h6M70 34v4m-2-2h4M120 16v4m-2-2h4" className="stroke-primary/70" />
      </g>
      <g className="origin-center [transform-box:fill-box] motion-safe:animate-bob">
        <path d="M72 70h56l-11 14H83z" className="fill-card stroke-foreground/60" />
        <path d="M86 70l14-27 14 27M100 43v27" className="fill-card stroke-foreground/60" />
      </g>
      <path d="M12 88q12-6 24 0t24 0 24 0 24 0 24 0 24 0 24 0" className="stroke-primary/50" />
      <path d="M30 98q12-5 24 0t24 0 24 0 24 0 24 0 24 0" className="stroke-primary/25" />
    </svg>
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
}: {
  label: string
  onClick: () => void
  children: ReactNode
  className?: string
}) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label={label}
          onClick={onClick}
          className={cn('text-muted-foreground hover:text-foreground', className)}
        >
          {children}
        </Button>
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  )
}
