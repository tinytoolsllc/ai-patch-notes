/* oxlint-disable react/only-export-components */
import { createRootRouteWithContext, HeadContent, Outlet } from "@tanstack/react-router";
import type { QueryClient } from "@tanstack/react-query";
import { Footer } from "../components/ui/Footer";
import { NavigationProgress } from "../components/ui/NavigationProgress";
import { seoHead } from "../seo";

function RootLayout() {
  return (
    <div className="min-h-screen flex flex-col">
      <HeadContent />
      <NavigationProgress />
      <Outlet />
      <Footer />
    </div>
  );
}

export const Route = createRootRouteWithContext<{ queryClient: QueryClient }>()({
  component: RootLayout,
  head: () => ({
    ...seoHead({
      title: "My Release Notes - Track GitHub Releases | myreleasenotes.ai",
      description:
        "Track GitHub releases for the packages you depend on. AI-powered summaries, smart filtering, and instant notifications. Free to start.",
      path: "/",
      jsonLd: {
        "@context": "https://schema.org",
        "@type": "WebApplication",
        name: "My Release Notes",
        url: "https://www.myreleasenotes.ai",
        description: "Track GitHub releases for the packages you depend on.",
        applicationCategory: "DeveloperApplication",
        operatingSystem: "Web",
        offers: {
          "@type": "Offer",
          price: "0",
          priceCurrency: "USD",
        },
      },
    }),
  }),
});
