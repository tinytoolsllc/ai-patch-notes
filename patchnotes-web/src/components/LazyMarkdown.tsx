import { lazy, Suspense } from "react";
import type { Options } from "react-markdown";

const MarkdownComponent = lazy(() => import("react-markdown"));

export function LazyMarkdown(props: Options) {
  return (
    <Suspense fallback={null}>
      <MarkdownComponent {...props} />
    </Suspense>
  );
}
