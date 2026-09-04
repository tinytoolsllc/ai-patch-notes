import { useStytchUser } from "@stytch/react";
import { Link, useNavigate } from "@tanstack/react-router";
import { Check, Sparkles } from "lucide-react";
import { AppHeader, Breadcrumb, Container, Button, Card } from "../components/ui";
import { useSubscriptionStore } from "../stores/subscriptionStore";
import { useGeofencing } from "../hooks/useGeofencing";

const FREE_FEATURES = [
  "Track up to 5 packages",
  "AI-powered release summaries",
  "Version grouping & filtering",
  "No advertisements",
];

const PRO_FEATURES = [
  "Everything in Free",
  "Track unlimited packages",
  "Weekly email digest",
  "Priority support",
];

export function Pricing() {
  const { user, isInitialized } = useStytchUser();
  const { isPro, isLoading, startCheckout, openPortal } = useSubscriptionStore();
  const { isAllowed: isGeofencingAllowed, isLoading: isGeofencingLoading } = useGeofencing();
  const navigate = useNavigate();

  const handleUpgrade = () => {
    if (!user) {
      void navigate({ to: "/login", search: { returnUrl: "/pricing" } });
      return;
    }
    startCheckout();
  };

  const handleManageSubscription = () => {
    openPortal();
  };

  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader breadcrumbs={<Breadcrumb>Pricing</Breadcrumb>} />

      <main className="py-12">
        <Container>
          <div className="text-center mb-12">
            <h1 className="text-3xl font-bold text-text-primary mb-4">
              Simple, transparent pricing
            </h1>
            <p className="text-lg text-text-secondary max-w-2xl mx-auto">
              Start free and upgrade when you need more. No hidden fees, cancel anytime.
            </p>
          </div>

          <div className="grid md:grid-cols-2 gap-8 max-w-4xl mx-auto">
            {/* Free Tier */}
            <Card className="relative flex flex-col">
              <div className="mb-6">
                <h2 className="text-xl font-semibold text-text-primary mb-2">Free</h2>
                <div className="flex items-baseline gap-1">
                  <span className="text-4xl font-bold text-text-primary">$0</span>
                  <span className="text-text-secondary">/forever</span>
                </div>
              </div>

              <ul className="space-y-3 mb-8">
                {FREE_FEATURES.map((feature) => (
                  <li key={feature} className="flex items-start gap-3">
                    <Check className="w-5 h-5 text-emerald-500 flex-shrink-0 mt-0.5" />
                    <span className="text-text-secondary">{feature}</span>
                  </li>
                ))}
              </ul>

              <div className="mt-auto">
                {!isInitialized ? (
                  <Button variant="secondary" size="lg" className="w-full" disabled>
                    Loading...
                  </Button>
                ) : !user ? (
                  <Link to="/login" search={{ returnUrl: "/pricing" }} className="block">
                    <Button variant="secondary" size="lg" className="w-full">
                      Get Started
                    </Button>
                  </Link>
                ) : (
                  <Button variant="secondary" size="lg" className="w-full" disabled>
                    Current Plan
                  </Button>
                )}
              </div>
            </Card>

            {/* Pro Tier */}
            <Card className="relative flex flex-col border-brand-500 dark:border-brand-400 border-2">
              <div className="absolute -top-3 left-1/2 -translate-x-1/2">
                <span className="inline-flex items-center gap-1.5 px-3 py-1 text-xs font-semibold bg-brand-500 text-white rounded-full">
                  <Sparkles className="w-3.5 h-3.5" />
                  Most Popular
                </span>
              </div>

              <div className="mb-6">
                <h2 className="text-xl font-semibold text-text-primary mb-2">Pro</h2>
                <div className="flex items-baseline gap-1">
                  <span className="text-4xl font-bold text-text-primary">$20</span>
                  <span className="text-text-secondary">/year</span>
                </div>
                <p className="text-sm text-text-tertiary mt-1">Less than $2/month</p>
              </div>

              <ul className="space-y-3 mb-8">
                {PRO_FEATURES.map((feature) => (
                  <li key={feature} className="flex items-start gap-3">
                    <Check className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
                    <span className="text-text-secondary">{feature}</span>
                  </li>
                ))}
              </ul>

              {isGeofencingAllowed === false && !isPro && (
                <p className="text-sm text-text-secondary mb-4">
                  Pro subscription is not available in your region. You can continue using the free
                  tier.
                </p>
              )}

              <div className="mt-auto">
                {!isInitialized || isGeofencingLoading ? (
                  <Button size="lg" className="w-full" disabled>
                    Loading...
                  </Button>
                ) : isPro ? (
                  <Button
                    variant="secondary"
                    size="lg"
                    className="w-full"
                    onClick={handleManageSubscription}
                    disabled={isLoading}
                  >
                    {isLoading ? "Loading..." : "Manage Subscription"}
                  </Button>
                ) : isGeofencingAllowed === false ? (
                  <Button size="lg" className="w-full" disabled>
                    Not Available in Your Region
                  </Button>
                ) : (
                  <Button size="lg" className="w-full" onClick={handleUpgrade} disabled={isLoading}>
                    {isLoading ? "Loading..." : "Upgrade to Pro"}
                  </Button>
                )}
              </div>
            </Card>
          </div>

          <div className="text-center mt-12 text-text-tertiary text-sm">
            <p>Secure payments powered by Stripe. Cancel anytime.</p>
          </div>
        </Container>
      </main>
    </div>
  );
}
