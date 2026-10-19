export interface InstallSnippet {
  label: string;
  code: string;
}

export function installSnippets(registry: string, packageId: string, version: string): InstallSnippet[] {
  switch (registry.toLowerCase()) {
    case 'npm':
      return [
        { label: 'npm', code: `npm install ${packageId}@${version}` },
        { label: 'package.json', code: `"${packageId}": "${version}"` },
      ];
    case 'nuget':
      return [
        { label: '.NET CLI', code: `dotnet add package ${packageId} --version ${version}` },
        { label: 'PackageReference', code: `<PackageReference Include="${packageId}" Version="${version}" />` },
      ];
    case 'pypi':
      return [
        { label: 'pip', code: `pip install ${packageId}==${version}` },
        { label: 'requirements.txt', code: `${packageId}==${version}` },
      ];
    case 'pub':
    case 'pub.dev':
      return [
        { label: 'dart pub', code: `dart pub add ${packageId}:${version}` },
        { label: 'pubspec.yaml', code: `${packageId}: ${version}` },
      ];
    default:
      return [];
  }
}
