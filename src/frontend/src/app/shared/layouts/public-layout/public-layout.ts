import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HeaderComponent } from '../../components/header/header';
import { FooterComponent } from '../../components/footer/footer';
import { GridPatternComponent } from '../../components/grid-pattern/grid-pattern';
import { CookieConsentComponent } from '../../components/cookie-consent/cookie-consent';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-public-layout',
  standalone: true,
  imports: [RouterOutlet, HeaderComponent, FooterComponent, GridPatternComponent, CookieConsentComponent],
  templateUrl: './public-layout.html',
  host: { class: 'flex-1 flex flex-col' },
})
export class PublicLayoutComponent {}
