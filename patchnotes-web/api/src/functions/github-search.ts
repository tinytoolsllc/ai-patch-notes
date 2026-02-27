import {
  app,
  HttpRequest,
  HttpResponseInit,
  InvocationContext,
} from '@azure/functions'

interface CacheEntry {
  data: GitHubSearchResult[]
  expiry: number
}

interface GitHubSearchResult {
  owner: string
  repo: string
  description: string | null
  starCount: number
}

interface GitHubApiItem {
  owner?: { login?: string }
  name?: string
  description?: string | null
  stargazers_count?: number
}

const CACHE_TTL_MS = 5 * 60 * 1000
const MAX_CACHE_ITEMS = 500
const cache = new Map<string, CacheEntry>()

function getCached(cacheKey: string): GitHubSearchResult[] | null {
  const entry = cache.get(cacheKey)
  if (!entry) {
    return null
  }

  if (entry.expiry <= Date.now()) {
    cache.delete(cacheKey)
    return null
  }

  return entry.data
}

function setCached(cacheKey: string, data: GitHubSearchResult[]): void {
  if (cache.size >= MAX_CACHE_ITEMS) {
    const now = Date.now()
    for (const [key, value] of cache.entries()) {
      if (value.expiry <= now) {
        cache.delete(key)
      }
    }

    if (cache.size >= MAX_CACHE_ITEMS) {
      const oldestKey = cache.keys().next().value
      if (oldestKey) {
        cache.delete(oldestKey)
      }
    }
  }

  cache.set(cacheKey, { data, expiry: Date.now() + CACHE_TTL_MS })
}

async function githubSearch(
  request: HttpRequest,
  context: InvocationContext
): Promise<HttpResponseInit> {
  const query = request.query.get('q')?.trim() ?? ''
  if (query.length < 2) {
    return {
      status: 400,
      jsonBody: {
        error:
          "Query parameter 'q' is required and must be at least 2 characters",
      },
    }
  }

  const cacheKey = query.toLowerCase()
  const cached = getCached(cacheKey)
  if (cached) {
    return {
      status: 200,
      headers: {
        'Cache-Control': 'public, max-age=60',
        'X-Cache': 'HIT',
      },
      jsonBody: cached,
    }
  }

  const githubToken = process.env.GITHUB_PAT || process.env.GITHUB_TOKEN
  const headers: Record<string, string> = {
    Accept: 'application/vnd.github+json',
    'User-Agent': 'PatchNotes',
    'X-GitHub-Api-Version': '2022-11-28',
  }

  if (githubToken) {
    headers.Authorization = `Bearer ${githubToken}`
  }

  try {
    const url = new URL('https://api.github.com/search/repositories')
    url.searchParams.set('q', query)
    url.searchParams.set('per_page', '10')

    const response = await fetch(url, { headers })
    if (!response.ok) {
      const bodyPreview = (await response.text().catch(() => '')).slice(0, 200)
      context.error(
        `GitHub API returned ${response.status} for q="${query}": ${bodyPreview}`
      )
      return { status: 502, jsonBody: { error: 'GitHub search failed' } }
    }

    const json = (await response.json()) as { items?: GitHubApiItem[] }
    const items = Array.isArray(json?.items) ? json.items : []
    const results: GitHubSearchResult[] = items
      .map((item) => ({
        owner: item?.owner?.login ?? '',
        repo: item?.name ?? '',
        description: item?.description ?? null,
        starCount:
          typeof item?.stargazers_count === 'number'
            ? item.stargazers_count
            : 0,
      }))
      .filter((item) => item.owner.length > 0 && item.repo.length > 0)

    setCached(cacheKey, results)

    return {
      status: 200,
      headers: {
        'Cache-Control': 'public, max-age=60',
        'X-Cache': 'MISS',
      },
      jsonBody: results,
    }
  } catch (error) {
    context.error(`GitHub search request failed for q="${query}":`, error)
    return { status: 502, jsonBody: { error: 'GitHub search failed' } }
  }
}

app.http('github-search', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'github-search',
  handler: githubSearch,
})
