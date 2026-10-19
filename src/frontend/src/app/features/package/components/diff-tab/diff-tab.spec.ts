import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AnalysisResult, AnalyzeRequest, ChangeCategory, VersionInfo } from '../../../../core/models/package.models';
import { DiffTabComponent } from './diff-tab';

const version = (value: string, overrides: Partial<VersionInfo> = {}): VersionInfo => ({
  version: value,
  publishedDate: null,
  releaseNotes: '',
  releaseNotesHtml: '',
  releaseNotesSource: null,
  entries: [],
  categories: [],
  isEmpty: true,
  isPrerelease: false,
  ...overrides,
});

const analysis: AnalysisResult = {
  packageId: 'left-pad',
  fromVersion: '1.0.0',
  toVersion: '1.2.0',
  totalVersions: 2,
  versions: [
    version('1.2.0', {
      isEmpty: false,
      releaseNotes: 'Added padEnd.',
      releaseNotesHtml: '<p>Added padEnd.</p>',
      releaseNotesSource: 'GitHub',
      categories: [ChangeCategory.NewFeature],
    }),
    version('1.1.0'),
  ],
  summary: { breakingChanges: 0, bugFixes: 0, newFeatures: 1, performanceImprovements: 0, securityFixes: 0, deprecations: 0, otherChanges: 0 },
};

describe('DiffTabComponent', () => {
  let fixture: ComponentFixture<DiffTabComponent>;
  let requests: AnalyzeRequest[];

  const element = () => fixture.nativeElement as HTMLElement;
  const text = () => element().textContent?.replace(/\s+/g, ' ') ?? '';
  const buttonWith = (label: string) =>
    Array.from(element().querySelectorAll('button')).find(button => button.textContent?.trim() === label);

  function render(inputs: Record<string, unknown> = {}): void {
    fixture = TestBed.createComponent(DiffTabComponent);
    fixture.componentRef.setInput('versions', ['1.2.0', '1.1.0', '1.0.0'].map(v => version(v)));
    fixture.componentRef.setInput('packageId', 'left-pad');
    fixture.componentRef.setInput('registry', 'npm');
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    requests = [];
    fixture.componentInstance.onAnalyze.subscribe(request => requests.push(request));
    fixture.detectChanges();
  }

  beforeEach(() => TestBed.configureTestingModule({}));

  it('starts with the two newest versions selected and asks for nothing until one is chosen', () => {
    render();

    expect(fixture.componentInstance.fromVersion()).toBe('1.1.0');
    expect(fixture.componentInstance.toVersion()).toBe('1.2.0');
    expect(requests).toEqual([]);
    expect(text()).toContain('Select two versions to see what changed between them.');
  });

  it('asks for a comparison of the chosen versions', () => {
    render();

    fixture.componentInstance.toggleFromDropdown(new Event('click'));
    fixture.detectChanges();
    buttonWith('1.0.0')!.click();
    fixture.detectChanges();

    expect(requests).toEqual([{
      packageId: 'left-pad', registry: 'npm', currentVersion: '1.0.0', targetVersion: '1.2.0', includePrerelease: false,
    }]);
    expect(fixture.componentInstance.fromOpen()).toBe(false);
  });

  it('shows a spinner while the comparison runs and nothing stale under it', () => {
    render({ loading: true, analysisData: analysis });

    expect(element().querySelector('.animate-spin')).not.toBeNull();
    expect(text()).not.toContain('Added padEnd.');
  });

  it('says why a comparison failed and repeats it on request', () => {
    render({ error: 'Server is busy. Too many analyses in progress. Please try again in a moment.' });

    expect(element().querySelector('[role="alert"]')?.textContent).toContain('Server is busy.');
    expect(text()).not.toContain('Select two versions');

    buttonWith('Try again')!.click();

    expect(requests).toHaveLength(1);
    expect(requests[0]).toMatchObject({ currentVersion: '1.1.0', targetVersion: '1.2.0' });
  });

  it('lists the versions of a finished comparison that have notes, with their source link', () => {
    render({ analysisData: analysis, gitHubUrl: 'https://github.com/left-pad/left-pad' });

    expect(text()).toContain('v1.2.0');
    expect(text()).toContain('Added padEnd.');
    expect(text()).not.toContain('v1.1.0');
    const link = element().querySelector<HTMLAnchorElement>('a[target="_blank"]');
    expect(link?.href).toBe('https://github.com/left-pad/left-pad/releases/tag/v1.2.0');
    expect(link?.rel).toBe('noopener noreferrer');
  });

  it('closes its version lists on a click elsewhere and on Escape', () => {
    render();
    const tab = fixture.componentInstance;

    tab.toggleToDropdown(new Event('click'));
    expect(tab.toOpen()).toBe(true);
    document.body.click();
    expect(tab.toOpen()).toBe(false);

    tab.toggleFromDropdown(new Event('click'));
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(tab.fromOpen()).toBe(false);
  });
});
