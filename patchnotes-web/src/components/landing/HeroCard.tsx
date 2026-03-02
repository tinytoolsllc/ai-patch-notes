import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { useStytchUser } from '@stytch/react'
import {
  ChevronLeft,
  ChevronRight,
  X,
  Sparkles,
  Package,
  SlidersHorizontal,
  MonitorSmartphone,
  UserPlus,
  PackageSearch,
  Bell,
  Check,
  Zap,
  Shield,
  Clock,
} from 'lucide-react'
import { Card, Button } from '../ui'
import { useFilterStore } from '../../stores/filterStore'

const SLIDE_COUNT = 4

const PRICING_ROWS: { label: string; free: string; pro: string }[] = [
  { label: 'Packages', free: '5', pro: 'Unlimited' },
  { label: 'AI Summaries', free: '✓', pro: '✓' },
  { label: 'Grouping & Filtering', free: '✓', pro: '✓' },
  { label: 'No Ads', free: '✓', pro: '✓' },
  { label: 'Weekly Email', free: '✗', pro: '✓' },
]

const features = [
  {
    icon: Sparkles,
    title: 'AI Summaries',
    description:
      'AI condenses changelogs into clear, actionable insights you can read in seconds.',
  },
  {
    icon: Package,
    title: 'Track Any Package',
    description:
      'Watch any public GitHub repo. Get notified the moment new versions drop.',
  },
  {
    icon: SlidersHorizontal,
    title: 'Smart Filtering',
    description:
      'Group by package, sort by date or name, and hide pre-releases you don\u2019t need.',
  },
  {
    icon: MonitorSmartphone,
    title: 'Works Everywhere',
    description: 'Fully responsive. Check releases from any device, any time.',
  },
]

const steps = [
  {
    icon: UserPlus,
    title: 'Sign up free',
    description: 'Create an account with just your email.',
  },
  {
    icon: PackageSearch,
    title: 'Pick your packages',
    description: 'Select the GitHub packages you want to track.',
  },
  {
    icon: Bell,
    title: 'Stay updated',
    description: 'Get a personalized feed with AI summaries.',
  },
]

const highlights = [
  {
    icon: Zap,
    title: 'AI Summaries',
    description: 'Changelogs condensed into clear, actionable insights.',
  },
  {
    icon: Shield,
    title: 'Always Free',
    description: 'Track up to 5 packages at no cost, forever.',
  },
  {
    icon: Clock,
    title: 'Weekly Digest',
    description: 'A curated summary of every release, delivered weekly.',
  },
]

// ---------------------------------------------------------------------------
// Slide Bodies (content below the fixed header)
// ---------------------------------------------------------------------------

function HeroBodyMobile() {
  return (
    <div className="flex flex-col items-center gap-3">
      <div className="flex flex-col gap-1.5">
        {highlights.map((h) => (
          <span
            key={h.title}
            className="flex items-center gap-2 text-sm text-text-secondary"
          >
            <Check className="w-4 h-4 text-emerald-500 shrink-0" />
            {h.title} — {h.description}
          </span>
        ))}
      </div>
      <Link to="/login" search={{ returnUrl: undefined }}>
        <Button className="px-6 text-xs">Get Started Free</Button>
      </Link>
    </div>
  )
}

function HeroBodyWeb() {
  return (
    <div className="flex flex-col items-center">
      <div className="flex items-center justify-center gap-8">
        {highlights.map((h) => (
          <div key={h.title} className="flex flex-col items-center text-center">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-600 text-white">
              <h.icon className="h-4 w-4" strokeWidth={1.5} />
            </div>
            <div>
              <p className="text-sm font-medium text-text-primary">{h.title}</p>
              <p className="text-xs text-text-secondary max-w-[180px]">
                {h.description}
              </p>
            </div>
          </div>
        ))}
      </div>
      <Link to="/login" search={{ returnUrl: undefined }} className="mt-3">
        <Button className="px-6 text-xs">Get Started Free</Button>
      </Link>
    </div>
  )
}

function HeroBody() {
  return (
    <>
      <div className="sm:hidden">
        <HeroBodyMobile />
      </div>
      <div className="hidden sm:block">
        <HeroBodyWeb />
      </div>
    </>
  )
}

