import { http, HttpResponse } from 'msw'
import type {
  PackageDto,
  PackageDetailDto,
  ReleaseDto,
  GitHubRepoSearchResultDto,
  WatchlistPackageDto,
} from '../../api/generated/model'
import type { FeedResponseDto, FeedGroupDto } from '../../api/hooks'

const API_BASE = '/api'

export const mockWatchlist: WatchlistPackageDto[] = []

export const mockPackages: PackageDto[] = [
  {
    id: 'pkg-react-test-id',
    name: 'react',
    npmName: 'react',
    githubOwner: 'facebook',
    githubRepo: 'react',
    lastFetchedAt: '2026-01-15T10:00:00Z',
    createdAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'pkg-lodash-test-id',
    name: 'lodash',
    npmName: 'lodash',
    githubOwner: 'lodash',
    githubRepo: 'lodash',
    lastFetchedAt: '2026-01-14T10:00:00Z',
    createdAt: '2026-01-02T00:00:00Z',
  },
]

export const mockPackageDetails: PackageDetailDto[] = mockPackages.map(
  (pkg) => ({ ...pkg, releaseCount: 0 })
)

export const mockReleases: ReleaseDto[] = [
  {
    id: 'rel-react-19-test-id',
    tag: 'v19.0.0',
    title: 'React 19',
    body: 'Major release with new features',
    publishedAt: '2026-01-10T00:00:00Z',
    fetchedAt: '2026-01-15T10:00:00Z',
    package: {
      id: 'pkg-react-test-id',
      npmName: 'react',
      githubOwner: 'facebook',
      githubRepo: 'react',
    },
  },
  {
    id: 'rel-lodash-418-test-id',
    tag: 'v4.18.0',
    title: 'Lodash 4.18.0',
    body: 'Bug fixes and improvements',
    publishedAt: '2026-01-08T00:00:00Z',
    fetchedAt: '2026-01-14T10:00:00Z',
    package: {
      id: 'pkg-lodash-test-id',
      npmName: 'lodash',
      githubOwner: 'lodash',
      githubRepo: 'lodash',
    },
  },
]

// Package releases include extra fields in their package object (PackageReleasePackageDto)
export const mockPackageReleases = mockReleases.map((r) => ({
  ...r,
  package: {
    ...r.package,
    name:
      r.package.npmName ?? `${r.package.githubOwner}/${r.package.githubRepo}`,
  },
}))

export const mockFeedGroups: FeedGroupDto[] = [
  {
    packageId: 'pkg-react-test-id',
    packageName: 'react',
    npmName: 'react',
    githubOwner: 'facebook',
    githubRepo: 'react',
    majorVersion: 19,
    isPrerelease: false,
    versionRange: 'v19.x',
    summary:
      '## TL;DR\nReact 19 introduces a new compiler for automatic memoization.\n\n## Breaking\n- Removed legacy context API\n\n## New\n- React Compiler for automatic optimizations',
    releaseCount: 1,
    lastUpdated: '2026-01-10T00:00:00Z',
    releases: [
      {
        id: 'rel-react-19-test-id',
        tag: 'v19.0.0',
        title: 'React 19',
        publishedAt: '2026-01-10T00:00:00Z',
      },
    ],
  },
  {
    packageId: 'pkg-lodash-test-id',
    packageName: 'lodash',
    npmName: 'lodash',
    githubOwner: 'lodash',
    githubRepo: 'lodash',
    majorVersion: 4,
    isPrerelease: false,
    versionRange: 'v4.x',
    summary: null,
    releaseCount: 1,
    lastUpdated: '2026-01-08T00:00:00Z',
    releases: [
      {
        id: 'rel-lodash-418-test-id',
        tag: 'v4.18.0',
        title: 'Lodash 4.18.0',
        publishedAt: '2026-01-08T00:00:00Z',
      },
    ],
  },
]

export const mockFeedResponse: FeedResponseDto = {
  groups: mockFeedGroups,
}

