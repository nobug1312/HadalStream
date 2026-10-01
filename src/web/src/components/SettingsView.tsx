import { useState, type ReactNode } from 'react'
import { FolderOpenIcon, Loader2Icon, MinusIcon, MoonIcon, PlusIcon, SunIcon } from 'lucide-react'
import { toast } from 'sonner'
import { cn } from 'cn'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { PageTitle } from '@/components/common'
import { call, toastError, type Quality, type Settings, type Theme } from '@/api'

type Form = Omit<Settings, 'headers' | 'proxy' | 'theme'> & { proxy: string; headers: string }

const toForm = (s: Settings): Form => ({
  downloadDir: s.downloadDir,
  defaultQuality: s.defaultQuality,
  maxConcurrentJobs: s.maxConcurrentJobs,
  connections: s.connections,
  proxy: s.proxy ?? '',
  headers: Object.entries(s.headers)
    .map(([k, v]) => `${k}: ${v}`)
    .join('\n'),
})

function parseHeaders(text: string) {
  const headers: Record<string, string> = {}
  for (const line of text.split('\n')) {
    const i = line.indexOf(':')
    if (i > 0) headers[line.slice(0, i).trim()] = line.slice(i + 1).trim()
  }
  return headers
}

export function SettingsView({
  settings,
  version,
  onSaved,
  onTheme,
}: {
  settings: Settings
  version: string
  onSaved: (settings: Settings) => void
  onTheme: (theme: Theme) => void
}) {
  const [draft, setDraft] = useState(() => toForm(settings))
  const [saving, setSaving] = useState(false)
  const dirty = JSON.stringify(draft) !== JSON.stringify(toForm(settings))
  const set = <K extends keyof Form>(key: K, value: Form[K]) => setDraft((d) => ({ ...d, [key]: value }))

  async function browse() {
    try {
      const folder = await call<string | null>('settings.pickFolder')
      if (folder) set('downloadDir', folder)
    } catch (e) {
      toastError(e)
    }
  }

  async function save() {
    setSaving(true)
    try {
      const saved = await call<Settings>('settings.save', {
        ...settings,
        ...draft,
        proxy: draft.proxy.trim() || null,
        headers: parseHeaders(draft.headers),
      })
      onSaved(saved)
      setDraft(toForm(saved))
      toast.success('Saved')
    } catch (e) {
      toastError(e)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="flex h-full flex-col">
      <div className="min-h-0 flex-1 overflow-y-auto">
        <div className="mx-auto max-w-3xl px-8 pt-7 pb-10">
          <PageTitle title="Settings" kanji="設定" />
          <p className="-mt-2 text-sm text-muted-foreground">Applies to new downloads.</p>

          <Section title="Downloads">
            <Row label="Download folder" htmlFor="dir" hint="Videos and HadalStream.log go here." stacked>
              <div className="flex gap-2">
                <Input id="dir" value={draft.downloadDir} onChange={(e) => set('downloadDir', e.target.value)} spellCheck={false} />
                <Button variant="outline" onClick={browse}>
                  <FolderOpenIcon /> Browse
                </Button>
              </div>
            </Row>
            <Row label="Default quality" hint="Picked first when there are several.">
              <Select value={draft.defaultQuality} onValueChange={(v) => set('defaultQuality', v as Quality)}>
                <SelectTrigger className="w-40" aria-label="Default quality">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="high">Highest</SelectItem>
                  <SelectItem value="medium">Medium</SelectItem>
                  <SelectItem value="low">Lowest</SelectItem>
                </SelectContent>
              </Select>
            </Row>
          </Section>

          <Section title="Performance">
            <Row label="Downloads at once" hint="Videos downloading together.">
              <Stepper label="Downloads at once" value={draft.maxConcurrentJobs} min={1} max={5} onChange={(v) => set('maxConcurrentJobs', v)} />
            </Row>
            <Row label="Connections" hint="Per download. 4 to 8 is usually fastest.">
              <Stepper label="Connections" value={draft.connections} min={1} max={16} onChange={(v) => set('connections', v)} />
            </Row>
          </Section>

          <Section title="Network">
            <Row label="Proxy" htmlFor="proxy" hint="Empty uses the system proxy." stacked>
              <Input
                id="proxy"
                value={draft.proxy}
                placeholder="http://127.0.0.1:8080"
                onChange={(e) => set('proxy', e.target.value)}
                spellCheck={false}
              />
            </Row>
            <Row label="Custom headers" htmlFor="headers" hint="One per line. Sent to pages only, never to video servers." stacked>
              <Textarea
                id="headers"
                rows={3}
                className="font-mono text-xs"
                value={draft.headers}
                placeholder="Referer: https://example.com/"
                onChange={(e) => set('headers', e.target.value)}
                spellCheck={false}
              />
            </Row>
          </Section>

          <Section title="Appearance">
            <Row label="Theme">
              <div role="radiogroup" aria-label="Theme" className="inline-flex rounded-xl bg-muted p-0.5">
                {(
                  [
                    ['light', 'Day', SunIcon],
                    ['dark', 'Night', MoonIcon],
                  ] as const
                ).map(([value, label, Icon]) => (
                  <button
                    key={value}
                    type="button"
                    role="radio"
                    aria-checked={settings.theme === value}
                    onClick={() => onTheme(value)}
                    className="flex h-7 items-center gap-1.5 rounded-lg px-3 text-[13px] font-medium text-muted-foreground transition-[background-color,color,box-shadow,scale] duration-150 ease-out hover:text-foreground motion-safe:active:scale-[0.97] aria-checked:bg-card aria-checked:text-foreground aria-checked:shadow-sm"
                  >
                    <Icon className="size-3.5" />
                    {label}
                  </button>
                ))}
              </div>
            </Row>
          </Section>

          <p className="mt-10 text-xs text-muted-foreground">HadalStream {version}</p>
        </div>
      </div>

      <footer className="flex shrink-0 items-center gap-2 border-t px-8 py-3">
        <span className="mr-auto text-xs text-muted-foreground">{dirty ? 'Unsaved changes' : 'All saved'}</span>
        <Button variant="ghost" disabled={!dirty || saving} onClick={() => setDraft(toForm(settings))}>
          Discard
        </Button>
        <Button disabled={!dirty || saving} onClick={save}>
          {saving && <Loader2Icon className="animate-spin" />}
          Save
        </Button>
      </footer>
    </div>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mt-8">
      <h2 className="mb-2 px-1 text-[11px] font-bold tracking-[0.08em] text-muted-foreground/80 uppercase">{title}</h2>
      <div className="divide-y rounded-2xl border bg-card shadow-xs">{children}</div>
    </section>
  )
}

function Row({
  label,
  hint,
  htmlFor,
  stacked,
  children,
}: {
  label: string
  hint?: string
  htmlFor?: string
  stacked?: boolean
  children: ReactNode
}) {
  return (
    <div className={cn('px-4 py-3.5', stacked ? 'space-y-2.5' : 'flex items-center justify-between gap-6')}>
      <div className="min-w-0">
        <label htmlFor={htmlFor} className="text-sm font-medium">
          {label}
        </label>
        {hint && <p className="mt-0.5 text-xs text-muted-foreground">{hint}</p>}
      </div>
      {children}
    </div>
  )
}

function Stepper({ label, value, min, max, onChange }: { label: string; value: number; min: number; max: number; onChange: (value: number) => void }) {
  const step =
    'grid h-full w-8 place-items-center text-muted-foreground transition-[color,scale] duration-150 ease-out hover:text-foreground motion-safe:active:scale-90 disabled:opacity-40'
  return (
    <div role="group" aria-label={label} className="inline-flex h-8 shrink-0 items-center rounded-lg border bg-background">
      <button type="button" aria-label="Decrease" disabled={value <= min} onClick={() => onChange(value - 1)} className={step}>
        <MinusIcon className="size-3.5" />
      </button>
      <span className="w-8 text-center text-sm font-medium tabular-nums" aria-live="polite">
        {value}
      </span>
      <button type="button" aria-label="Increase" disabled={value >= max} onClick={() => onChange(value + 1)} className={step}>
        <PlusIcon className="size-3.5" />
      </button>
    </div>
  )
}
