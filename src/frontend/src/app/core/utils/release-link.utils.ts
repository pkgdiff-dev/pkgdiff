export function getReleaseLink(
  packageId: string,
  version: string,
  source: string | null,
  gitHubUrl: string | null,
  registry: string = 'nuget'
): string | null {
  if (source === 'GitHub' && gitHubUrl) {
    const match = gitHubUrl.match(/github\.com\/([^\/]+)\/([^\/]+)/);
    if (match) {
      const [, owner, repo] = match;
      const cleanRepo = repo.replace(/\.git$/, '');
      const cleanVersion = version.replace(/^v/i, '');
      return `https://github.com/${owner}/${cleanRepo}/releases/tag/v${cleanVersion}`;
    }
  }

  switch (registry.toLowerCase()) {
    case 'npm':
      return `https://www.npmjs.com/package/${packageId}/v/${version}`;
    case 'pypi':
      return `https://pypi.org/project/${packageId}/${version}/`;
    case 'maven':
      return `https://central.sonatype.com/artifact/${packageId}/${version}`;
    case 'pub':
    case 'pub.dev':
      return `https://pub.dev/packages/${packageId}/versions/${version}`;
    case 'nuget':
      return `https://www.nuget.org/packages/${packageId}/${version}`;
    default:
      return null;
  }
}

const registryLabels: Record<string, string> = {
  nuget: 'NuGet',
  npm: 'npm',
  pypi: 'PyPI',
  maven: 'Maven',
  pub: 'pub.dev',
  'pub.dev': 'pub.dev',
};

export function getSourceLabel(source: string | null, registry: string = 'nuget'): string {
  if (source === 'GitHub') return 'View on GitHub';
  const label = registryLabels[registry.toLowerCase()] ?? registry;
  return `View on ${label}`;
}
