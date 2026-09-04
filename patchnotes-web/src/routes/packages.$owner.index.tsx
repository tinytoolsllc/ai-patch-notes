import { createFileRoute } from "@tanstack/react-router";
import { OwnerPackagesPage } from "../pages/OwnerPackagesPage";
import { seoHead } from "../seo";

export const Route = createFileRoute("/packages/$owner/")({
  component: function OwnerPageWrapper() {
    const { owner } = Route.useParams();
    return <OwnerPackagesPage owner={owner} />;
  },
  head: ({ params }) => ({
    ...seoHead({
      title: `${params.owner} Packages | My Release Notes`,
      description: `Browse GitHub release notes for packages by ${params.owner}.`,
      path: `/packages/${params.owner}`,
    }),
  }),
});
