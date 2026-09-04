interface LogoProps {
  size?: number;
  className?: string;
}

export function Logo({ size = 40, className = "" }: LogoProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 64 64"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      aria-hidden="true"
    >
      {/* Notepad body */}
      <rect
        x="10"
        y="8"
        width="44"
        height="52"
        rx="4"
        className="fill-brand-100 stroke-brand-600 dark:fill-brand-900 dark:stroke-brand-400"
        strokeWidth="2"
      />
      {/* Spiral binding dots */}
      <circle cx="18" cy="8" r="2" className="fill-brand-600 dark:fill-brand-400" />
      <circle cx="28" cy="8" r="2" className="fill-brand-600 dark:fill-brand-400" />
      <circle cx="38" cy="8" r="2" className="fill-brand-600 dark:fill-brand-400" />
      <circle cx="48" cy="8" r="2" className="fill-brand-600 dark:fill-brand-400" />
      {/* 3D Box - front face */}
      <path
        d="M24 30L32 26L40 30L40 40L32 44L24 40Z"
        className="fill-brand-200 stroke-brand-600 dark:fill-brand-800 dark:stroke-brand-400"
        strokeWidth="1.5"
        strokeLinejoin="round"
      />
      {/* 3D Box - top face */}
      <path
        d="M24 30L32 26L40 30L32 34Z"
        className="fill-brand-300 stroke-brand-600 dark:fill-brand-700 dark:stroke-brand-400"
        strokeWidth="1.5"
        strokeLinejoin="round"
      />
      {/* 3D Box - center line */}
      <line
        x1="32"
        y1="34"
        x2="32"
        y2="44"
        className="stroke-brand-600 dark:stroke-brand-400"
        strokeWidth="1.5"
      />
      {/* Lines representing text */}
      <line
        x1="18"
        y1="50"
        x2="32"
        y2="50"
        className="stroke-brand-400 dark:stroke-brand-500"
        strokeWidth="1.5"
        strokeLinecap="round"
      />
      <line
        x1="18"
        y1="54"
        x2="28"
        y2="54"
        className="stroke-brand-300 dark:stroke-brand-600"
        strokeWidth="1.5"
        strokeLinecap="round"
      />
    </svg>
  );
}
