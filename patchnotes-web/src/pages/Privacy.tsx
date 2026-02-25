import { AppHeader, Breadcrumb, Container } from '../components/ui'

const LAST_UPDATED = 'February 7, 2026'

export function Privacy() {
  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader breadcrumbs={<Breadcrumb>Privacy</Breadcrumb>} />

      <main className="py-12">
        <Container>
          <div className="max-w-3xl mx-auto prose prose-neutral dark:prose-invert">
            <p className="text-sm text-text-tertiary mb-8">
              Last updated: {LAST_UPDATED}
            </p>

            <p className="text-text-secondary leading-relaxed">
              Tiny Tools LLC (&quot;we&quot;, &quot;us&quot;, or
              &quot;our&quot;) is operated by Tiny Tools LLC. This policy
              describes how we collect, use, and protect your information when
              you use our service at{' '}
              <a
                href="https://app.myreleasenotes.ai"
                className="text-brand-500 hover:underline"
              >
                app.myreleasenotes.ai
              </a>
              .
            </p>

            <Section title="Information We Collect">
              <SubSection title="Account Data">
                <p className="text-text-secondary leading-relaxed">
                  When you create an account, we collect your email address and
                  optionally your name (via OAuth). We store a user ID for
                  authentication purposes.
                </p>
              </SubSection>

              <SubSection title="Usage Data">
                <p className="text-text-secondary leading-relaxed">
                  We store your package watchlist (which npm packages you choose
                  to track) and account timestamps (creation and last update).
                </p>
              </SubSection>

              <SubSection title="Payment Data">
                <p className="text-text-secondary leading-relaxed">
                  If you subscribe to Pro, Stripe processes your payment. We
                  store only your Stripe customer ID, subscription ID, and
                  subscription status. We never see or store your card number.
                </p>
              </SubSection>

              <SubSection title="Operational Data">
                <p className="text-text-secondary leading-relaxed">
                  We use Azure Application Insights for operational monitoring
                  and debugging. No advertising or third-party analytics
                  trackers are used.
                </p>
              </SubSection>
            </Section>

            <Section title="How We Use Your Information">
              <ul className="list-disc pl-6 space-y-2 text-text-secondary">
                <li>Authenticate you and manage your session</li>
                <li>Display release notes for packages you track</li>
                <li>Process Pro subscription payments</li>
                <li>
                  Generate AI-powered release summaries (using public release
                  text only)
                </li>
                <li>Monitor and debug service issues</li>
              </ul>
            </Section>

            <Section title="Third-Party Services">
              <div className="overflow-x-auto">
                <table className="w-full text-sm text-text-secondary">
                  <thead>
                    <tr className="border-b border-border-muted">
                      <th className="text-left py-3 pr-4 font-semibold text-text-primary">
                        Service
                      </th>
                      <th className="text-left py-3 pr-4 font-semibold text-text-primary">
                        Purpose
                      </th>
                      <th className="text-left py-3 font-semibold text-text-primary">
                        Data Shared
                      </th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border-muted">
                    <ServiceRow
                      service="Stytch"
                      purpose="Authentication (email magic links, OAuth)"
                      data="Email, name"
                    />
                    <ServiceRow
                      service="Stripe"
                      purpose="Payment processing"
                      data="Email, subscription data"
                    />
                    <ServiceRow
                      service="Ollama"
                      purpose="AI release summaries"
                      data="Public release text only (no user data)"
                    />
                    <ServiceRow
                      service="Microsoft Azure"
                      purpose="Hosting & monitoring"
                      data="Operational logs (no user data)"
                    />
                    <ServiceRow
                      service="GitHub API"
                      purpose="Fetching release notes"
                      data="No user data"
                    />
                    <ServiceRow
                      service="npm Registry"
                      purpose="Package metadata"
                      data="No user data"
                    />
                  </tbody>
                </table>
              </div>
              <p className="text-text-secondary leading-relaxed mt-4">
                We do not sell, rent, or share your data with advertisers or any
                other third parties.
              </p>
            </Section>

            <Section title="Cookies">
              <p className="text-text-secondary leading-relaxed">
                We use only functional session cookies (via Stytch) to keep you
                logged in. We do not use advertising cookies, tracking pixels,
                or third-party analytics cookies.
              </p>
            </Section>

            <Section title="Security">
              <ul className="list-disc pl-6 space-y-2 text-text-secondary">
                <li>HTTPS encryption on all connections</li>
                <li>Database encryption at rest (Azure SQL)</li>
                <li>Webhook signature verification (Stytch & Stripe)</li>
                <li>CORS restricted to specific origins</li>
                <li>No client-side storage of sensitive data</li>
              </ul>
            </Section>

            <Section title="Your Rights">
              <p className="text-text-secondary leading-relaxed">
                Under GDPR and CCPA, you have the right to:
              </p>
              <ul className="list-disc pl-6 space-y-2 text-text-secondary mt-2">
                <li>
                  <strong>Access</strong> &mdash; Request a copy of the data we
                  hold about you
                </li>
                <li>
                  <strong>Deletion</strong> &mdash; Request that we delete your
                  account and all associated data
                </li>
                <li>
                  <strong>Portability</strong> &mdash; Receive your data in a
                  portable format
                </li>
                <li>
                  <strong>Correction</strong> &mdash; Request correction of
                  inaccurate data
                </li>
              </ul>
              <p className="text-text-secondary leading-relaxed mt-4">
                We do not sell personal data, so no CCPA opt-out is required.
              </p>
            </Section>

            <Section title="Data Retention">
              <p className="text-text-secondary leading-relaxed">
                We retain your data for as long as your account is active. If
                you request deletion, we will remove your data within 30 days.
              </p>
            </Section>

            <Section title="Contact Us">
              <p className="text-text-secondary leading-relaxed">
                For privacy-related questions or to exercise your rights, email
                us at{' '}
                <a
                  href="mailto:privacy@yourtinytools.com"
                  className="text-brand-500 hover:underline"
                >
                  privacy@yourtinytools.com
                </a>
                .
              </p>
            </Section>
          </div>
        </Container>
      </main>
    </div>
  )
}

function Section({
  title,
  children,
}: {
  title: string
  children: React.ReactNode
}) {
  return (
    <section className="mt-10">
      <h2 className="text-xl font-semibold text-text-primary mb-4">{title}</h2>
      {children}
    </section>
  )
}

function SubSection({
  title,
  children,
}: {
  title: string
  children: React.ReactNode
}) {
  return (
    <div className="mt-4">
      <h3 className="text-base font-medium text-text-primary mb-2">{title}</h3>
      {children}
    </div>
  )
}

function ServiceRow({
  service,
  purpose,
  data,
}: {
  service: string
  purpose: string
  data: string
}) {
  return (
    <tr>
      <td className="py-3 pr-4 font-medium text-text-primary">{service}</td>
      <td className="py-3 pr-4">{purpose}</td>
      <td className="py-3">{data}</td>
    </tr>
  )
}
