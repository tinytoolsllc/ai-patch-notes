import { renderHook, waitFor } from "../test/utils";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { http, HttpResponse } from "msw";
import { server } from "../test/mocks/server";
import { mockPackages, mockReleases, mockPackageReleases } from "../test/mocks/handlers";
import {
  usePackages,
  usePackage,
  useReleases,
  usePackageReleases,
  useDeletePackage,
  useUpdatePackage,
  useAddToWatchlist,
  useRemoveFromWatchlist,
  useAddFromGithub,
  useGithubSearch,
  useResetSummaries,
  useResetReleases,
  useRelease,
  usePackagesByOwner,
  usePackageByOwnerRepo,
} from "./hooks";

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: 0,
        staleTime: 0,
      },
      mutations: {
        retry: false,
      },
    },
  });

  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}

describe("usePackages", () => {
  it("fetches packages successfully", async () => {
    const { result } = renderHook(() => usePackages(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockPackages);
  });

  it("handles error when fetching fails", async () => {
    server.use(
      http.get("/api/packages", () => {
        return new HttpResponse(null, { status: 500 });
      }),
    );

    const { result } = renderHook(() => usePackages(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe("usePackage", () => {
  it("fetches a single package successfully", async () => {
    const { result } = renderHook(() => usePackage("pkg-react-test-id"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toMatchObject({
      id: "pkg-react-test-id",
      githubOwner: "facebook",
      githubRepo: "react",
    });
  });

  it("does not fetch when id is empty", async () => {
    const { result } = renderHook(() => usePackage(""), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
    expect(result.current.data).toBeUndefined();
  });

  it("handles 404 error", async () => {
    server.use(
      http.get("/api/packages/nonexistent", () => {
        return new HttpResponse(null, { status: 404 });
      }),
    );

    const { result } = renderHook(() => usePackage("nonexistent"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe("useReleases", () => {
  it("fetches releases successfully", async () => {
    const { result } = renderHook(() => useReleases(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(mockReleases);
  });
});

describe("usePackageReleases", () => {
  it("fetches releases for a specific package", async () => {
    const { result } = renderHook(() => usePackageReleases("pkg-react-test-id"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual([mockPackageReleases[0]]);
  });

  it("does not fetch when packageId is empty", async () => {
    const { result } = renderHook(() => usePackageReleases(""), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
  });
});

// These three hooks pass a path parameter straight into the URL, so an empty
// value would produce a request to a malformed path (`/api/releases/`). The
// generated client used to disable such a query on its own, but orval now
// only guards against null and undefined -- see the `enabled` options in
// hooks.ts. Nothing else stops these, so they are covered here directly.
describe("useRelease", () => {
  it("does not fetch when id is empty", () => {
    const { result } = renderHook(() => useRelease(""), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
  });
});

describe("usePackagesByOwner", () => {
  it("does not fetch when owner is empty", () => {
    const { result } = renderHook(() => usePackagesByOwner(""), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
  });
});

describe("usePackageByOwnerRepo", () => {
  it("does not fetch when owner is empty", () => {
    const { result } = renderHook(() => usePackageByOwnerRepo("", "react"), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
  });

  it("does not fetch when repo is empty", () => {
    const { result } = renderHook(() => usePackageByOwnerRepo("facebook", ""), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
  });
});

describe("useDeletePackage", () => {
  it("deletes a package successfully", async () => {
    const { result } = renderHook(() => useDeletePackage(), {
      wrapper: createWrapper(),
    });

    result.current.mutate("pkg-react-test-id");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });
});

describe("useUpdatePackage", () => {
  it("updates a package successfully", async () => {
    const { result } = renderHook(() => useUpdatePackage(), {
      wrapper: createWrapper(),
    });

    result.current.mutate({
      id: "pkg-react-test-id",
      githubOwner: "new-owner",
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toMatchObject({
      data: { githubOwner: "new-owner" },
    });
  });
});

describe("useAddToWatchlist", () => {
  it("adds a package to watchlist successfully", async () => {
    const { result } = renderHook(() => useAddToWatchlist(), {
      wrapper: createWrapper(),
    });

    result.current.mutate("pkg-react-test-id");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("handles conflict when already watching", async () => {
    server.use(
      http.post("/api/watchlist/:packageId", () => {
        return HttpResponse.json({ error: "Already watching" }, { status: 409 });
      }),
    );

    const { result } = renderHook(() => useAddToWatchlist(), {
      wrapper: createWrapper(),
    });

    result.current.mutate("pkg-react-test-id");

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe("useRemoveFromWatchlist", () => {
  it("removes a package from watchlist successfully", async () => {
    const { result } = renderHook(() => useRemoveFromWatchlist(), {
      wrapper: createWrapper(),
    });

    result.current.mutate("pkg-react-test-id");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });
});

describe("useAddFromGithub", () => {
  it("adds a package from GitHub successfully", async () => {
    const { result } = renderHook(() => useAddFromGithub(), {
      wrapper: createWrapper(),
    });

    result.current.mutate({ owner: "facebook", repo: "react" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toMatchObject({
      data: { packageId: "pkg-facebook-react-id" },
    });
  });
});

describe("useGithubSearch", () => {
  it("searches GitHub repos when query is long enough", async () => {
    const { result } = renderHook(() => useGithubSearch("react"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toMatchObject({
      data: expect.arrayContaining([expect.objectContaining({ owner: "facebook", repo: "react" })]),
    });
  });

  it("does not fetch when query is too short", () => {
    const { result } = renderHook(() => useGithubSearch("r"), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
    expect(result.current.data).toBeUndefined();
  });

  it("does not fetch when query is empty", () => {
    const { result } = renderHook(() => useGithubSearch(""), {
      wrapper: createWrapper(),
    });

    expect(result.current.isFetching).toBe(false);
    expect(result.current.data).toBeUndefined();
  });
});

describe("useResetSummaries", () => {
  it("resets summaries for a package successfully", async () => {
    const { result } = renderHook(() => useResetSummaries(), {
      wrapper: createWrapper(),
    });

    result.current.mutate("pkg-react-test-id");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("handles error when reset fails", async () => {
    server.use(
      http.post("/api/admin/packages/:id/reset-summaries", () => {
        return new HttpResponse(null, { status: 404 });
      }),
    );

    const { result } = renderHook(() => useResetSummaries(), {
      wrapper: createWrapper(),
    });

    result.current.mutate("nonexistent");

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe("useResetReleases", () => {
  it("resets releases for a package successfully", async () => {
    const { result } = renderHook(() => useResetReleases(), {
      wrapper: createWrapper(),
    });

    result.current.mutate("pkg-react-test-id");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("handles error when reset fails", async () => {
    server.use(
      http.post("/api/admin/packages/:id/reset-releases", () => {
        return new HttpResponse(null, { status: 404 });
      }),
    );

    const { result } = renderHook(() => useResetReleases(), {
      wrapper: createWrapper(),
    });

    result.current.mutate("nonexistent");

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
