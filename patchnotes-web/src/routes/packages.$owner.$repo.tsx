import { createFileRoute } from "@tanstack/react-router";
import { seoHead } from "../seo";
import { getGetPackageByOwnerRepoQueryOptions } from "../api/hooks";

export const Route = createFileRoute("/packages/$owner/$repo")({
  loader: ({ context, params }) =>
    context.queryClient.ensureQueryData(
      getGetPackageByOwnerRepoQueryOptions(params.owner, params.repo),
    ),
  head: ({ params }) => ({
    ...seoHead({
      title: `${params.owner}/${params.repo} | My Release Notes`,
      description: `Track GitHub releases for ${params.owner}/${params.repo}. AI-powered summaries and notifications.`,
      path: `/packages/${params.owner}/${params.repo}`,
      jsonLd: {
        "@context": "https://schema.org",
        "@type": "SoftwareSourceCode",
        name: params.repo,
        codeRepository: `https://github.com/${params.owner}/${params.repo}`,
        author: { "@type": "Organization", name: params.owner },
        description: `Release notes for ${params.owner}/${params.repo}`,
      },
    }),
  }),
});
