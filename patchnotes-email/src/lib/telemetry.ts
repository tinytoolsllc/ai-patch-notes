import * as appInsights from "applicationinsights";

const connectionString = process.env.APPLICATIONINSIGHTS_CONNECTION_STRING;

if (connectionString) {
  appInsights
    .setup(connectionString)
    .setAutoCollectRequests(true)
    .setAutoCollectDependencies(true)
    .setAutoCollectExceptions(true)
    .start();
}

const client = connectionString ? appInsights.defaultClient : null;

// Validate required env vars on cold start
const requiredVars = ["RESEND_API_KEY", "DATABASE_URL"];
const missingVars = requiredVars.filter((v) => !process.env[v]);

if (missingVars.length > 0) {
  console.error(`Missing required environment variables: ${missingVars.join(", ")}`);
}

// Emit cold start event
if (client) {
  client.trackEvent({
    name: "EmailFunctionStarted",
    properties: {
      configValid: (missingVars.length === 0).toString(),
      missingVars: missingVars.join(", "),
      appInsightsConfigured: "true",
    },
  });
}

export function trackEvent(name: string, properties?: Record<string, string>): void {
  client?.trackEvent({ name, properties });
}

/**
 * `String(value)` renders a plain object as "[object Object]", which throws away
 * the whole payload of a non-Error rejection -- exactly the detail an exception
 * report exists to carry. Prefer JSON, falling back to String for values JSON
 * cannot represent (circular structures, BigInt, symbols).
 */
function describeNonError(value: unknown): string {
  if (typeof value === "string") return value;
  if (value === null) return "null";
  if (value === undefined) return "undefined";
  if (
    typeof value === "number" ||
    typeof value === "boolean" ||
    typeof value === "bigint" ||
    typeof value === "symbol"
  ) {
    return value.toString();
  }
  try {
    // JSON.stringify returns undefined for a function or an unsupported value.
    return JSON.stringify(value) ?? Object.prototype.toString.call(value);
  } catch {
    // Circular structure, or a throwing toJSON.
    return Object.prototype.toString.call(value);
  }
}

export function trackException(error: unknown, properties?: Record<string, string>): void {
  const exception = error instanceof Error ? error : new Error(describeNonError(error));
  client?.trackException({ exception, properties });
}

export async function flush(): Promise<void> {
  await client?.flush();
}
