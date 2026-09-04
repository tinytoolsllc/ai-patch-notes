const BASE_URL = "https://www.myreleasenotes.ai";
const OG_IMAGE = `${BASE_URL}/og-image.png`;

export function seoHead(opts: {
  title: string;
  description: string;
  path: string;
  noindex?: boolean;
  jsonLd?: Record<string, unknown>;
}) {
  const url = `${BASE_URL}${opts.path}`;

  const meta: Array<Record<string, unknown>> = [
    { title: opts.title },
    { name: "description", content: opts.description },
    { property: "og:type", content: "website" },
    { property: "og:url", content: url },
    { property: "og:title", content: opts.title },
    { property: "og:description", content: opts.description },
    { property: "og:image", content: OG_IMAGE },
    { name: "twitter:card", content: "summary_large_image" },
    { name: "twitter:title", content: opts.title },
    { name: "twitter:description", content: opts.description },
    { name: "twitter:image", content: OG_IMAGE },
  ];

  if (opts.noindex) {
    meta.push({ name: "robots", content: "noindex" });
  }

  if (opts.jsonLd) {
    meta.push({ "script:ld+json": opts.jsonLd });
  }

  const links = [{ rel: "canonical", href: url }];

  return { meta, links };
}
