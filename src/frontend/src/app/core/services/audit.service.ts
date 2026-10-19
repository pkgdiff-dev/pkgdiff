import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api';
import { AuditDependency, AuditReport, ManifestParseResult, StartAuditResponse } from '../models/audit.models';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private api = inject(ApiService);

  parse(registry: string, content: string, fileName: string | null): Observable<ManifestParseResult> {
    return this.api.post<ManifestParseResult>('audit/parse', { registry, content, fileName });
  }

  start(registry: string, format: string | null, dependencies: AuditDependency[]): Observable<StartAuditResponse> {
    return this.api.post<StartAuditResponse>('audit', { registry, format, dependencies });
  }

  get(id: string): Observable<AuditReport> {
    return this.api.get<AuditReport>(`audit/${encodeURIComponent(id)}`);
  }
}
