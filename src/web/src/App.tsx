import { useEffect, useReducer, useRef, useState } from 'react'
import { toast } from 'sonner'
import { Toaster } from '@/components/ui/sonner'
import { AddPanel } from '@/components/AddPanel'
import { ConvertView } from '@/components/ConvertView'
import { JobList } from '@/components/JobList'
import { SettingsView } from '@/components/SettingsView'
import { Sidebar, type Page } from '@/components/Sidebar'
import { call, formatBytes, on, toastError, views, type Conversion, type Job, type Settings, type Theme, type View } from '@/api'

type ListAction<T> = { type: 'set'; items: T[] } | { type: 'upsert'; item: T } | { type: 'remove'; id: string }

// Items stay in creation order (the host lists them that way and new ones are appended), so views never sort.
function listReducer<T extends { id: string }>(state: T[], action: ListAction<T>): T[] {
  switch (action.type) {
    case 'set':
      return action.items
    case 'upsert': {
      const i = state.findIndex((x) => x.id === action.item.id)
      return i < 0 ? [...state, action.item] : state.with(i, action.item)
    }
    case 'remove':
      return state.filter((x) => x.id !== action.id)
  }
}

function summary(jobs: Job[], view: View) {
  const running = jobs.filter((j) => j.status === 'Downloading')
  if (view !== 'completed' && view !== 'failed' && running.length > 0)
    return `${running.length} downloading · ${formatBytes(running.reduce((s, j) => s + j.speed, 0))}/s`
  const count = jobs.filter(views[view].match).length
  return count === 0 ? '' : `${count} ${count === 1 ? 'item' : 'items'}`
}

// Subscribes to one host list ("job"/"removed" style events) and reports status transitions.
function useHostList<T extends { id: string; status: string }>(
  listMethod: string,
  upsertEvent: 'job' | 'conversion',
  removeEvent: 'removed' | 'conversionRemoved',
  onTransition: (item: T) => void,
) {
  const [items, dispatch] = useReducer(listReducer<T>, [])
  const statuses = useRef(new Map<string, string>())
  const transition = useRef(onTransition)
  useEffect(() => {
    transition.current = onTransition
  })

  useEffect(() => {
    const offUpsert = on<T>(upsertEvent, (item) => {
      const previous = statuses.current.get(item.id)
      if (previous && previous !== item.status) transition.current(item)
      statuses.current.set(item.id, item.status)
      dispatch({ type: 'upsert', item })
    })
    const offRemove = on<T>(removeEvent, (item) => {
      statuses.current.delete(item.id)
      dispatch({ type: 'remove', id: item.id })
    })
    call<T[]>(listMethod)
      .then((list) => {
        list.forEach((x) => statuses.current.set(x.id, x.status))
        dispatch({ type: 'set', items: list })
      })
      .catch(toastError)
    return () => {
      offUpsert()
      offRemove()
    }
  }, [listMethod, upsertEvent, removeEvent])

  return items
}

export default function App() {
  const [settings, setSettings] = useState<Settings | null>(null)
  const [version, setVersion] = useState('')
  const [page, setPage] = useState<Page>('all')
  const theme: Theme = settings?.theme ?? (document.documentElement.classList.contains('dark') ? 'dark' : 'light')

  const jobs = useHostList<Job>('jobs.list', 'job', 'removed', (job) => {
    const name = job.title || job.videoId
    if (job.status === 'Completed')
      toast.success('Downloaded', {
        description: name,
        action: { label: 'Show', onClick: () => call('jobs.reveal', { id: job.id }).catch(toastError) },
      })
    if (job.status === 'Failed') toast.error('Download failed', { description: `${name}: ${job.error}` })
  })

  const conversions = useHostList<Conversion>('convert.list', 'conversion', 'conversionRemoved', (item) => {
    if (item.status === 'Completed')
      toast.success('Converted', {
        description: item.fileName,
        action: { label: 'Show', onClick: () => call('convert.reveal', { id: item.id }).catch(toastError) },
      })
    if (item.status === 'Failed') toast.error('Conversion failed', { description: `${item.fileName}: ${item.error}` })
  })

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark')
  }, [theme])

  useEffect(() => {
    call<Settings>('settings.get').then(setSettings).catch(toastError)
    call<{ version: string }>('app.info').then((info) => setVersion(info.version)).catch(() => {})
    // A file dropped outside the converter's drop zone must not make the WebView try to open it.
    const block = (e: DragEvent) => e.preventDefault()
    window.addEventListener('dragover', block)
    window.addEventListener('drop', block)
    return () => {
      window.removeEventListener('dragover', block)
      window.removeEventListener('drop', block)
    }
  }, [])

  async function changeTheme(next: Theme) {
    if (!settings) return
    const previous = settings
    setSettings({ ...settings, theme: next })
    try {
      setSettings(await call<Settings>('settings.save', { ...settings, theme: next }))
    } catch (e) {
      setSettings(previous)
      toastError(e)
    }
  }

  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar current={page} onNavigate={setPage} jobs={jobs} conversions={conversions} theme={theme} onTheme={changeTheme} />
      <main className="my-2 mr-2 flex min-w-0 flex-1 flex-col overflow-hidden rounded-2xl border bg-background shadow-sm">
        {page === 'settings' ? (
          settings && <SettingsView settings={settings} version={version} onSaved={setSettings} onTheme={changeTheme} />
        ) : page === 'convert' ? (
          <ConvertView items={conversions} />
        ) : (
          <div className="min-h-0 flex-1 overflow-y-auto">
            <AddPanel
              title={views[page].title}
              kanji={views[page].kanji}
              subtitle={summary(jobs, page)}
              defaultQuality={settings?.defaultQuality ?? 'high'}
            />
            <div className="mx-auto max-w-5xl px-8 pb-10">
              <JobList jobs={jobs} view={page} />
            </div>
          </div>
        )}
      </main>
      <Toaster position="bottom-right" theme={theme} />
    </div>
  )
}
