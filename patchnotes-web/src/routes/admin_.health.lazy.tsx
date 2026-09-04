import { createLazyFileRoute } from "@tanstack/react-router";
import { AdminHealth } from "../pages/AdminHealth";

export const Route = createLazyFileRoute("/admin_/health")({
  component: AdminHealth,
});
