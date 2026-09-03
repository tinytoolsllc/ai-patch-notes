import { type InputHTMLAttributes, forwardRef } from "react";

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, className = "", id, ...props }, ref) => {
    const inputId = id || label?.toLowerCase().replace(/\s+/g, "-");

    return (
      <div className="w-full">
        {label && (
          <label htmlFor={inputId} className="block text-sm font-medium text-text-primary mb-1.5">
            {label}
          </label>
        )}
        <input
          ref={ref}
          id={inputId}
          className={`
            w-full px-3 py-2
            bg-surface-primary
            border border-border-default
            rounded-lg
            text-text-primary
            placeholder:text-text-tertiary
            transition-colors duration-150
            focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-transparent
            disabled:opacity-50 disabled:bg-surface-tertiary
            ${error ? "border-major ring-1 ring-major" : ""}
            ${className}
          `}
          {...props}
        />
        {error && <p className="mt-1.5 text-sm text-major">{error}</p>}
      </div>
    );
  },
);

Input.displayName = "Input";