function FeaturesBodyMobile() {
  return (
    <div className="grid grid-cols-1 gap-2">
      {features.map((f) => (
        <div key={f.title} className="flex items-start gap-2">
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-brand-50 dark:bg-brand-950">
            <f.icon
              className="h-3.5 w-3.5 text-brand-600 dark:text-brand-400"
              strokeWidth={1.5}
            />
          </div>
          <div>
            <p className="text-xs font-medium text-text-primary">{f.title}</p>
            <p className="text-xs leading-tight text-text-secondary">
              {f.description}
            </p>
          </div>
        </div>
      ))}
    </div>
  )
}

function FeaturesBodyWeb() {
  return (
    <div className="grid grid-cols-2 gap-3 px-2">
      {features.map((f) => (
        <div key={f.title} className="flex items-start gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-brand-50 dark:bg-brand-950">
            <f.icon
              className="h-4 w-4 text-brand-600 dark:text-brand-400"
              strokeWidth={1.5}
            />
          </div>
          <div>
            <p className="text-sm font-medium text-text-primary">{f.title}</p>
            <p className="text-xs text-text-secondary">{f.description}</p>
          </div>
        </div>
      ))}
    </div>
  )
}

function FeaturesBody() {
  return (
    <>
      <div className="sm:hidden">
        <FeaturesBodyMobile />
      </div>
      <div className="hidden sm:block">
        <FeaturesBodyWeb />
      </div>
    </>
  )
}

function HowItWorksBodyMobile() {
  return (
    <div className="flex flex-col gap-4">
      {steps.map((step, i) => (
        <div key={step.title} className="flex items-start gap-2">
          <div className="relative flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-brand-600 text-white">
            <step.icon className="h-3.5 w-3.5" strokeWidth={1.5} />
            <span className="absolute -top-1 -right-1 flex h-3.5 w-3.5 items-center justify-center rounded-full bg-surface-primary border-2 border-brand-600 text-2xs font-bold text-brand-600">
              {i + 1}
            </span>
          </div>
          <div>
            <p className="text-xs font-medium text-text-primary">
              {step.title}
            </p>
            <p className="text-xs leading-tight text-text-secondary">
              {step.description}
            </p>
          </div>
        </div>
      ))}
    </div>
  )
}

function HowItWorksBodyWeb() {
  return (
    <div className="flex flex-col items-center">
      <div className="flex items-center justify-center gap-8">
        {steps.map((step, i) => (
          <div
            key={step.title}
            className="flex flex-col items-center text-center"
          >
            <div className="relative flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-600 text-white">
              <step.icon className="h-4 w-4" strokeWidth={1.5} />
              <span className="absolute -top-1 -right-1 flex h-4 w-4 items-center justify-center rounded-full bg-surface-primary border-2 border-brand-600 text-2xs font-bold text-brand-600">
                {i + 1}
              </span>
            </div>
            <div>
              <p className="text-sm font-medium text-text-primary">
                {step.title}
              </p>
              <p className="text-xs text-text-secondary max-w-[180px]">
                {step.description}
              </p>
            </div>
          </div>
        ))}
      </div>
      <Link to="/login" search={{ returnUrl: undefined }} className="mt-3">
        <Button variant="secondary" className="px-5 text-xs">
          Create your free account
        </Button>
      </Link>
    </div>
  )
}

function HowItWorksBody() {
  return (
    <>
      <div className="sm:hidden">
        <HowItWorksBodyMobile />
      </div>
      <div className="hidden sm:block">
        <HowItWorksBodyWeb />
      </div>
    </>
  )
}

