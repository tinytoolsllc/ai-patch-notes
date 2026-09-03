import { createFileRoute } from "@tanstack/react-router";
import { seoHead } from "../seo";

export const Route = createFileRoute("/onboarding")({
  head: () => ({
    ...seoHead({
      title: "Get Started | My Release Notes",
      description: "Choose a watchlist template to get started.",
      path: "/onboarding",
      noindex: true,
    }),
  }),
});
