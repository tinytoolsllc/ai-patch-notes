import { createLazyFileRoute } from "@tanstack/react-router";
import { WatchlistPage } from "../pages/WatchlistPage";

export const Route = createLazyFileRoute("/watchlist")({
  component: WatchlistPage,
});
