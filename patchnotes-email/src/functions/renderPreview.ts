import { trackEvent, trackException, flush } from "../lib/telemetry.js";
import { app, HttpRequest, HttpResponseInit, InvocationContext } from "@azure/functions";
import { renderTemplate } from "../lib/templateRenderer.js";

interface PreviewRequest {
  jsxSource: string;
  props: Record<string, unknown>;
}

export async function renderPreview(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  const startedAt = Date.now();
  let body: PreviewRequest;
  try {
    body = (await request.json()) as PreviewRequest;
  } catch {
    return { status: 400, body: "Invalid JSON body" };
  }

  if (!body.jsxSource || typeof body.jsxSource !== "string") {
    return { status: 400, body: "Missing required field: jsxSource" };
  }

  try {
    const html = await renderTemplate(body.jsxSource, body.props ?? {});
    trackEvent("PreviewRendered", {
      durationMs: (Date.now() - startedAt).toString(),
    });
    await flush();
    return {
      status: 200,
      headers: { "Content-Type": "text/html; charset=utf-8" },
      body: html,
    };
  } catch (err) {
    context.error("Template render failed:", err);
    trackException(err, { operation: "renderPreview" });
    trackEvent("PreviewRenderFailed", {
      durationMs: (Date.now() - startedAt).toString(),
    });
    await flush();
    const message = err instanceof Error ? err.message : "Unknown render error";
    return { status: 422, body: `Template render failed: ${message}` };
  }
}

app.http("renderPreview", {
  methods: ["POST"],
  authLevel: "function",
  handler: renderPreview,
});
