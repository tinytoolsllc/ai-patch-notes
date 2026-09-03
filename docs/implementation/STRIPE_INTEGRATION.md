# Stripe Integration

Stripe billing for PatchNotes Pro subscriptions.

---

## Overview

| Component           | Location                                         | Status                      |
| ------------------- | ------------------------------------------------ | --------------------------- |
| Subscription Routes | `PatchNotes.Api/Routes/SubscriptionRoutes.cs`    | Implemented                 |
| Webhook Handler     | `PatchNotes.Api/Webhooks/StripeWebhook.cs`       | Implemented                 |
| Subscription Store  | `patchnotes-web/src/stores/subscriptionStore.ts` | Implemented                 |
| Subscription API    | `patchnotes-web/src/api/subscription.ts`         | Implemented                 |
| Pricing Page        | `patchnotes-web/src/pages/Pricing.tsx`           | Implemented                 |
| User Model          | `PatchNotes.Data/User.cs`                        | Implemented (Stripe fields) |

### Stack

- **Frontend**: React 19, Zustand store, redirect to Stripe Hosted Checkout (no `@stripe/stripe-js` needed)
- **Backend**: .NET minimal API with `Stripe.net` SDK
- **Authentication**: Stytch Consumer (individual users, cookie-based sessions)
- **Database**: SQLite (dev) / SQL Server (prod) with EF Core
- **Webhook endpoint**: `POST /webhooks/stripe`

### Stripe Resources

| Resource | ID                               | Description        |
| -------- | -------------------------------- | ------------------ |
| Product  | `prod_TvrQE65x9wzata`            | PatchNotes Pro     |
| Price    | `price_1SxzhHGHBIpfuFar4mCKzqVW` | $20/year recurring |

### Tiers

**Free** ($0/forever): Track up to 5 packages, AI summaries, version grouping, dark mode.

**Pro** ($20/year): Unlimited packages, no ads, weekly email highlights, priority support.

---

## Endpoints

- `POST /api/subscription/checkout` — Creates Stripe Checkout session, redirects user
- `POST /api/subscription/portal` — Creates Stripe Customer Portal session
- `GET /api/subscription/status` — Returns `{ isPro, status, expiresAt }`

### Checkout Flow

1. Frontend calls `POST /api/subscription/checkout`
2. Backend creates a Stripe Hosted Checkout session with `stytch_user_id` in metadata
3. Backend returns the checkout URL
4. Frontend redirects via `window.location.href`
5. On success, Stripe redirects to `/subscription-success`
6. Webhook `checkout.session.completed` links Stripe customer to user

---

## Webhook Events

Registered at: `https://api-mypkgupdate-com.azurewebsites.net/webhooks/stripe`

| Event                           | Handler                                                 |
| ------------------------------- | ------------------------------------------------------- |
| `checkout.session.completed`    | Links Stripe customer to user, sets subscription status |
| `customer.subscription.updated` | Syncs status and expiry changes                         |
| `customer.subscription.deleted` | Marks as canceled, preserves access until period end    |
| `invoice.payment_failed`        | Marks user as `past_due`                                |
| `invoice.payment_succeeded`     | Updates expiry on successful renewal                    |

### Safeguards

- **Signature verification**: `EventUtility.ConstructEvent` with `throwOnApiVersionMismatch: true`
- **Idempotency**: `ProcessedWebhookEvent` table prevents duplicate processing
- **API version pinning**: `Program.cs` validates `StripeConfiguration.ApiVersion` at startup
- **Metadata filtering**: Events without `app: "patchnotes"` metadata are acknowledged but ignored

### Recommended Events to Add

**High priority:**

- `invoice.payment_action_required` — Handle 3D Secure / SCA challenges

**Medium priority:**

- `charge.dispute.created` / `charge.dispute.updated` — Handle payment disputes

**Low priority (if trials are added):**

- `customer.subscription.trial_will_end` — Notify users before trial expires

---

## Azure Configuration

App Service: `api-mypkgupdate-com` (resource group `MyPkgUpdate`)

| Setting                 | Source             | Description                            |
| ----------------------- | ------------------ | -------------------------------------- |
| `Stripe__SecretKey`     | Azure App Settings | Stripe secret API key                  |
| `Stripe__WebhookSecret` | Azure App Settings | Webhook signing secret                 |
| `Stripe:PriceId`        | `appsettings.json` | PatchNotes Pro price ID (not a secret) |

See [ENV_VARIABLES.md](./ENV_VARIABLES.md) for the full configuration reference.

---

## Local Development

### Stripe CLI for Webhook Testing

```bash
# Login (headless-friendly)
stripe login --interactive

# Forward webhooks to local dev server
stripe listen --forward-to localhost:5000/webhooks/stripe
```

The CLI will print a webhook signing secret (`whsec_...`) — use that as `Stripe:WebhookSecret` locally.

### Stripe MCP Server

The MCP server has its own auth (does not use the CLI's `config.toml`):

```bash
npx -y @stripe/mcp --tools=all --api-key=sk_test_...
```

Or via environment variable:

```bash
export STRIPE_SECRET_KEY=sk_test_...
npx -y @stripe/mcp --tools=all
```

For automation and agents, use [restricted API keys](https://dashboard.stripe.com/apikeys) with minimal permissions.

---

## Testing Checklist

- [ ] Test checkout flow with Stripe test card `4242 4242 4242 4242`
- [ ] Test webhook delivery with Stripe CLI
- [ ] Test subscription cancellation via Customer Portal
- [ ] Test payment failure with card `4000 0000 0000 0341`
- [ ] Test 3D Secure with card `4000 0027 6000 3184`
- [ ] Verify free tier enforces 5-package limit
- [ ] Verify Pro tier allows unlimited packages

## Dashboard Checklist

- [x] Product created: PatchNotes Pro (`prod_TvrQE65x9wzata`)
- [x] Price created: $20/year (`price_1SxzhHGHBIpfuFar4mCKzqVW`)
- [x] Webhook endpoint registered with correct events
- [x] Secret key configured on Azure
- [x] Webhook secret configured on Azure
- [ ] Enable Dynamic Payment Methods: Settings → Payment Methods → "Let Stripe decide"
- [ ] Configure Customer Portal products and prices for self-serve
- [ ] Configure automatic tax regions (if applicable)

---

## Future: Unified Billing

When Tiny Tools adds Stripe billing, both apps will share a single Stripe account. Key design decisions:

- **Shared Stripe Customer by email** — one customer per human across apps, unified Customer Portal
- **Webhook router (Azure Function)** — routes events by product ID to the correct app's internal endpoint
- **Separate databases** — each app tracks its own subscription state independently

For now, PatchNotes registers its webhook endpoint directly with Stripe. The router will be added when the second app needs billing.

---

## External References

- [Stripe Checkout Integration](https://docs.stripe.com/checkout/quickstart)
- [Subscription Lifecycle](https://docs.stripe.com/billing/subscriptions/overview)
- [Webhook Best Practices](https://docs.stripe.com/webhooks/best-practices)
- [Go-Live Checklist](https://docs.stripe.com/get-started/checklist/go-live)
