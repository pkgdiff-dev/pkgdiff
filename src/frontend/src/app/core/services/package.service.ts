import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api';
import { AnalysisResult, AnalyzeRequest, RegistryInfo, LiveUpdate, PaginatedResult, PackageMetadata, PackageSuggestion } from '../models/package.models';

@Injectable({ providedIn: 'root' })
export class PackageService {
  private api = inject(ApiService);

  getLiveUpdates(page = 1, pageSize = 20): Observable<PaginatedResult<LiveUpdate>> {
    return this.api.get<PaginatedResult<LiveUpdate>>('Package/live-updates', { page, pageSize });
  }

  getRegistries(): Observable<RegistryInfo[]> {
    return this.api.get<RegistryInfo[]>('Package/registries');
  }

  getPackageMetadata(registry: string, packageId: string): Observable<PackageMetadata> {
    return this.api.get<PackageMetadata>(`Package/${registry}/${encodeURIComponent(packageId)}`);
  }

  analyze(request: AnalyzeRequest): Observable<AnalysisResult> {
    return this.api.post<AnalysisResult>('Package/analyze', request);
  }

  getCachedPackages(): Observable<PackageMetadata[]> {
    return this.api.get<PackageMetadata[]>('Package/cached');
  }

  getSuggestions(registry: string, query: string, limit = 5): Observable<PackageSuggestion[]> {
    return this.api.get<PackageSuggestion[]>(`Package/${registry}/suggest?q=${encodeURIComponent(query)}&limit=${limit}`);
  }
}
