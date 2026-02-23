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

export function trackException(error: unknown, properties?: Record<string, string>): void {
    const exception = error instanceof Error ? error : new Error(String(error));
    client?.trackException({ exception, properties });
}

export async function flush(): Promise<void> {
    await client?.flush();
}
