import { createFileRoute } from "@tanstack/react-router";
import { seoHead } from "../seo";

export const Route = createFileRoute("/about")({
  head: () => ({
    ...seoHead({
      title: "About | My Release Notes",
      description:
        "Learn about My Release Notes — the easiest way to track GitHub releases for the packages you depend on.",
      path: "/about",
    }),
  }),
});
