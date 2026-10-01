import { useEffect, useState, type ReactNode } from 'react'
import {
  AlertTriangleIcon,
  ArrowDownIcon,
  BanIcon,
  CheckIcon,
  Clock3Icon,
  FileTextIcon,
  FolderOpenIcon,
  Loader2Icon,
  PauseIcon,
  PlayIcon,
  RotateCwIcon,
  Trash2Icon,
  XIcon,
} from 'lucide-react'
import { cn } from 'cn'
import { Progress } from '@/components/ui/progress'
import { IconAction, Kbd, PaperBoat } from '@/components/common'
import { call, formatBytes, formatDuration, platformLabel, toastError, views, type Job, type View } from '@/api'

const seen = new Set<string>()

const empty: Record<View, { title: string; hint: ReactNode }> = {
  all: {
    title: 'Calm waters',
    hint: (
      <>
        Paste a link above or press <Kbd>Ctrl</Kbd>+<Kbd>V</Kbd>.
      </>
    ),
  },
  active: { title: 'Nothing running', hint: 'Active downloads show here.' },
  completed: { title: 'Nothing finished yet', hint: 'Double-click a video to play it.' },
  failed: { title: 'No failures', hint: 'Errors go to HadalStream.log.' },
}

export function JobList({ jobs, view }: { jobs: Job[]; view: View }) {
  // Jobs arrive in creation order; reversing is O(n) and avoids re-sorting on every progress tick.
  const visible = jobs.filter(views[view].match).reverse()

  if (visible.length === 0) {
    return (
      <div className="flex flex-col items-center rounded-2xl border border-dashed px-6 pt-8 pb-12 text-center">
        <PaperBoat className="h-28 w-52" />
        <p className="mt-3 text-sm font-bold">{empty[view].title}</p>
        <p className="mt-1 max-w-sm text-sm text-muted-foreground">{empty[view].hint}</p>
      </div>
    )
  }

  return (
    <ul className="divide-y overflow-hidden rounded-2xl border bg-card shadow-xs" aria-label={views[view].title}>
      {visible.map((job) => (
        <JobRow key={job.id} job={job} />
      ))}
    </ul>
  )
}

function JobRow({ job }: { job: Job }) {
  // Animate a row only the first time it appears; switching views happens often and should feel instant.
  const [isNew] = useState(() => !seen.has(job.id))
  useEffect(() => void seen.add(job.id), [job.id])
  const act = (method: string) => call(`jobs.${method}`, { id: job.id }).catch(toastError)
  const percent = job.totalBytes > 0 ? Math.min(100, (job.downloadedBytes / job.totalBytes) * 100) : 0
  const running = job.status === 'Downloading' || job.status === 'Queued'
  const showProgress = running || job.status === 'Paused'

  return (
    <li
      onDoubleClick={job.status === 'Completed' ? () => act('open') : undefined}
      title={job.status === 'Completed' ? job.filePath ?? undefined : undefined}
      className={cn(
        'flex items-center gap-4 px-4 py-3.5 transition-[background-color,opacity,translate] duration-200 ease-out hover:bg-muted/40',
        isNew && 'starting:opacity-0 motion-safe:starting:translate-y-1',
      )}
    >
      <StatusIcon job={job} />
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <p className="truncate text-sm font-medium">{job.title || job.videoId}</p>
          <span className="shrink-0 rounded-full border px-2 py-px text-[11px] font-medium text-muted-foreground">{job.quality}</span>
        </div>
        {showProgress && (
          <Progress
            value={percent}
            className={cn('mt-2', job.status === 'Paused' && '[&_[data-slot=progress-indicator]]:bg-warning')}
          />
        )}
        <p
          className={cn(
            'mt-1.5 truncate text-xs tabular-nums',
            job.status === 'Failed' ? 'text-destructive' : 'text-muted-foreground',
          )}
          title={job.status === 'Failed' ? job.error ?? undefined : undefined}
        >
          {detail(job)}
        </p>
      </div>
      {showProgress && job.totalBytes > 0 && (
        <span className="w-14 text-right text-sm font-medium tabular-nums">{percent.toFixed(percent < 10 ? 1 : 0)}%</span>
      )}
      <div className="flex items-center gap-0.5">
        {running && (
          <IconAction label="Pause" onClick={() => act('pause')}>
            <PauseIcon />
          </IconAction>
        )}
        {job.status === 'Paused' && (
          <IconAction label="Resume" onClick={() => act('resume')}>
            <PlayIcon />
          </IconAction>
        )}
        {(job.status === 'Failed' || job.status === 'Canceled') && (
          <IconAction label="Retry" onClick={() => act('resume')}>
            <RotateCwIcon />
          </IconAction>
        )}
        {job.status === 'Failed' && (
          <IconAction label="Error log" onClick={() => act('revealLog')}>
            <FileTextIcon />
          </IconAction>
        )}
        {job.status === 'Completed' && (
          <>
            <IconAction label="Play" onClick={() => act('open')}>
              <PlayIcon />
            </IconAction>
            <IconAction label="Show in folder" onClick={() => act('reveal')}>
              <FolderOpenIcon />
            </IconAction>
          </>
        )}
        {showProgress ? (
          <IconAction label="Cancel" onClick={() => act('cancel')} className="hover:text-destructive">
            <XIcon />
          </IconAction>
        ) : (
          <IconAction label="Remove" onClick={() => act('remove')} className="hover:text-destructive">
            <Trash2Icon />
          </IconAction>
        )}
      </div>
    </li>
  )
}

function StatusIcon({ job }: { job: Job }) {
  const [Icon, tint] = {
    Queued: [Clock3Icon, 'bg-muted text-muted-foreground'],
    Downloading: [job.totalBytes === 0 ? Loader2Icon : ArrowDownIcon, 'bg-primary/12 text-primary'],
    Paused: [PauseIcon, 'bg-warning/12 text-warning'],
    Completed: [CheckIcon, 'bg-success/12 text-success'],
    Failed: [AlertTriangleIcon, 'bg-destructive/12 text-destructive'],
    Canceled: [BanIcon, 'bg-muted text-muted-foreground'],
  }[job.status] as [typeof CheckIcon, string]

  return (
    <div className={cn('grid size-9 shrink-0 place-items-center rounded-xl transition-colors duration-200', tint)}>
      <Icon className={cn('size-4', Icon === Loader2Icon && 'animate-spin')} />
    </div>
  )
}

function detail(job: Job) {
  const progress = `${formatBytes(job.downloadedBytes)} of ${formatBytes(job.totalBytes)}`
  switch (job.status) {
    case 'Downloading': {
      if (job.totalBytes === 0) return 'Preparing…'
      const eta = job.speed > 0 ? ` · ${formatDuration((job.totalBytes - job.downloadedBytes) / job.speed)} left` : ''
      return `${progress} · ${formatBytes(job.speed)}/s${eta}`
    }
    case 'Queued':
      return 'In queue'
    case 'Paused':
      return job.totalBytes > 0 ? `Paused · ${progress}` : 'Paused'
    case 'Completed':
      return `${formatBytes(job.totalBytes)} · ${platformLabel[job.platform]}`
    case 'Failed':
      return job.error
    case 'Canceled':
      return 'Canceled'
  }
}
