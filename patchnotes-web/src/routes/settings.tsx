import { createFileRoute } from "@tanstack/react-router";
import { seoHead } from "../seo";

export const Route = createFileRoute("/settings")({
  head: () => ({
    ...seoHead({
      title: "Settings | My Release Notes",
      description: "Manage your My Release Notes account settings.",
      path: "/settings",
      noindex: true,
    }),
  }),
});
