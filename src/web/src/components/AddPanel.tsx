import { useCallback, useEffect, useRef, useState } from 'react'
import { CheckIcon, CircleAlertIcon, CopyIcon, DownloadIcon, LinkIcon, XIcon } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Kbd, PageTitle } from '@/components/common'
import { AcornLoader, Pinwheel, Sky } from '@/components/scenery'
import { call, formatBytes, pickVariant, platformLabel, toastError, type Platform, type Quality, type ResolveResult } from '@/api'

interface Row extends ResolveResult {
  key: string
  quality: string
}

export function AddPanel({
  title,
  kanji,
  subtitle,
  defaultQuality,
}: {
  title: string
  kanji: string
  subtitle: string
  defaultQuality: Quality
}) {
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [rows, setRows] = useState<Row[]>([])
  const busyRef = useRef(false)
  const ready = rows.filter((r) => r.video && !r.error)

  const analyze = useCallback(
    async (text: string) => {
      if (!text.trim() || busyRef.current) return
      busyRef.current = true
      setBusy(true)
      setInput(text)
      try {
        const results = await call<ResolveResult[]>('resolve', { input: text })
        const fresh: Row[] = results.map((r) => ({
          ...r,
          key: r.video ? `${r.video.platform}:${r.video.id}` : `error:${r.input}`,
          quality: pickVariant(r.variants, defaultQuality)?.label ?? '',
        }))
        setRows((prev) => [...fresh, ...prev.filter((p) => !fresh.some((f) => f.key === p.key))])
        setInput('')
      } catch (e) {
        toastError(e)
      } finally {
        busyRef.current = false
        setBusy(false)
      }
    },
    [defaultQuality],
  )

  // Ctrl+V anywhere in the window analyzes the clipboard straight away.
  useEffect(() => {
    const onPaste = (e: ClipboardEvent) => {
      if ((e.target as HTMLElement).closest('input, textarea, [contenteditable="true"]')) return
      const text = e.clipboardData?.getData('text') ?? ''
      if (!text.trim()) return
      e.preventDefault()
      void analyze(text)
    }
    document.addEventListener('paste', onPaste)
    return () => document.removeEventListener('paste', onPaste)
  }, [analyze])

  async function download(selected: Row[]) {
    try {
      await call('jobs.add', selected.map((r) => ({ video: r.video, title: r.title ?? '', quality: r.quality })))
      setRows((prev) => prev.filter((r) => !selected.includes(r)))
      toast.success(selected.length > 1 ? `${selected.length} downloads started` : 'Download started')
    } catch (e) {
      toastError(e)
    }
  }

  return (
    <>
      <div className="sticky top-0 z-10 isolate">
        <Sky className="h-full" />
        <div className="mx-auto max-w-5xl px-8 pt-7 pb-6">
          <PageTitle title={title} kanji={kanji} aside={subtitle} />
          <div className="flex items-start gap-2 rounded-2xl border bg-card p-1.5 pl-3.5 shadow-xs transition-[border-color,box-shadow] duration-200 ease-out focus-within:border-ring/50 focus-within:ring-2 focus-within:ring-ring/10">
            <LinkIcon className="mt-2 size-4 shrink-0 text-muted-foreground" aria-hidden />
            <textarea
              rows={1}
              value={input}
              readOnly={busy}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
                  e.preventDefault()
                  void analyze(input)
                }
              }}
              onPaste={(e) => {
                if (input.trim()) return
                e.preventDefault()
                void analyze(e.clipboardData.getData('text'))
              }}
              placeholder="Paste links or Abyss IDs"
              aria-label="Video links"
              spellCheck={false}
              className="field-sizing-content max-h-40 min-h-8 flex-1 resize-none bg-transparent py-1.5 text-sm leading-5 outline-none placeholder:text-muted-foreground/80 read-only:opacity-60"
            />
            <Button onClick={() => analyze(input)} disabled={busy || !input.trim()} className={busy ? 'disabled:opacity-100' : undefined}>
              {busy && <Pinwheel className="size-4" />}
              {busy ? 'Analyzing' : 'Analyze'}
            </Button>
          </div>
          <p className="mt-2 px-1 text-xs text-muted-foreground">
            <Kbd>Enter</Kbd> analyze · <Kbd>Shift</Kbd>+<Kbd>Enter</Kbd> new line · <Kbd>Ctrl</Kbd>+<Kbd>V</Kbd> anywhere
          </p>
        </div>
      </div>

      <div className="mx-auto max-w-5xl px-8">
        {busy && (
          <div className="mb-6 flex items-center gap-3 rounded-2xl border border-dashed px-4 py-2.5 text-sm text-muted-foreground transition-opacity duration-200 ease-out starting:opacity-0">
            <AcornLoader className="h-9 w-6" />
            Looking for videos…
          </div>
        )}
        {rows.length > 0 && (
          <section className="mb-8 overflow-hidden rounded-2xl border bg-card shadow-xs" aria-label="Results">
            <header className="flex items-center gap-3 border-b px-4 py-2.5">
              <p className="text-sm font-medium">{ready.length > 0 ? `${ready.length} ready` : `${rows.length} failed`}</p>
              {ready.length > 0 && rows.length > ready.length && (
                <span className="text-xs text-destructive">{rows.length - ready.length} failed</span>
              )}
              <div className="ml-auto flex gap-1">
                <Button variant="ghost" size="sm" onClick={() => setRows([])}>
                  Clear
                </Button>
                {ready.length > 1 && (
                  <Button size="sm" onClick={() => download(ready)}>
                    <DownloadIcon /> Download all
                  </Button>
                )}
              </div>
            </header>
            <ul className="divide-y">
              {rows.map((row, index) => (
                <ResultRow
                  key={row.key}
                  row={row}
                  index={index}
                  onQuality={(quality) => setRows((prev) => prev.map((r) => (r.key === row.key ? { ...r, quality } : r)))}
                  onDownload={() => download([row])}
                  onDismiss={() => setRows((prev) => prev.filter((r) => r.key !== row.key))}
                />
              ))}
            </ul>
          </section>
        )}
      </div>
    </>
  )
}

