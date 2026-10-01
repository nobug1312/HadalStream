import type { ComponentType, ReactNode } from 'react'
import { CircleAlertIcon, CircleCheckIcon, FilmIcon, LayersIcon, LoaderIcon, MoonIcon, Settings2Icon, SunIcon } from 'lucide-react'
import { cn } from 'cn'
import { Progress } from '@/components/ui/progress'
import { AppLogo, IconAction, Seigaiha } from '@/components/common'
import { formatBytes, views, type Conversion, type Job, type Theme, type View } from '@/api'

export type Page = View | 'convert' | 'settings'

const nav: { view: View; label: string; icon: ComponentType<{ className?: string }> }[] = [
  { view: 'all', label: 'All', icon: LayersIcon },
  { view: 'active', label: 'In progress', icon: LoaderIcon },
  { view: 'completed', label: 'Completed', icon: CircleCheckIcon },
  { view: 'failed', label: 'Failed', icon: CircleAlertIcon },
]

export function Sidebar({
  current,
  onNavigate,
  jobs,
  conversions,
  theme,
  onTheme,
}: {
  current: Page
  onNavigate: (page: Page) => void
  jobs: Job[]
  conversions: Conversion[]
  theme: Theme
  onTheme: (theme: Theme) => void
}) {
  const running = jobs.filter((j) => j.status === 'Downloading')
  const total = running.reduce((sum, j) => sum + j.totalBytes, 0)
  const done = running.reduce((sum, j) => sum + j.downloadedBytes, 0)
  const speed = running.reduce((sum, j) => sum + j.speed, 0)
  const converting = conversions.filter((c) => c.status === 'Queued' || c.status === 'Converting').length

  return (
    <aside className="relative flex w-60 shrink-0 flex-col px-3 py-3">
      <Seigaiha className="pointer-events-none absolute inset-x-0 bottom-0 h-[84px] w-full [mask-image:linear-gradient(to_top,black_20%,transparent)]" />
      <div className="flex items-center gap-3 px-2 pt-1 pb-6">
        <AppLogo />
        <div className="min-w-0">
          <p className="truncate text-sm leading-tight font-bold tracking-tight">HadalStream</p>
          <p className="truncate text-xs text-muted-foreground">Abyss · HydraX · Dood</p>
        </div>
      </div>

      <nav className="flex flex-col gap-0.5" aria-label="Library">
        <p className="px-2 pb-1.5 text-[11px] font-medium tracking-[0.08em] text-muted-foreground/70 uppercase">Library</p>
        {nav.map(({ view, label, icon: Icon }) => {
          const count = jobs.filter(views[view].match).length
          return (
            <NavItem key={view} active={current === view} onClick={() => onNavigate(view)}>
              <Icon className="size-4 opacity-80" />
              {label}
              {count > 0 && <span className="ml-auto text-xs text-muted-foreground tabular-nums">{count}</span>}
            </NavItem>
          )
        })}
      </nav>

      <nav className="mt-6 flex flex-col gap-0.5" aria-label="Tools">
        <p className="px-2 pb-1.5 text-[11px] font-medium tracking-[0.08em] text-muted-foreground/70 uppercase">Tools</p>
        <NavItem active={current === 'convert'} onClick={() => onNavigate('convert')}>
          <FilmIcon className="size-4 opacity-80" />
          Convert
          {converting > 0 && <span className="ml-auto text-xs text-muted-foreground tabular-nums">{converting}</span>}
        </NavItem>
      </nav>

      <div className="relative mt-auto flex flex-col gap-2">
        {running.length > 0 && (
          <div
            className="rounded-xl border bg-card/90 p-3 shadow-xs transition-opacity duration-200 ease-out starting:opacity-0"
            aria-live="polite"
          >
            <div className="flex items-baseline justify-between text-xs text-muted-foreground">
              <span>{running.length} downloading</span>
              {total > 0 && <span className="tabular-nums">{Math.floor((done / total) * 100)}%</span>}
            </div>
            <p className="mt-1 text-lg font-bold tracking-tight tabular-nums">{formatBytes(speed)}/s</p>
            <Progress value={total > 0 ? (done / total) * 100 : 0} className="mt-2" />
          </div>
        )}
        <div className="flex items-center gap-1">
          <NavItem active={current === 'settings'} onClick={() => onNavigate('settings')} className="flex-1">
            <Settings2Icon className="size-4 opacity-80" />
            Settings
          </NavItem>
          <IconAction
            label={theme === 'dark' ? 'Day' : 'Night'}
            onClick={() => onTheme(theme === 'dark' ? 'light' : 'dark')}
          >
            {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
          </IconAction>
        </div>
      </div>
    </aside>
  )
}

function NavItem({
  active,
  onClick,
  className,
  children,
}: {
  active: boolean
  onClick: () => void
  className?: string
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-current={active ? 'page' : undefined}
      className={cn(
        'flex h-8 items-center gap-2.5 rounded-lg px-2.5 text-[13px] font-medium text-sidebar-foreground transition-colors duration-150 outline-none hover:bg-sidebar-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 aria-[current=page]:bg-card aria-[current=page]:text-foreground aria-[current=page]:shadow-xs',
        className,
      )}
    >
      {children}
    </button>
  )
}
