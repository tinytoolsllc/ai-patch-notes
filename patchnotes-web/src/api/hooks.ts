import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useStytchUser } from '@stytch/react'
import * as z from 'zod'
import type { GetReleasesParams, WatchlistPackageDto } from './generated/model'

import {
  useGetPackages,
  useGetPackage,
  useGetPackageReleases,
  useGetPackagesByOwner,
  useGetPackageByOwnerRepo,
  getGetPackagesQueryKey,
  createPackage,
  deletePackage,
  updatePackage,
  bulkCreatePackages,
} from './generated/packages/packages'
import { useGetRelease, useGetReleases } from './generated/releases/releases'
import {
  useGetWatchlist,
  getGetWatchlistQueryKey,
  setWatchlist,
  addToWatchlist,
  removeFromWatchlist,
  addToWatchlistFromGitHub,
} from './generated/watchlist/watchlist'
import {
  searchGitHubRepositoriesUser,
  getSearchGitHubRepositoriesUserQueryKey,
} from './generated/git-hub-search/git-hub-search'
import {
  resetPackageSummaries,
  resetPackageReleases,
  useGetPackagesHealth,
  useResetPackageSync,
  useDisablePackageSync,
  getGetPackagesHealthQueryKey,
} from './generated/admin-packages/admin-packages'
import { useGetFeed } from './generated/feed/feed'
import { GetFeedResponse } from './generated/feed/feed.zod'

import {
  GetPackagesResponse,
  GetPackageResponse,
  GetPackageReleasesResponse,
  GetPackagesByOwnerResponse,
  GetPackageByOwnerRepoResponse,
} from './generated/packages/packages.zod'
import {
  GetReleaseResponse,
  GetReleasesResponse,
} from './generated/releases/releases.zod'
import { GetWatchlistResponse } from './generated/watchlist/watchlist.zod'

// ── Helpers ─────────────────────────────────────────────────

function validateResponse<T extends z.ZodType>(
  schema: T,
  data: unknown
): z.output<T> {
  const result = schema.safeParse(data)
  if (!result.success) {
    console.error('[Zod validation error]', z.prettifyError(result.error))
    throw result.error
  }
  return result.data
}

// ── Query Hooks ──────────────────────────────────────────────

export function usePackages() {
  return useGetPackages(undefined, {
    query: {
      select: (res) => validateResponse(GetPackagesResponse, res.data).items,
    },
  })
}

export function usePackage(id: string) {
  return useGetPackage(id, {
    query: {
      select: (res) => validateResponse(GetPackageResponse, res.data),
    },
  })
}

interface ReleasesOptions {
  packages?: string[]
  days?: number
  excludePrerelease?: boolean
  majorVersion?: number
}

export function useReleases(options?: ReleasesOptions) {
  const params: GetReleasesParams | undefined = options
    ? {
        packages: options.packages?.join(','),
        days: options.days,
        excludePrerelease: options.excludePrerelease,
        majorVersion: options.majorVersion,
      }
    : undefined

  return useGetReleases(params, {
    query: {
      select: (res) => validateResponse(GetReleasesResponse, res.data).items,
    },
  })
}

export function useRelease(id: string) {
  return useGetRelease(id, {
    query: {
      select: (res) => validateResponse(GetReleaseResponse, res.data),
    },
  })
}

export function usePackageReleases(packageId: string) {
  return useGetPackageReleases(packageId, undefined, {
    query: {
      select: (res) =>
        validateResponse(GetPackageReleasesResponse, res.data).items,
    },
  })
}

// ── Owner/Repo Query Hooks ──────────────────────────────────

export function usePackagesByOwner(owner: string) {
  return useGetPackagesByOwner(owner, undefined, {
    query: {
      select: (res) =>
        validateResponse(GetPackagesByOwnerResponse, res.data).items,
    },
  })
}

export function usePackageByOwnerRepo(owner: string, repo: string) {
  return useGetPackageByOwnerRepo(owner, repo, {
    query: {
      select: (res) =>
        validateResponse(GetPackageByOwnerRepoResponse, res.data),
    },
  })
}

// ── Mutation Hooks ───────────────────────────────────────────

export function useAddPackage() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (npmName: string) => createPackage({ npmName }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: getGetPackagesQueryKey() })
    },
  })
}

export function useDeletePackage() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => deletePackage(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: getGetPackagesQueryKey() })
    },
  })
}

