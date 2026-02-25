import { type HTMLAttributes, forwardRef } from 'react'
import { Container } from './Container'

type HeaderProps = HTMLAttributes<HTMLElement>

export const Header = forwardRef<HTMLElement, HeaderProps>(
  ({ className = '', children, ...props }, ref) => {
    return (
      <header
        ref={ref}
        className={`
          sticky top-0 z-10
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
