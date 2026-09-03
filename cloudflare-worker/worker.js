/**
 * Cloudflare Worker that routes SEO pages to the API for server-rendered HTML.
 *
 * Routes:
 *   /packages/* and /releases/* → API at /html/packages/* and /html/releases/*
 *   Everything else → passes through to the SWA (default origin)
 *
 * Authenticated users (with stytch_session cookie) bypass the API and go
 * directly to the SWA so they get the full interactive SPA experience.
 *
 * Worker Routes (configured in Cloudflare dashboard):
 *   www.myreleasenotes.ai/packages/*
 *   www.myreleasenotes.ai/releases/*
 */
export default {
  async fetch(request) {
    const url = new URL(request.url);

    if (url.pathname.startsWith("/packages/") || url.pathname.startsWith("/releases/")) {
      // Authenticated users get the SPA
      const cookie = request.headers.get("cookie") || "";
      if (cookie.includes("stytch_session")) {
        return fetch(request);
      }

      // Anonymous users and bots get server-rendered HTML from the API
      const apiUrl = "https://api.myreleasenotes.ai/html" + url.pathname + url.search;
      return fetch(apiUrl, { headers: request.headers });
    }

    // All other routes pass through to the default origin (SWA)
    return fetch(request);
  },
};
