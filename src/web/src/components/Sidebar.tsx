import { useState, type ComponentType, type ReactNode } from 'react'
import { CircleAlertIcon, CircleCheckIcon, FilmIcon, LayersIcon, LoaderIcon, MoonIcon, Settings2Icon, SunIcon } from 'lucide-react'
import { cn } from 'cn'
import { Progress } from '@/components/ui/progress'
import { AppLogo, IconAction } from '@/components/common'
import { FlameSprite, Meadow } from '@/components/scenery'
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
  const [rainbowTarget, setRainbowTarget] = useState<'settings' | 'theme' | null>(null)
  const [rainbowRevision, setRainbowRevision] = useState(0)
  const running = jobs.filter((j) => j.status === 'Downloading')
  const total = running.reduce((sum, j) => sum + j.totalBytes, 0)
  const done = running.reduce((sum, j) => sum + j.downloadedBytes, 0)
  const speed = running.reduce((sum, j) => sum + j.speed, 0)
  const converting = conversions.filter((c) => c.status === 'Queued' || c.status === 'Converting').length

  return (
    <aside className="flex w-60 shrink-0 flex-col p-3">
      <div className="flex items-center gap-3 px-2 pt-1 pb-6">
        <AppLogo />
        <div className="min-w-0">
          <p className="rainbow-text truncate text-[15px] leading-tight font-bold tracking-tight motion-safe:animate-rainbow">HadalStream</p>
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

      <div className="mt-auto flex flex-col gap-2">
        {running.length > 0 && (
          <div
            className="flex items-center gap-3 rounded-2xl border bg-card p-3 shadow-xs transition-opacity duration-200 ease-out starting:opacity-0"
            aria-live="polite"
          >
            <FlameSprite className="h-11 w-8 shrink-0" />
            <div className="min-w-0 flex-1">
              <div className="flex items-baseline justify-between text-xs text-muted-foreground">
                <span>{running.length} downloading</span>
                {total > 0 && <span className="tabular-nums">{Math.floor((done / total) * 100)}%</span>}
              </div>
              <p className="text-base font-bold tracking-tight tabular-nums">{formatBytes(speed)}/s</p>
              <Progress value={total > 0 ? (done / total) * 100 : 0} className="mt-1.5" />
            </div>
          </div>
        )}
      </div>
      <div className="relative -mx-1 -mb-1 mt-2 h-32 shrink-0 overflow-hidden rounded-2xl">
        <Meadow />
        <div className="absolute inset-x-0 bottom-0 flex items-center gap-1 px-1 pb-1">
          <NavItem
            active={current === 'settings'}
            onClick={() => {
              setRainbowTarget('settings')
              setRainbowRevision((revision) => revision + 1)
              onNavigate('settings')
            }}
            onMouseEnter={() => {
              setRainbowTarget('settings')
              setRainbowRevision((revision) => revision + 1)
            }}
            onMouseLeave={() => setRainbowTarget((target) => (target === 'settings' ? null : target))}
            onFocus={() => {
              setRainbowTarget('settings')
              setRainbowRevision((revision) => revision + 1)
            }}
            onBlur={() => setRainbowTarget((target) => (target === 'settings' ? null : target))}
            className="flex-1 bg-transparent text-sidebar-foreground hover:bg-transparent hover:text-foreground aria-[current=page]:bg-transparent aria-[current=page]:text-foreground aria-[current=page]:shadow-none active:scale-[0.97]"
          >
            <Settings2Icon
              key={`settings-icon-${rainbowRevision}`}
              className={cn(
                'size-4 transition-colors',
                rainbowTarget === 'settings' ? 'motion-safe:animate-rainbow-icon' : current === 'settings' ? 'text-primary' : 'opacity-80',
              )}
            />
            <span
              key={`settings-label-${rainbowRevision}`}
              className={rainbowTarget === 'settings' ? 'rainbow-text motion-safe:animate-rainbow' : undefined}
            >
              Settings
            </span>
          </NavItem>
          <IconAction
            label={theme === 'dark' ? 'Day' : 'Night'}
            onClick={() => {
              setRainbowTarget('theme')
              setRainbowRevision((revision) => revision + 1)
              onTheme(theme === 'dark' ? 'light' : 'dark')
            }}
            onMouseEnter={() => {
              setRainbowTarget('theme')
              setRainbowRevision((revision) => revision + 1)
            }}
            onMouseLeave={() => setRainbowTarget((target) => (target === 'theme' ? null : target))}
            onFocus={() => {
              setRainbowTarget('theme')
              setRainbowRevision((revision) => revision + 1)
            }}
            onBlur={() => setRainbowTarget((target) => (target === 'theme' ? null : target))}
            className="bg-transparent text-foreground/80 hover:bg-transparent hover:text-foreground active:scale-[0.97]"
          >
            {theme === 'dark' ? (
              <SunIcon key={`theme-icon-${rainbowRevision}`} className={rainbowTarget === 'theme' ? 'motion-safe:animate-rainbow-icon' : undefined} />
            ) : (
              <MoonIcon key={`theme-icon-${rainbowRevision}`} className={rainbowTarget === 'theme' ? 'motion-safe:animate-rainbow-icon' : undefined} />
            )}
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
  onMouseEnter,
  onMouseLeave,
  onFocus,
  onBlur,
  children,
}: {
  active: boolean
  onClick: () => void
  className?: string
  onMouseEnter?: () => void
  onMouseLeave?: () => void
  onFocus?: () => void
  onBlur?: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      onFocus={onFocus}
      onBlur={onBlur}
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
