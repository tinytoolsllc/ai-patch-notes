import { type HTMLAttributes, type ReactNode, forwardRef } from 'react'
import { Container } from './Container'

interface HeaderProps extends HTMLAttributes<HTMLElement> {
  breadcrumb?: ReactNode
}

export const Header = forwardRef<HTMLElement, HeaderProps>(
  ({ className = '', children, breadcrumb, ...props }, ref) => {
    return (
      <header
        ref={ref}
        className={`
          sticky top-0 z-30
          bg-surface-primary/80
          backdrop-blur-md
          border-b border-border-default
          ${className}
        `}
        {...props}
      >
        <Container>
          <div className="h-16 flex items-center justify-between">
            {children}
          </div>
          {breadcrumb && (
            <div className="pb-2 flex items-center gap-1.5 text-sm text-text-secondary">
              {breadcrumb}
            </div>
          )}
        </Container>
      </header>
    )
  }
)

Header.displayName = 'Header'

type HeaderTitleProps = HTMLAttributes<HTMLHeadingElement>

export const HeaderTitle = forwardRef<HTMLHeadingElement, HeaderTitleProps>(
  ({ className = '', children, ...props }, ref) => {
    return (
      <h1
        ref={ref}
        className={`text-xl font-semibold text-text-primary ${className}`}
        {...props}
      >
        {children}
      </h1>
    )
  }
)

HeaderTitle.displayName = 'HeaderTitle'
