import { type HTMLAttributes, forwardRef } from "react";

type BadgeVariant = "default" | "major" | "minor" | "patch" | "prerelease";

interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: BadgeVariant;
}

const variantStyles: Record<BadgeVariant, string> = {
  default: "bg-surface-tertiary text-text-secondary",
  major: "bg-major-muted text-major",
  minor: "bg-minor-muted text-minor",
  patch: "bg-patch-muted text-patch",
  prerelease: "bg-prerelease-muted text-prerelease",
};

export const Badge = forwardRef<HTMLSpanElement, BadgeProps>(
  ({ variant = "default", className = "", children, ...props }, ref) => {
    return (
      <span
        ref={ref}
        className={`
          inline-flex items-center
          px-2 py-0.5
          text-xs font-medium
          rounded-md
          ${variantStyles[variant]}
          ${className}
        `}
        {...props}
      >
        {children}
      </span>
    );
  },
);

Badge.displayName = "Badge";
