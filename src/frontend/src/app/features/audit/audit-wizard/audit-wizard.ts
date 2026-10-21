import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LucideAngularModule, Upload, Trash2, ArrowLeft, ShieldCheck, FileText } from 'lucide-angular';
import { AuditService } from '../../../core/services/audit.service';
import { SeoService } from '../../../core/services/seo.service';
import { AuditDependency, ManifestParseResult } from '../../../core/models/audit.models';
import { apiErrorMessage } from '../../../core/utils/http-error.utils';

interface Ecosystem {
  registry: string;
  label: string;
  registryLabel: string;
  icon: string;
  files: string[];
  placeholder: string;
}

type Step = 1 | 2 | 3;

const MAX_FILE_BYTES = 1024 * 1024;

const ECOSYSTEMS: Ecosystem[] = [
  {
    registry: 'npm',
    label: 'JavaScript / TypeScript',
    registryLabel: 'npm',
    icon: '/icons/npm.svg',
    files: ['package.json', 'package-lock.json'],
    placeholder: '{\n  "dependencies": {\n    "express": "^4.17.1",\n    "lodash": "4.17.20"\n  }\n}',
  },
  {
    registry: 'nuget',
    label: '.NET',
    registryLabel: 'NuGet',
    icon: '/icons/nuget.svg',
    files: ['.csproj', 'Directory.Packages.props', 'packages.config', 'packages.lock.json'],
    placeholder: '<Project Sdk="Microsoft.NET.Sdk">\n  <ItemGroup>\n    <PackageReference Include="Newtonsoft.Json" Version="12.0.1" />\n  </ItemGroup>\n</Project>',
  },
  {
    registry: 'pypi',
    label: 'Python',
    registryLabel: 'PyPI',
    icon: '/icons/pypi.svg',
    files: ['requirements.txt', 'pyproject.toml', 'poetry.lock', 'Pipfile', 'Pipfile.lock'],
    placeholder: 'requests==2.25.0\nflask>=2.0\ndjango~=3.2',
  },
  {
    registry: 'pub.dev',
    label: 'Dart / Flutter',
    registryLabel: 'pub.dev',
    icon: '/icons/pub.svg',
    files: ['pubspec.yaml', 'pubspec.lock'],
    placeholder: 'dependencies:\n  http: ^0.13.0\n  provider: ^6.0.0',
  },
  {
    registry: 'packagist',
    label: 'PHP',
    registryLabel: 'Packagist',
    icon: '/icons/packagist.svg',
    files: ['composer.json', 'composer.lock'],
    placeholder: '{\n  "require": {\n    "laravel/framework": "^10.0",\n    "guzzlehttp/guzzle": "7.4.0"\n  }\n}',
  },
];

@Component({
  selector: 'app-audit-wizard',
  standalone: true,
  imports: [FormsModule, LucideAngularModule],
  templateUrl: './audit-wizard.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditWizardComponent {
  private audits = inject(AuditService);
  private router = inject(Router);

  readonly UploadIcon = Upload;
  readonly TrashIcon = Trash2;
  readonly BackIcon = ArrowLeft;
  readonly ShieldIcon = ShieldCheck;
  readonly FileIcon = FileText;

  readonly ecosystems = ECOSYSTEMS;
  readonly steps = ['Ecosystem', 'Dependency file', 'Review', 'Report'];

  step = signal<Step>(1);
  ecosystem = signal<Ecosystem | null>(null);
  content = signal('');
  fileName = signal<string | null>(null);
  parsing = signal(false);
  parseError = signal<string | null>(null);
  parsed = signal<ManifestParseResult | null>(null);
  rows = signal<AuditDependency[]>([]);
  starting = signal(false);
  startError = signal<string | null>(null);

  unpinned = computed(() => this.rows().filter(r => !r.version).length);

  constructor() {
    inject(SeoService).updateMeta({
      title: 'Dependency Security Audit — Check NuGet, npm, PyPI, pub.dev & Packagist Packages | pkgdiff',
      description: 'Paste a package.json, .csproj, requirements.txt, pubspec.yaml, composer.json or lock file and get a report of the known vulnerabilities of every dependency, with the versions that fix them.',
      url: '/audit',
    });
  }

  chooseEcosystem(ecosystem: Ecosystem): void {
    if (this.ecosystem()?.registry !== ecosystem.registry) {
      this.content.set('');
      this.fileName.set(null);
      this.parsed.set(null);
      this.rows.set([]);
    }
    this.ecosystem.set(ecosystem);
    this.parseError.set(null);
    this.step.set(2);
  }

  goTo(step: Step): void {
    if (step < this.step()) this.step.set(step);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (file.size > MAX_FILE_BYTES) {
      this.parseError.set('The file is larger than 1 MB.');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      this.content.set(String(reader.result ?? ''));
      this.fileName.set(file.name);
      this.parseError.set(null);
    };
    reader.onerror = () => this.parseError.set('The file could not be read.');
    reader.readAsText(file);
  }

  readPackages(): void {
    const ecosystem = this.ecosystem();
    if (!ecosystem || !this.content().trim() || this.parsing()) return;

    this.parsing.set(true);
    this.parseError.set(null);
    this.audits.parse(ecosystem.registry, this.content(), this.fileName()).subscribe({
      next: result => {
        this.parsing.set(false);
        this.parsed.set(result);
        this.rows.set(result.dependencies.map(d => ({ ...d })));
        this.step.set(3);
      },
      error: (error: unknown) => {
        this.parsing.set(false);
        this.parseError.set(apiErrorMessage(error, 'The file could not be read. Please check that it is complete.'));
      },
    });
  }

  setVersion(index: number, version: string): void {
    this.rows.update(rows => rows.map((row, i) => (i === index ? { ...row, version: version.trim() || null } : row)));
  }

  remove(index: number): void {
    this.rows.update(rows => rows.filter((_, i) => i !== index));
  }

  startAudit(): void {
    const ecosystem = this.ecosystem();
    if (!ecosystem || this.rows().length === 0 || this.starting()) return;

    this.starting.set(true);
    this.startError.set(null);
    this.audits.start(ecosystem.registry, this.parsed()?.format ?? null, this.rows()).subscribe({
      next: response => {
        this.starting.set(false);
        this.router.navigate(['/audit', response.id]);
      },
      error: (error: unknown) => {
        this.starting.set(false);
        this.startError.set(apiErrorMessage(error, 'The audit could not be started. Please try again.'));
      },
    });
  }
}