export const handlers = [
  // GET /feed
  http.get(`${API_BASE}/feed`, () => {
    return HttpResponse.json(mockFeedResponse)
  }),

  // GET /packages
  http.get(`${API_BASE}/packages`, () => {
    return HttpResponse.json({
      items: mockPackages,
      total: mockPackages.length,
      limit: 20,
      offset: 0,
    })
  }),

  // GET /packages/:id
  http.get(`${API_BASE}/packages/:id`, ({ params }) => {
    const id = params.id as string
    const pkg = mockPackageDetails.find((p) => p.id === id)
    if (!pkg) {
      return new HttpResponse(null, { status: 404 })
    }
    return HttpResponse.json(pkg)
  }),

  // POST /packages
  http.post(`${API_BASE}/packages`, async ({ request }) => {
    const body = (await request.json()) as { npmName: string }
    const newPackage: PackageDto = {
      id: 'pkg-new-test-id',
      name: body.npmName,
      npmName: body.npmName,
      githubOwner: 'owner',
      githubRepo: body.npmName,
      lastFetchedAt: null,
      createdAt: new Date().toISOString(),
    }
    return HttpResponse.json(newPackage, { status: 201 })
  }),

  // DELETE /packages/:id
  http.delete(`${API_BASE}/packages/:id`, () => {
    return new HttpResponse(null, { status: 204 })
  }),

  // PATCH /packages/:id
  http.patch(`${API_BASE}/packages/:id`, async ({ params, request }) => {
    const id = params.id as string
    const body = (await request.json()) as Partial<PackageDto>
    const pkg = mockPackages.find((p) => p.id === id)
    if (!pkg) {
      return new HttpResponse(null, { status: 404 })
    }
    return HttpResponse.json({ ...pkg, ...body })
  }),

  // GET /releases
  http.get(`${API_BASE}/releases`, () => {
    return HttpResponse.json({
      items: mockReleases,
      total: mockReleases.length,
      limit: 20,
      offset: 0,
    })
  }),

  // GET /releases/:id
  http.get(`${API_BASE}/releases/:id`, ({ params }) => {
    const id = params.id as string
    const release = mockReleases.find((r) => r.id === id)
    if (!release) {
      return new HttpResponse(null, { status: 404 })
    }
    return HttpResponse.json(release)
  }),

  // GET /packages/:id/releases
  http.get(`${API_BASE}/packages/:id/releases`, ({ params }) => {
    const packageId = params.id as string
    const releases = mockPackageReleases.filter(
      (r) => r.package.id === packageId
    )
    return HttpResponse.json({
      items: releases,
      total: releases.length,
      limit: 20,
      offset: 0,
    })
  }),

  // GET /watchlist
  http.get(`${API_BASE}/watchlist`, () => {
    return HttpResponse.json(mockWatchlist)
  }),

  // PUT /watchlist
  http.put(`${API_BASE}/watchlist`, async ({ request }) => {
    const body = (await request.json()) as { packageIds: string[] }
    const newEntries = body.packageIds
      .map((id) => mockPackages.find((p) => p.id === id))
      .filter((p): p is PackageDto => p != null)
      .map((pkg) => ({
        id: pkg.id,
        name: pkg.name,
        githubOwner: pkg.githubOwner,
        githubRepo: pkg.githubRepo,
        npmName: pkg.npmName ?? null,
      }))
    mockWatchlist.splice(0, mockWatchlist.length, ...newEntries)
    return HttpResponse.json(body.packageIds)
  }),

  // POST /watchlist/:packageId
  http.post(`${API_BASE}/watchlist/:packageId`, ({ params }) => {
    const packageId = params.packageId as string
    if (mockWatchlist.some((w) => w.id === packageId)) {
      return HttpResponse.json(
        { error: 'Already watching this package' },
        { status: 409 }
      )
    }
    const pkg = mockPackages.find((p) => p.id === packageId)
    if (!pkg) {
      return HttpResponse.json({ error: 'Package not found' }, { status: 404 })
    }
    mockWatchlist.push({
      id: pkg.id,
      name: pkg.name,
      githubOwner: pkg.githubOwner,
      githubRepo: pkg.githubRepo,
      npmName: pkg.npmName ?? null,
    })
    return HttpResponse.json(packageId, { status: 201 })
  }),

  // DELETE /watchlist/:packageId
  http.delete(`${API_BASE}/watchlist/:packageId`, ({ params }) => {
    const packageId = params.packageId as string
    const index = mockWatchlist.findIndex((w) => w.id === packageId)
    if (index !== -1) {
      mockWatchlist.splice(index, 1)
    }
    return new HttpResponse(null, { status: 204 })
  }),

  // POST /watchlist/github/:owner/:repo
  http.post(`${API_BASE}/watchlist/github/:owner/:repo`, ({ params }) => {
    const owner = params.owner as string
    const repo = params.repo as string
    const packageId = `pkg-${owner}-${repo}-id`
    mockWatchlist.push({
      id: packageId,
      name: `${owner}/${repo}`,
      githubOwner: owner,
      githubRepo: repo,
      npmName: null,
    })
    return HttpResponse.json({ packageId }, { status: 201 })
  }),

  // GET /github/search
  http.get(`${API_BASE}/github/search`, ({ request }) => {
    const url = new URL(request.url)
    const q = url.searchParams.get('q') ?? ''
    if (q.length < 2) {
      return HttpResponse.json([], { status: 400 })
    }
    const results: GitHubRepoSearchResultDto[] = [
      {
        owner: 'facebook',
        repo: 'react',
        description: 'A library for building UIs',
        starCount: 200000,
      },
      {
        owner: 'vuejs',
        repo: 'vue',
        description: 'Progressive JS framework',
        starCount: 190000,
      },
    ]
    return HttpResponse.json(results)
  }),

  // POST /admin/packages/:id/reset-summaries
  http.post(`${API_BASE}/admin/packages/:id/reset-summaries`, () => {
    return new HttpResponse(null, { status: 204 })
  }),

  // POST /admin/packages/:id/reset-releases
  http.post(`${API_BASE}/admin/packages/:id/reset-releases`, () => {
    return new HttpResponse(null, { status: 204 })
  }),

  // GET /subscription/status
  http.get(`${API_BASE}/subscription/status`, () => {
    return HttpResponse.json({ isPro: false, status: null, expiresAt: null })
  }),
]
