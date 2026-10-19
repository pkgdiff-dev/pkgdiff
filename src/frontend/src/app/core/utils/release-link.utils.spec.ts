import { getReleaseLink, getSourceLabel } from './release-link.utils';

describe('getReleaseLink', () => {
  it('points at the GitHub release when the notes came from GitHub', () => {
    expect(getReleaseLink('left-pad', 'v1.3.0', 'GitHub', 'https://github.com/left-pad/left-pad.git', 'npm'))
      .toBe('https://github.com/left-pad/left-pad/releases/tag/v1.3.0');
  });

  it('points at the version page of the registry otherwise', () => {
    expect(getReleaseLink('Serilog', '4.0.0', 'NuGet', null, 'nuget')).toBe('https://www.nuget.org/packages/Serilog/4.0.0');
    expect(getReleaseLink('left-pad', '1.3.0', null, null, 'NPM')).toBe('https://www.npmjs.com/package/left-pad/v/1.3.0');
    expect(getReleaseLink('requests', '2.32.0', null, null, 'pypi')).toBe('https://pypi.org/project/requests/2.32.0/');
    expect(getReleaseLink('dio', '5.4.0', null, null, 'pub')).toBe('https://pub.dev/packages/dio/versions/5.4.0');
    expect(getReleaseLink('dio', '5.4.0', 'GitHub', null, 'pub.dev')).toBe('https://pub.dev/packages/dio/versions/5.4.0');
  });

  it('offers no link for a registry it does not know', () => {
    expect(getReleaseLink('thing', '1.0.0', null, null, 'crates')).toBeNull();
  });
});

describe('getSourceLabel', () => {
  it('names where the link goes', () => {
    expect(getSourceLabel('GitHub', 'npm')).toBe('View on GitHub');
    expect(getSourceLabel(null, 'nuget')).toBe('View on NuGet');
    expect(getSourceLabel(null, 'pub')).toBe('View on pub.dev');
    expect(getSourceLabel(null, 'pub.dev')).toBe('View on pub.dev');
    expect(getSourceLabel(null, 'crates')).toBe('View on crates');
  });
});
