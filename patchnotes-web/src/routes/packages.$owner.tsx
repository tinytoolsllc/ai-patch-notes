import { createFileRoute, Outlet } from "@tanstack/react-router";

export const Route = createFileRoute("/packages/$owner")({
  component: function PackagesOwnerLayout() {
    return <Outlet />;
  },
});