export function useUpdatePackage() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      id,
      githubOwner,
      githubRepo,
      tagPrefix,
      name,
      npmName,
      url,
    }: {
      id: string
      githubOwner?: string
      githubRepo?: string
      tagPrefix?: string
      name?: string
      npmName?: string
      url?: string
    }) =>
      updatePackage(id, {
        githubOwner: githubOwner ?? null,
        githubRepo: githubRepo ?? null,
        tagPrefix: tagPrefix !== undefined ? tagPrefix : undefined,
        name: name ?? null,
        npmName: npmName ?? null,
        url: url ?? null,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: getGetPackagesQueryKey() })
    },
  })
}

export function useBulkAddPackages() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (
      items: {
        githubOwner: string
        githubRepo: string
        name?: string
        npmName?: string
        tagPrefix?: string
      }[]
    ) => bulkCreatePackages(items),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: getGetPackagesQueryKey() })
    },
  })
}

export function useResetSummaries() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => resetPackageSummaries(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: getGetPackagesQueryKey() })
    },
  })
}

export function useResetReleases() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => resetPackageReleases(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: getGetPackagesQueryKey() })
    },
  })
}

// ── Watchlist Hooks ──────────────────────────────────────────

export function useWatchlist() {
  const { user } = useStytchUser()
  return useGetWatchlist({
    query: {
      enabled: !!user,
      select: (res) => validateResponse(GetWatchlistResponse, res.data),
    },
  })
}

export function useSetWatchlist() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (packageIds: string[]) => setWatchlist({ packageIds }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: getGetWatchlistQueryKey() })
    },
  })
}

export function useAddToWatchlist() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (packageId: string) => addToWatchlist(packageId),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: getGetWatchlistQueryKey() })
      queryClient.invalidateQueries({ queryKey: ['/api/feed'] })
    },
  })
}

export function useRemoveFromWatchlist() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (packageId: string) => removeFromWatchlist(packageId),
    onMutate: async (packageId) => {
      await queryClient.cancelQueries({ queryKey: getGetWatchlistQueryKey() })
      const previous = queryClient.getQueryData(getGetWatchlistQueryKey())
      queryClient.setQueryData(
        getGetWatchlistQueryKey(),
        (old: { data: WatchlistPackageDto[] } | undefined) =>
          old
            ? { ...old, data: old.data.filter((pkg) => pkg.id !== packageId) }
            : old
      )
      return { previous }
    },
    onError: (_err, _packageId, context) => {
      if (context?.previous) {
        queryClient.setQueryData(getGetWatchlistQueryKey(), context.previous)
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: getGetWatchlistQueryKey() })
      queryClient.invalidateQueries({ queryKey: ['/api/feed'] })
    },
  })
}

export function useAddFromGithub() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ owner, repo }: { owner: string; repo: string }) =>
      addToWatchlistFromGitHub(owner, repo),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: getGetWatchlistQueryKey() })
      queryClient.invalidateQueries({ queryKey: getGetPackagesQueryKey() })
    },
  })
}

export function useGithubSearch(query: string) {
  return useQuery({
    queryKey: getSearchGitHubRepositoriesUserQueryKey({ q: query }),
    queryFn: ({ signal }) =>
      searchGitHubRepositoriesUser({ q: query }, { signal }),
    enabled: query.length >= 2,
    staleTime: 60_000,
  })
}

// ── Package Health Hooks ─────────────────────────────────────

export function usePackageHealth() {
  return useGetPackagesHealth()
}

export { useResetPackageSync, useDisablePackageSync, getGetPackagesHealthQueryKey }

// ── Re-exports (used directly by pages) ─────────────────────

export { useGetPackages } from './generated/packages/packages'
export { getGetPackageByOwnerRepoQueryOptions } from './generated/packages/packages'
export { getGetReleaseQueryOptions } from './generated/releases/releases'
export { useSearchGitHubRepositories } from './generated/admin-git-hub/admin-git-hub'
export {
  useUpdateEmailTemplate,
  getGetEmailTemplatesQueryKey,
} from './generated/email-templates/email-templates'
export {
  useGetCurrentUser,
  useUpdateCurrentUser,
  useGetEmailPreferences,
  useUpdateEmailPreferences,
  getGetEmailPreferencesQueryKey,
  getGetCurrentUserQueryKey,
} from './generated/users/users'

// ── Feed Hook ───────────────────────────────────────────────

export type {
  FeedResponseDto,
  FeedGroupDto,
  FeedReleaseDto,
} from './generated/model'

interface FeedOptions {
  excludePrerelease?: boolean
}

export function useFeed(options?: FeedOptions) {
  return useGetFeed(options, {
    query: {
      select: (res) => validateResponse(GetFeedResponse, res.data),
    },
  })
}
