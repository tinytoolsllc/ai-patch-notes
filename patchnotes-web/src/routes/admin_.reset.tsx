import { createFileRoute } from "@tanstack/react-router";
import { seoHead } from "../seo";

export const Route = createFileRoute("/admin_/reset")({
  head: () => ({
    ...seoHead({
      title: "Reset Data | Admin | My Release Notes",
      description: "Reset package summaries and releases.",
      path: "/admin/reset",
      noindex: true,
    }),
  }),
});
