import { toast } from 'sonner'

export type Platform = 'Abyss' | 'Dood'
export type JobStatus = 'Queued' | 'Downloading' | 'Paused' | 'Completed' | 'Failed' | 'Canceled'
export type Quality = 'high' | 'medium' | 'low'
export type Theme = 'dark' | 'light'

export interface VideoRef {
  platform: Platform
  id: string
  url: string
  referer: string | null
}

export interface Variant {
  label: string
  size: number | null
}

export interface ResolveResult {
  input: string
  video: VideoRef | null
  title: string | null
  variants: Variant[]
  error: string | null
}

export interface Job {
  id: string
  platform: Platform
  videoId: string
  title: string
  quality: string
  status: JobStatus
  error: string | null
  filePath: string | null
  totalBytes: number
  downloadedBytes: number
  speed: number
  createdAt: string
}

export interface Settings {
  downloadDir: string
  maxConcurrentJobs: number
  connections: number
  defaultQuality: Quality
  proxy: string | null
  headers: Record<string, string>
  theme: Theme
}

export type ConversionStatus = 'Queued' | 'Converting' | 'Completed' | 'Failed' | 'Canceled'

export interface Conversion {
  id: string
  fileName: string
  source: string
  output: string | null
  status: ConversionStatus
  error: string | null
  lossless: boolean | null
  details: string | null
  progress: number
  speed: number
  duration: number
}

interface HostMessage {
  id?: number | null
  result?: unknown
  error?: string
  event?: string
  data?: unknown
}

interface WebView {
  postMessage(message: unknown): void
  postMessageWithAdditionalObjects(message: unknown, objects: FileList): void
  addEventListener(type: 'message', listener: (e: MessageEvent<HostMessage>) => void): void
}

// Provided by WebView2 inside the desktop app; there is no HTTP server behind this UI.
const webview = (window as unknown as { chrome?: { webview?: WebView } }).chrome?.webview
const pending = new Map<number, { resolve: (value: unknown) => void; reject: (error: Error) => void }>()
const listeners = new Map<string, Set<(data: unknown) => void>>()
let nextId = 0

webview?.addEventListener('message', ({ data: message }) => {
  if (message.event) {
    listeners.get(message.event)?.forEach((listener) => listener(message.data))
    return
  }
  const request = message.id == null ? undefined : pending.get(message.id)
  if (!request) return
  pending.delete(message.id!)
  if (message.error) request.reject(new Error(message.error))
  else request.resolve(message.result)
})

export function call<T = void>(method: string, params?: unknown, files?: FileList): Promise<T> {
  if (!webview) return Promise.reject(new Error('Open this in the HadalStream app.'))
  const id = ++nextId
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (value: unknown) => void, reject })
    // Dropped files travel as WebView2 objects so the host gets their real paths.
    if (files) webview.postMessageWithAdditionalObjects({ id, method, params }, files)
    else webview.postMessage({ id, method, params })
  })
}

type HostEvent = 'job' | 'removed' | 'conversion' | 'conversionRemoved'

export function on<T>(event: HostEvent, listener: (data: T) => void) {
  const set = listeners.get(event) ?? new Set()
  listeners.set(event, set)
  set.add(listener as (data: unknown) => void)
  return () => void set.delete(listener as (data: unknown) => void)
}

export type View = 'all' | 'active' | 'completed' | 'failed'

export const views: Record<View, { title: string; kanji: string; match: (job: Job) => boolean }> = {
  all: { title: 'Downloads', kanji: '一覧', match: () => true },
  active: {
    title: 'In progress',
    kanji: '進行中',
    match: (j) => j.status === 'Queued' || j.status === 'Downloading' || j.status === 'Paused',
  },
  completed: { title: 'Completed', kanji: '完了', match: (j) => j.status === 'Completed' },
  failed: { title: 'Failed', kanji: '失敗', match: (j) => j.status === 'Failed' || j.status === 'Canceled' },
}

export const platformLabel: Record<Platform, string> = { Abyss: 'Abyss · HydraX', Dood: 'DoodStream' }

export function formatBytes(bytes: number | null | undefined) {
  if (bytes == null) return ''
  if (bytes <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)))
  return `${(bytes / 1024 ** i).toFixed(i >= 2 ? 1 : 0)} ${units[i]}`
}

export function formatDuration(seconds: number) {
  const s = Math.max(0, Math.round(seconds))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const pad = (n: number) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${pad(m)}:${pad(s % 60)}` : `${m}:${pad(s % 60)}`
}

// Same rule as the original tool: high = largest, low = smallest, medium = middle by size.
export function pickVariant(variants: Variant[], quality: Quality) {
  const sorted = [...variants].sort((a, b) => (a.size ?? 0) - (b.size ?? 0))
  if (quality === 'low') return sorted[0]
  if (quality === 'medium') return sorted[Math.floor((sorted.length - 1) / 2)]
  return sorted.at(-1)
}

export const toastError = (e: unknown) => toast.error((e as Error).message)
