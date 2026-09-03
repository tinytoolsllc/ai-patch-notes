/* eslint-disable react-refresh/only-export-components */
import { createFileRoute, Link } from "@tanstack/react-router";
import { XCircle } from "lucide-react";
import { AppHeader, Container, Button, Card } from "../components/ui";

function SubscriptionCanceled() {
  return (
    <div className="min-h-screen bg-surface-secondary">
      <AppHeader />

      <main className="py-16">
        <Container>
          <Card className="max-w-lg mx-auto text-center py-12">
            <div className="w-16 h-16 mx-auto mb-6 rounded-full bg-amber-100 dark:bg-amber-900/30 flex items-center justify-center">
              <XCircle className="w-8 h-8 text-amber-600 dark:text-amber-400" />
            </div>

            <h1 className="text-2xl font-bold text-text-primary mb-3">Checkout Canceled</h1>

            <p className="text-text-secondary mb-8">
              No worries! Your subscription was not created. You can upgrade to Pro anytime when
              you're ready.
            </p>

            <div className="flex flex-col sm:flex-row gap-3 justify-center">
              <Link to="/">
                <Button variant="secondary">Back to Home</Button>
              </Link>
              <Link to="/pricing">
                <Button>View Pricing</Button>
              </Link>
            </div>
          </Card>
        </Container>
      </main>
    </div>
  );
}

export const Route = createFileRoute("/subscription-canceled")({
  component: SubscriptionCanceled,
});
