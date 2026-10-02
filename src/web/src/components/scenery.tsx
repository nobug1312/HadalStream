import { useId, useState } from 'react'
import { cn } from 'cn'

const ink = 'fill-[#3b342b]'
const inkStroke = 'stroke-[#3b342b]'
const blush = 'fill-[#e8a08a]/80'

// Original mascot: a little acorn sprite, centred on (x, y) with its feet 13 units below.
function Acorn({ x, y, scale = 1 }: { x: number; y: number; scale?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${scale})`}>
      <ellipse cx="-3.6" cy="12.4" rx="2.6" ry="1.4" className="fill-acorn-cap" />
      <ellipse cx="3.6" cy="12.4" rx="2.6" ry="1.4" className="fill-acorn-cap" />
      <ellipse cy="3" rx="8.5" ry="9.5" className="fill-acorn" />
      <path d="M-9.6-2.2c0-6 4.3-9.3 9.6-9.3s9.6 3.3 9.6 9.3C6.8-.8 3.6-.2 0-.2s-6.8-.6-9.6-2z" className="fill-acorn-cap" />
      <path d="M0-11.4c.3-2 1.5-3.3 3.2-3.8" fill="none" strokeWidth="1.6" strokeLinecap="round" className="stroke-acorn-cap" />
      <g className="fill-acorn/50">
        <circle cx="-4.5" cy="-5.5" r=".8" />
        <circle cx="0" cy="-8" r=".8" />
        <circle cx="4.5" cy="-5.5" r=".8" />
      </g>
      <circle cx="-3" cy="3.4" r="1.15" className={ink} />
      <circle cx="3" cy="3.4" r="1.15" className={ink} />
      <ellipse cx="-5.4" cy="6" rx="1.7" ry="1" className={blush} />
      <ellipse cx="5.4" cy="6" rx="1.7" ry="1" className={blush} />
      <path d="M-1.3 6.2q1.3 1.1 2.6 0" fill="none" strokeWidth=".9" strokeLinecap="round" className={inkStroke} />
    </g>
  )
}

// Original orange tabby, sitting, with its feet 14 units below (x, y).
function Cat({ x, y, scale = 1 }: { x: number; y: number; scale?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${scale})`}>
      <g fill="none" strokeWidth="2.8" strokeLinecap="round" className="origin-bottom-left [transform-box:fill-box] motion-safe:animate-tail">
        <path d="M5.5 10q9 1 7.5-12" className="stroke-cat" />
        <path d="M13-2q.1-1.8-.4-3" className="stroke-cat-dark" />
      </g>
      <ellipse cy="6" rx="7" ry="7.6" className="fill-cat" />
      <ellipse cy="8" rx="3.8" ry="4.8" className="fill-cat-light" />
      <ellipse cx="-3" cy="12.8" rx="2.5" ry="1.4" className="fill-cat-light" />
      <ellipse cx="3" cy="12.8" rx="2.5" ry="1.4" className="fill-cat-light" />
      <path d="M-6.2-8-5-15.5-1.5-10.5zM6.2-8 5-15.5 1.5-10.5z" className="fill-cat" />
      <path d="M-4.6-10.4-4.3-13.4-2.6-11zM4.6-10.4 4.3-13.4 2.6-11z" className="fill-[#e8a08a]" />
      <circle cy="-5" r="6.4" className="fill-cat" />
      <path d="M-1.4-11v1.8M0-11.3v2.2M1.4-11v1.8" fill="none" strokeWidth=".8" strokeLinecap="round" className="stroke-cat-dark" />
      <path d="M-3.9-5.2q1-1.1 2 0M1.9-5.2q1-1.1 2 0" fill="none" strokeWidth=".9" strokeLinecap="round" className={inkStroke} />
      <path d="M-.7-3.5h1.4L0-2.7z" className="fill-[#d9776a]" />
      <path d="M-1.4-2.3q.7.8 1.4 0 .7.8 1.4 0" fill="none" strokeWidth=".7" strokeLinecap="round" className={inkStroke} />
      <ellipse cx="-4.4" cy="-3" rx="1.3" ry=".8" className={blush} />
      <ellipse cx="4.4" cy="-3" rx="1.3" ry=".8" className={blush} />
    </g>
  )
}