function PricingBody() {
  return (
    <table className="w-full max-w-sm sm:max-w-md mx-auto text-xs sm:text-sm">
      <thead>
        <tr>
          <th className="text-left py-1 pr-2 text-text-tertiary font-normal" />
          <th className="py-1 px-2 text-center">
            <p className="font-semibold text-text-primary">Free</p>
            <p className="text-text-secondary font-normal text-xs">$0</p>
          </th>
          <th className="py-1 px-2 text-center">
            <p className="font-semibold text-brand-500">Pro</p>
            <p className="text-text-secondary font-normal text-xs">$20/yr</p>
          </th>
        </tr>
      </thead>
      <tbody>
        {PRICING_ROWS.map((row) => (
          <tr key={row.label} className="border-t border-border-default">
            <td className="py-1 pr-2 text-text-secondary">{row.label}</td>
            <td className="py-1 px-2 text-center">
              <span
                className={
                  row.free === '\u2713'
                    ? 'text-emerald-500'
                    : row.free === '\u2717'
                      ? 'text-text-tertiary'
                      : 'text-text-primary font-medium'
                }
              >
                {row.free}
              </span>
            </td>
            <td className="py-1 px-2 text-center">
              <span
                className={
                  row.pro === '\u2713'
                    ? 'text-emerald-500'
                    : 'text-text-primary font-medium'
                }
              >
                {row.pro}
              </span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

// ---------------------------------------------------------------------------
// Slide definitions (title/subtitle rendered at fixed position in the card)
// ---------------------------------------------------------------------------

const slides = [
  {
    title: 'Never miss a release that matters',
    subtitle: 'AI-powered summaries of every GitHub release.',
    Body: HeroBody,
  },
  {
    title: 'Everything you need to stay current',
    subtitle:
      'Stop scrolling through changelogs. Get the information you need.',
    Body: FeaturesBody,
  },
  {
    title: 'How it works',
    subtitle: 'Get started in under a minute — no setup required.',
    Body: HowItWorksBody,
  },
  {
    title: 'Simple pricing',
    subtitle: 'Start free and upgrade when you need more. Cancel anytime.',
    Body: PricingBody,
  },
]

// ---------------------------------------------------------------------------
// Main Component
// ---------------------------------------------------------------------------

export function HeroCard() {
  const { user } = useStytchUser()
  const { heroDismissed, dismissHero } = useFilterStore()
  const [index, setIndex] = useState(0)

  if (user || heroDismissed) return null

  const prev = () => setIndex((i) => (i - 1 + SLIDE_COUNT) % SLIDE_COUNT)
  const next = () => setIndex((i) => (i + 1) % SLIDE_COUNT)

  const slide = slides[index]

  return (
    <Card padding="none" className="relative mb-6 overflow-hidden">
      {/* Dismiss button */}
      <button
        onClick={dismissHero}
        className="absolute top-2 right-2 z-10 p-1 rounded-md text-text-tertiary hover:text-text-primary hover:bg-surface-tertiary transition-colors"
        aria-label="Dismiss hero"
      >
        <X className="w-4 h-4" />
      </button>

      {/* Prev / Next arrows */}
      <button
        onClick={prev}
        className="absolute left-1 top-1/2 -translate-y-1/2 z-10 p-1 rounded-full text-text-tertiary hover:text-text-primary hover:bg-surface-tertiary transition-colors"
        aria-label="Previous slide"
      >
        <ChevronLeft className="w-5 h-5" />
      </button>
      <button
        onClick={next}
        className="absolute right-1 top-1/2 -translate-y-1/2 z-10 p-1 rounded-full text-text-tertiary hover:text-text-primary hover:bg-surface-tertiary transition-colors"
        aria-label="Next slide"
      >
        <ChevronRight className="w-5 h-5" />
      </button>

      <div className="px-8 pt-3 pb-1">
        {/* Fixed-position header */}
        <div className="text-center mb-2 sm:mb-3 h-[44px] sm:h-auto flex flex-col justify-center">
          <h3 className="text-base font-semibold text-text-primary">
            {slide.title}
          </h3>
          <p className="text-xs text-text-tertiary mt-0.5">{slide.subtitle}</p>
        </div>

        {/* Slide body (fixed height so arrows don't shift) */}
        <div className="h-[190px] flex items-center justify-center">
          <slide.Body />
        </div>
      </div>

      {/* Dot navigation */}
      <div className="flex items-center justify-center gap-1.5 pb-2">
        {Array.from({ length: SLIDE_COUNT }, (_, i) => (
          <button
            key={i}
            onClick={() => setIndex(i)}
            aria-label={`Go to slide ${i + 1}`}
            className={`w-2 h-2 rounded-full transition-colors ${
              i === index
                ? 'bg-brand-600 dark:bg-brand-400'
                : 'bg-border-default hover:bg-text-tertiary'
            }`}
          />
        ))}
      </div>
    </Card>
  )
}