function ResultRow({
  row,
  index,
  onQuality,
  onDownload,
  onDismiss,
}: {
  row: Row
  index: number
  onQuality: (quality: string) => void
  onDownload: () => void
  onDismiss: () => void
}) {
  const failed = !!row.error || !row.video
  return (
    <li
      style={{ transitionDelay: `${Math.min(index, 8) * 40}ms` }}
      className="flex items-center gap-3 px-4 py-3 transition-[opacity,translate] duration-200 ease-out starting:opacity-0 motion-safe:starting:translate-y-1"
    >
      {failed ? (
        <div className="grid size-9 shrink-0 place-items-center rounded-xl bg-destructive/10 text-destructive">
          <CircleAlertIcon className="size-4" />
        </div>
      ) : (
        <PlatformMark platform={row.video!.platform} />
      )}
      <div className="min-w-0 flex-1">
        <p className="selectable truncate text-sm font-medium" title={row.title ?? row.input}>
          {row.title || row.video?.id || row.input}
        </p>
        {row.video && (
          <div className="mt-0.5 flex items-center gap-1.5 text-xs text-muted-foreground">
            <span>{platformLabel[row.video.platform]}</span>
            <span aria-hidden>·</span>
            <CopyId id={row.video.id} />
          </div>
        )}
        {row.error && <p className="mt-0.5 truncate text-xs text-destructive" title={row.error}>{row.error}</p>}
      </div>
      {!failed && row.variants.length > 0 && (
        <>
          <Select value={row.quality} onValueChange={onQuality}>
            <SelectTrigger size="sm" className="w-44" aria-label="Quality">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {row.variants.map((v) => (
                <SelectItem key={v.label} value={v.label}>
                  {v.label}
                  <span className="text-muted-foreground">{formatBytes(v.size)}</span>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button size="sm" onClick={onDownload}>
            <DownloadIcon /> Download
          </Button>
        </>
      )}
      <Button variant="ghost" size="icon-sm" onClick={onDismiss} aria-label="Dismiss" className="text-muted-foreground">
        <XIcon />
      </Button>
    </li>
  )
}

function PlatformMark({ platform }: { platform: Platform }) {
  return platform === 'Abyss' ? (
    <div className="grid size-9 shrink-0 place-items-center rounded-xl bg-primary/12 text-[13px] font-bold text-primary">
      A
    </div>
  ) : (
    <div className="grid size-9 shrink-0 place-items-center rounded-xl bg-seal/12 text-[13px] font-bold text-seal">
      D
    </div>
  )
}

function CopyId({ id }: { id: string }) {
  const [copied, setCopied] = useState(false)
  return (
    <button
      type="button"
      title="Copy ID"
      onClick={async () => {
        await navigator.clipboard.writeText(id)
        setCopied(true)
        setTimeout(() => setCopied(false), 1500)
      }}
      className="group/id -mx-1 inline-flex items-center gap-1 rounded px-1 font-mono transition-colors duration-150 hover:bg-muted hover:text-foreground"
    >
      {id}
      {copied ? (
        <CheckIcon className="size-3 text-success" />
      ) : (
        <CopyIcon className="size-3 opacity-0 transition-opacity duration-150 group-hover/id:opacity-100" />
      )}
    </button>
  )
}