// Original forest owl, with its feet 10 units below (x, y).
function Owl({ x, y, scale = 1 }: { x: number; y: number; scale?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${scale})`}>
      <path d="M-6.4-5.5-5.2-11.5-2.2-7.6zM6.4-5.5 5.2-11.5 2.2-7.6z" className="fill-owl" />
      <ellipse rx="7" ry="8.6" className="fill-owl" />
      <ellipse cy="3" rx="4.2" ry="5" className="fill-owl-light" />
      <path d="M-6.6-.5q-1.4 5 2 8M6.6-.5q1.4 5-2 8" fill="none" strokeWidth="1.1" strokeLinecap="round" className="stroke-trunk/60" />
      <g className="origin-center [transform-box:fill-box] motion-safe:animate-blink">
        <circle cx="-2.8" cy="-3" r="2.7" className="fill-[#fffdf8]" />
        <circle cx="2.8" cy="-3" r="2.7" className="fill-[#fffdf8]" />
        <circle cx="-2.5" cy="-2.7" r="1.25" className={ink} />
        <circle cx="2.5" cy="-2.7" r="1.25" className={ink} />
      </g>
      <path d="M-1-.6h2L0 1.2z" className="fill-warning" />
      <path d="M-2.6 8.2v1.6M-1.4 8.2v1.6M1.4 8.2v1.6M2.6 8.2v1.6" fill="none" strokeWidth=".8" strokeLinecap="round" className="stroke-warning" />
    </g>
  )
}

// Original hearth spirit: a little flame resting in a clay lamp dish.
export function FlameSprite({ className }: { className?: string }) {
  const glow = useId()
  return (
    <svg aria-hidden viewBox="-13 -17 26 35" className={className}>
      <radialGradient id={glow}>
        <stop offset="0" stopColor="#ffc861" stopOpacity=".55" />
        <stop offset="1" stopColor="#ffc861" stopOpacity="0" />
      </radialGradient>
      <circle cy="1" r="13" fill={`url(#${glow})`} />
      <g className="origin-bottom [transform-box:fill-box] motion-safe:animate-flicker">
        <path d="M0-15C4-9 9-5 9 1.5S5 11 0 11s-9-3.5-9-9.5C-9-4-4-6 0-15z" className="fill-[#f2954a]" />
        <path d="M0-6c2.5 3 5 5.5 5 8.5S2.8 8 0 8s-5-2.5-5-5.5S-2 0 0-6z" className="fill-[#ffd36e]" />
        <circle cx="-2.4" cy="2.4" r="1.1" className={ink} />
        <circle cx="2.4" cy="2.4" r="1.1" className={ink} />
        <path d="M-1.4 4.8q1.4 1.2 2.8 0" fill="none" strokeWidth=".8" strokeLinecap="round" className={inkStroke} />
        <ellipse cx="-4" cy="4.2" rx="1.2" ry=".7" className="fill-[#f07a5a]/60" />
        <ellipse cx="4" cy="4.2" rx="1.2" ry=".7" className="fill-[#f07a5a]/60" />
      </g>
      <path d="M-10 11h20q-1.6 5.5-10 5.5T-10 11z" className="fill-[#b8714a]" />
      <path d="M-10 11h20" strokeWidth="1.4" strokeLinecap="round" className="stroke-[#9a5a38]" />
    </svg>
  )
}

// Cumulus cloud with a shaded underside, centred on (x, y).
function Cloud({ x, y, scale = 1 }: { x: number; y: number; scale?: number }) {
  const d = 'M-50 10a16 16 0 0 1 14-22 22 22 0 0 1 40-8 18 18 0 0 1 32 6 14 14 0 0 1 14 24z'
  return (
    <g transform={`translate(${x} ${y}) scale(${scale})`}>
      <path d={d} transform="translate(0 3)" className="fill-cloud-shade" />
      <path d={d} className="fill-cloud" />
    </g>
  )
}

function Moon({ x, y }: { x: number; y: number }) {
  return <path d={`M${x} ${y - 10}a10 10 0 0 0 0 20 12 12 0 0 1 0-20z`} className="fill-primary/85" />
}

function Sun({ x, y }: { x: number; y: number }) {
  return (
    <g className="dark:hidden">
      <circle cx={x} cy={y} r="17" className="fill-[#fff4d6]/70" />
      <circle cx={x} cy={y} r="9.5" className="fill-[#ffd27a]" />
    </g>
  )
}

function Grass({ x, y, delay }: { x: number; y: number; delay: number }) {
  return (
    <g transform={`translate(${x} ${y})`}>
      <path
        d="M0 0q-1-5-3.5-7M0 0q.5-6 1-9.5M0 0q1.5-4 4-6"
        fill="none"
        strokeWidth="1.3"
        strokeLinecap="round"
        style={{ animationDelay: `${delay}s` }}
        className="origin-bottom stroke-grass [transform-box:fill-box] motion-safe:animate-sway"
      />
    </g>
  )
}

const stars = [
  [70, 16],
  [150, 34],
  [228, 12],
  [318, 24],
  [384, 10],
  [450, 38],
  [590, 18],
]

