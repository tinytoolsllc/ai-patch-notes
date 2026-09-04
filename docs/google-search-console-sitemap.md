# Google Search Console — Sitemap Setup

## Prerequisites

- Verified ownership of `www.myreleasenotes.ai` in [Google Search Console](https://search.google.com/search-console)
- The API deployed with the `/sitemap.xml` endpoint live at `api.myreleasenotes.ai`

## Steps

1. Go to [Google Search Console](https://search.google.com/search-console)
2. Select the **`www.myreleasenotes.ai`** property
3. In the left sidebar, navigate to **Indexing → Sitemaps**
4. In the "Add a new sitemap" field, enter:
   ```
   https://api.myreleasenotes.ai/sitemap.xml
   ```
5. Click **Submit**

Google will fetch the sitemap from the API domain. Even though the URLs in the sitemap point to `www.myreleasenotes.ai`, Google accepts cross-domain sitemaps as long as the sitemap is referenced in the `robots.txt` of the target domain — which it is:

```
# www.myreleasenotes.ai/robots.txt
Sitemap: https://api.myreleasenotes.ai/sitemap.xml
```

## Verification

After submitting, Google Search Console will show:

- **Status**: Success / Couldn't fetch / Has errors
- **Discovered URLs**: number of URLs found in the sitemap

If the status shows an error, check:

- `curl -I https://api.myreleasenotes.ai/sitemap.xml` returns `200` with `Content-Type: application/xml`
- The XML is valid (no malformed tags or encoding issues)
- CORS headers allow Google's crawler (the endpoint doesn't require auth, so this should work)

## Notes

- The sitemap is generated dynamically and cached for 1 hour (`Cache-Control: public, max-age=3600`)
- It includes the last 1000 releases by publish date — older releases rotate out as new ones are added
- Google re-fetches submitted sitemaps periodically on its own schedule
