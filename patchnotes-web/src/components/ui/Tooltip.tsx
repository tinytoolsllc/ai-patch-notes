import { type ReactNode } from "react";

type TooltipProps = {
  label: string;
  children: ReactNode;
  position?: "top" | "bottom";
};

export function Tooltip({ label, children, position = "bottom" }: TooltipProps) {
  const positionClasses =
    position === "top"
      ? "bottom-full mb-2 after:top-full after:border-t-surface-inverse"
      : "top-full mt-2 after:bottom-full after:border-b-surface-inverse";

  return (
    <div className="relative group/tooltip">
      {children}
      <div
        role="tooltip"
        className={`
          absolute left-1/2 -translate-x-1/2 z-50
          px-2 py-1 rounded-md
          bg-surface-inverse text-text-inverse
          text-xs font-medium whitespace-nowrap
          opacity-0 pointer-events-none
          group-hover/tooltip:opacity-100
          transition-opacity duration-150
          after:absolute after:left-1/2 after:-translate-x-1/2
          after:border-4 after:border-transparent
          ${positionClasses}
        `}
      >
        {label}
      </div>
    </div>
  );
}
