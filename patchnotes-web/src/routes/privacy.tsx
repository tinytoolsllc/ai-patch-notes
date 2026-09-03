import { createFileRoute } from "@tanstack/react-router";
import { seoHead } from "../seo";

export const Route = createFileRoute("/privacy")({
  head: () => ({
    ...seoHead({
      title: "Privacy Policy | My Release Notes",
      description: "Read the My Release Notes privacy policy. Learn how we handle your data.",
      path: "/privacy",
    }),
  }),
});
