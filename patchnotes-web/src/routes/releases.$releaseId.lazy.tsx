import { createLazyFileRoute } from "@tanstack/react-router";
import { ReleaseDetail } from "../pages/ReleaseDetail";

export const Route = createLazyFileRoute("/releases/$releaseId")({
  component: function ReleaseDetailWrapper() {
    const { releaseId } = Route.useParams();
    return <ReleaseDetail releaseId={releaseId} />;
  },
});
