import { createLazyFileRoute } from "@tanstack/react-router";
import { Privacy } from "../pages/Privacy";

export const Route = createLazyFileRoute("/privacy")({
  component: Privacy,
});
