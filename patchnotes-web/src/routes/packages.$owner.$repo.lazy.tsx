import { createLazyFileRoute } from "@tanstack/react-router";
import { PackageDetailByRepo } from "../pages/PackageDetailByRepo";

export const Route = createLazyFileRoute("/packages/$owner/$repo")({
  component: function PackageDetailByRepoWrapper() {
    const { owner, repo } = Route.useParams();
    return <PackageDetailByRepo owner={owner} repo={repo} />;
  },
});
