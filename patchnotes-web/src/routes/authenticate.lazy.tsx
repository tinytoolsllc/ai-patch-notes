import { createLazyFileRoute } from "@tanstack/react-router";
import { Authenticate } from "../pages/Authenticate";

export const Route = createLazyFileRoute("/authenticate")({
  component: Authenticate,
});
