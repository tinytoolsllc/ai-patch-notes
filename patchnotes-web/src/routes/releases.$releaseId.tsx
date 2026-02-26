import { createFileRoute } from '@tanstack/react-router'
import { seoHead } from '../seo'
import { getGetReleaseQueryOptions } from '../api/hooks'

export const Route = createFileRoute('/releases/$releaseId')({
  loader: ({ context, params }) =>
    context.queryClient.ensureQueryData(
      getGetReleaseQueryOptions(params.releaseId)
    ),
  head: ({ loaderData }) => {
    if (!loaderData || loaderData.status !== 200) {
      return {
        ...seoHead({
          title: 'Release | My Release Notes',
          description: 'View release details on My Release Notes.',
          path: '/releases',
        }),
      }
    }
    const { data } = loaderData
    const owner = data.package.githubOwner
    const repo = data.package.githubRepo
    const tag = data.tag
    return {
      ...seoHead({
        title: `${owner}/${repo} ${tag} | My Release Notes`,
        description: `Release notes for ${owner}/${repo} ${tag}.`,
        path: `/releases/${data.id}`,
        jsonLd: {
          '@context': 'https://schema.org',
          '@type': 'TechArticle',
          headline: `${owner}/${repo} ${tag}`,
          datePublished: data.publishedAt,
          about: {
            '@type': 'SoftwareSourceCode',
            name: repo,
            version: tag,
            codeRepository: `https://github.com/${owner}/${repo}`,
          },
        },
      }),
    }
  },
})
