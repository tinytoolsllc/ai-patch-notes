import { type InputHTMLAttributes, forwardRef } from "react";

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "type"> {
  label?: string;
  description?: string;
}

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(
  ({ label, description, className = "", id, ...props }, ref) => {
    const inputId = id || label?.toLowerCase().replace(/\s+/g, "-");

    return (
      <label
        htmlFor={inputId}
        className={`
          group flex items-start gap-3 cursor-pointer select-none
          ${props.disabled ? "opacity-50 cursor-not-allowed" : ""}
          ${className}
        `}
      >
        <div className="relative flex-shrink-0 mt-0.5">
          <input ref={ref} type="checkbox" id={inputId} className="peer sr-only" {...props} />
          <div
            className={`
              w-5 h-5 rounded-md border-2 transition-all duration-200 ease-out
              border-border-default bg-surface-primary
              peer-hover:border-brand-400
              peer-focus-visible:ring-2 peer-focus-visible:ring-brand-500 peer-focus-visible:ring-offset-2
              peer-checked:bg-brand-600 peer-checked:border-brand-600
              peer-checked:peer-hover:bg-brand-700 peer-checked:peer-hover:border-brand-700
              peer-disabled:opacity-50
            `}
          />
          <svg
            className={`
              absolute top-0.5 left-0.5 w-4 h-4 text-white pointer-events-none
              transition-all duration-200 ease-out
              opacity-0 scale-50
              peer-checked:opacity-100 peer-checked:scale-100
            `}
            viewBox="0 0 16 16"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <path d="M3.5 8.5L6.5 11.5L12.5 4.5" />
          </svg>
        </div>
        {(label || description) && (
          <div className="flex flex-col gap-0.5 min-w-0">
            {label && (
              <span className="text-sm font-medium text-text-primary leading-tight">{label}</span>
            )}
            {description && (
              <span className="text-xs text-text-tertiary leading-tight">{description}</span>
            )}
          </div>
        )}
      </label>
    );
  },
);

Checkbox.displayName = "Checkbox";
