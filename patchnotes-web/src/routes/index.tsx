import { createFileRoute } from "@tanstack/react-router";
import { getGetFeedQueryOptions } from "../api/hooks";
import { seoHead } from "../seo";

export const Route = createFileRoute("/")({
  loader: ({ context: { queryClient } }) =>
    queryClient.ensureQueryData(getGetFeedQueryOptions({ excludePrerelease: true })),
  head: () => ({
    ...seoHead({
      title: "My Release Notes - Track GitHub Releases | myreleasenotes.ai",
      description:
        "Track GitHub releases for the packages you depend on. AI-powered summaries, smart filtering, and instant notifications. Free to start.",
      path: "/",
    }),
  }),
});