// Sky band behind page headers. The parent needs `relative isolate`.
export function Sky({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn('pointer-events-none absolute inset-x-0 top-0 -z-10 overflow-hidden bg-linear-to-b from-sky to-background', className)}
    >
      <svg viewBox="0 0 640 120" preserveAspectRatio="xMaxYMin meet" className="absolute inset-0 size-full">
        <Sun x={430} y={24} />
        <path d="M318 30q3-2.5 6 0 3-2.5 6 0M338 22q2.2-2 4.4 0 2.2-2 4.4 0" fill="none" strokeWidth="1.1" strokeLinecap="round" className="stroke-foreground/35 dark:hidden" />
        <g className="hidden dark:inline">
          <Moon x={430} y={24} />
          {stars.map(([cx, cy]) => (
            <circle key={cx} cx={cx} cy={cy} r=".9" className="fill-foreground/60" />
          ))}
        </g>
        <g className="motion-safe:animate-drift [animation-delay:-6s]">
          <Cloud x={255} y={18} scale={0.4} />
        </g>
        <g className="motion-safe:animate-drift [animation-delay:-14s]">
          <Cloud x={405} y={44} scale={0.55} />
        </g>
        <g className="motion-safe:animate-drift">
          <Cloud x={575} y={48} />
        </g>
      </svg>
    </div>
  )
}

// Sidebar footer, edge to edge: hills, a camphor tree with an owl, and a cat. Click to say hi.
// The parent is `relative`; the bottom 40 units are open ground for the settings row.
export function Meadow() {
  const [pets, setPets] = useState(0)
  return (
    <button
      type="button"
      aria-label="Pet the cat"
      onClick={() => setPets((p) => p + 1)}
      className="absolute inset-0 rounded-[inherit] outline-none focus-visible:ring-2 focus-visible:ring-ring/40 focus-visible:ring-inset"
    >
      <svg aria-hidden viewBox="0 0 240 128" preserveAspectRatio="xMidYMax slice" className="size-full">
        <path d="M0 68C48 54 104 52 156 60s64 8 84 2v66H0z" className="fill-hill-far" />
        <path d="M43 86c1-11 1-21-1-29h6c-2 8-3 18-1 29z" className="fill-trunk" />
        <g className="fill-leaf">
          <circle cx="34" cy="46" r="11" />
          <circle cx="47" cy="38" r="14" />
          <circle cx="60" cy="47" r="10" />
          <circle cx="46" cy="52" r="11" />
          <circle cx="31" cy="55" r="6" />
        </g>
        <g className="fill-leaf-light">
          <circle cx="42" cy="32" r="6" />
          <circle cx="56" cy="40" r="4.5" />
          <circle cx="31" cy="42" r="4" />
        </g>
        <path d="M50 60q7-1 12-5" fill="none" strokeWidth="1.8" strokeLinecap="round" className="stroke-trunk" />
        <Owl x={60} y={50} scale={0.58} />
        <path d="M0 86C56 74 118 74 168 82s56 8 72 4v42H0z" className="fill-hill-near" />
        <g key={pets} className={pets ? 'motion-safe:animate-jump' : undefined}>
          <Cat x={178} y={70} scale={0.85} />
        </g>
        {pets > 0 && (
          <path
            key={`heart-${pets}`}
            d="M178 52c-4-3.5-5.5-5.5-5.5-7.5a2.8 2.8 0 0 1 5.5-1 2.8 2.8 0 0 1 5.5 1c0 2-1.5 4-5.5 7.5z"
            className="origin-bottom fill-seal [transform-box:fill-box] motion-safe:animate-heart"
          />
        )}
        <Grass x={98} y={77} delay={-1.3} />
        <Grass x={128} y={78} delay={-2.6} />
        <Grass x={222} y={88} delay={-3.9} />
        <Fireflies points={[[96, 56, 0], [140, 62, 0.7], [214, 66, 1.4]]} />
      </svg>
    </button>
  )
}

function Fireflies({ points }: { points: number[][] }) {
  return (
    <g className="hidden dark:inline">
      {points.map(([cx, cy, delay]) => (
        <g key={cx} className="animate-pulse" style={{ animationDelay: `${delay}s` }}>
          <circle cx={cx} cy={cy} r="3" className="fill-firefly/20" />
          <circle cx={cx} cy={cy} r="1.2" className="fill-firefly" />
        </g>
      ))}
    </g>
  )
}

// Loading: the acorn sprite hopping in place.
export function AcornLoader({ className }: { className?: string }) {
  return (
    <svg aria-hidden viewBox="-12 -20 24 37" className={className}>
      <ellipse cy="14.5" rx="7" ry="1.5" className="fill-foreground/10" />
      <g className="motion-safe:animate-hop">
        <Acorn x={0} y={0} />
      </g>
    </svg>
  )
}

// Kazaguruma (paper pinwheel): the compact loading indicator.
export function Pinwheel({ className }: { className?: string }) {
  return (
    <svg aria-hidden viewBox="0 0 24 24" className={cn('shrink-0 animate-spin', className)}>
      {['fill-seal', 'fill-warning', 'fill-success', 'fill-sky-deep'].map((petal, i) => (
        <path key={petal} d="M12 12V2.6a8.4 8.4 0 0 1 8.2 5.6z" transform={`rotate(${i * 90} 12 12)`} className={petal} />
      ))}
      <circle cx="12" cy="12" r="1.8" className="fill-card" />
    </svg>
  )
}
