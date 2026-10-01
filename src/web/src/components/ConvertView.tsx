import { useState } from 'react'
import {
  AlertTriangleIcon,
  BanIcon,
  CheckIcon,
  Clock3Icon,
  FilmIcon,
  FolderOpenIcon,
  Loader2Icon,
  PlayIcon,
  RotateCwIcon,
  Trash2Icon,
  UploadIcon,
  XIcon,
} from 'lucide-react'
import { cn } from 'cn'
import { Button } from '@/components/ui/button'
import { Progress } from '@/components/ui/progress'
import { IconAction, PageTitle } from '@/components/common'
import { call, formatDuration, toastError, type Conversion } from '@/api'

export function ConvertView({ items }: { items: Conversion[] }) {
  const [dragging, setDragging] = useState(false)
  const active = items.find((i) => i.status === 'Converting')

  const drop = (files: FileList) => {
    if (files.length > 0) call('convert.drop', undefined, files).catch(toastError)
  }

  return (
    <div
      className="min-h-0 flex-1 overflow-y-auto"
      onDragOver={(e) => {
        if (!e.dataTransfer.types.includes('Files')) return
        e.preventDefault()
        setDragging(true)
      }}
      onDragLeave={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setDragging(false)
      }}
      onDrop={(e) => {
        e.preventDefault()
        setDragging(false)
        drop(e.dataTransfer.files)
      }}
    >
      <div className="mx-auto max-w-5xl px-8 pb-10">
        <div className="pt-7 pb-6">
          <PageTitle
            title="Convert to MP4"
            kanji="変換"
            aside={active ? `Converting · ${active.speed > 0 ? `${active.speed.toFixed(1)}×` : 'starting'}` : ''}
          />
          <div
            className={cn(
              'flex flex-col items-center rounded-2xl border border-dashed bg-card px-6 py-10 text-center transition-[border-color,background-color] duration-150',
              dragging && 'border-primary bg-primary/5',
            )}
          >
            <div className={cn('grid size-11 place-items-center rounded-xl bg-muted transition-colors duration-150', dragging && 'bg-primary/15 text-primary')}>
              <UploadIcon className="size-5" />
            </div>
            <p className="mt-4 text-sm font-bold">{dragging ? 'Release to convert' : 'Drop FLV, MOV or MKV here'}</p>
            <p className="mt-1 max-w-md text-sm text-muted-foreground">Lossless when possible. Saved next to the original.</p>
            <Button variant="outline" className="mt-5" onClick={() => call('convert.pick').catch(toastError)}>
              <FilmIcon /> Choose files
            </Button>
          </div>
        </div>

        {items.length > 0 && (
          <ul className="divide-y overflow-hidden rounded-2xl border bg-card shadow-xs" aria-label="Conversions">
            {[...items].reverse().map((item) => (
              <ConversionRow key={item.id} item={item} />
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}

function ConversionRow({ item }: { item: Conversion }) {
  const act = (method: string) => call(`convert.${method}`, { id: item.id }).catch(toastError)
  const busy = item.status === 'Queued' || item.status === 'Converting'
  const percent = item.progress * 100

  return (
    <li
      onDoubleClick={item.status === 'Completed' ? () => act('open') : undefined}
      title={item.output ?? item.source}
      className="flex items-center gap-4 px-4 py-3.5 transition-[background-color,opacity,translate] duration-200 ease-out hover:bg-muted/40 starting:opacity-0 motion-safe:starting:translate-y-1"
    >
      <StatusIcon status={item.status} />
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <p className="truncate text-sm font-medium">{item.fileName}</p>
          {item.lossless !== null && (
            <span
              className={cn(
                'shrink-0 rounded-full px-2 py-px text-[11px] font-medium',
                item.lossless ? 'bg-success/12 text-success' : 'bg-warning/12 text-warning',
              )}
            >
              {item.lossless ? 'Lossless' : 'Re-encoded'}
            </span>
          )}
        </div>
        {item.status === 'Converting' && <Progress value={percent} className="mt-2" />}
        <p
          className={cn('mt-1.5 truncate text-xs tabular-nums', item.status === 'Failed' ? 'text-destructive' : 'text-muted-foreground')}
          title={item.error ?? item.details ?? undefined}
        >
          {detail(item)}
        </p>
      </div>
      {item.status === 'Converting' && item.duration > 0 && (
        <span className="w-14 text-right text-sm font-medium tabular-nums">{percent.toFixed(percent < 10 ? 1 : 0)}%</span>
      )}
      <div className="flex items-center gap-0.5">
        {(item.status === 'Failed' || item.status === 'Canceled') && (
          <IconAction label="Retry" onClick={() => act('retry')}>
            <RotateCwIcon />
          </IconAction>
        )}
        {item.status === 'Completed' && (
          <>
            <IconAction label="Play" onClick={() => act('open')}>
              <PlayIcon />
            </IconAction>
            <IconAction label="Show in folder" onClick={() => act('reveal')}>
              <FolderOpenIcon />
            </IconAction>
          </>
        )}
        {busy ? (
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

function StatusIcon({ status }: { status: Conversion['status'] }) {
  const [Icon, tint] = {
    Queued: [Clock3Icon, 'bg-muted text-muted-foreground'],
    Converting: [Loader2Icon, 'bg-primary/12 text-primary'],
    Completed: [CheckIcon, 'bg-success/12 text-success'],
    Failed: [AlertTriangleIcon, 'bg-destructive/12 text-destructive'],
    Canceled: [BanIcon, 'bg-muted text-muted-foreground'],
  }[status] as [typeof CheckIcon, string]
  return (
    <div className={cn('grid size-9 shrink-0 place-items-center rounded-xl transition-colors duration-200', tint)}>
      <Icon className={cn('size-4', status === 'Converting' && 'animate-spin')} />
    </div>
  )
}

function detail(item: Conversion) {
  switch (item.status) {
    case 'Queued':
      return 'In queue'
    case 'Converting': {
      if (!item.details) return 'Reading streams…'
      const eta = item.speed > 0 && item.duration > 0 ? ` · ${formatDuration((item.duration * (1 - item.progress)) / item.speed)} left` : ''
      return `${item.details}${item.speed > 0 ? ` · ${item.speed.toFixed(1)}×` : ''}${eta}`
    }
    case 'Completed':
      return `Saved as ${item.output?.split('\\').pop()}`
    case 'Failed':
      return item.error
    case 'Canceled':
      return 'Canceled'
  }
}
