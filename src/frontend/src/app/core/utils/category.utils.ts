import { ChangeCategory } from '../models/package.models';
import type { LucideIconData } from 'lucide-angular';
import { 
  AlertTriangle, 
  Bug, 
  Sparkles, 
  Zap, 
  Shield, 
  XCircle,
  Info
} from 'lucide-angular';

export interface CategoryStyle {
  icon: LucideIconData;
  label: string;
  bgClass: string;
  textClass: string;
  borderClass: string;
  dotClass: string;
}

export function getCategoryStyle(category: ChangeCategory): CategoryStyle {
  switch (category) {
    case ChangeCategory.BreakingChange:
      return {
        icon: AlertTriangle,
        label: 'Breaking',
        bgClass: 'bg-red-50 dark:bg-red-900/20',
        textClass: 'text-red-700 dark:text-red-400',
        borderClass: 'border-red-200 dark:border-red-800',
        dotClass: 'bg-red-500'
      };
    case ChangeCategory.BugFix:
      return {
        icon: Bug,
        label: 'Fix',
        bgClass: 'bg-blue-50 dark:bg-blue-900/20',
        textClass: 'text-blue-700 dark:text-blue-400',
        borderClass: 'border-blue-200 dark:border-blue-800',
        dotClass: 'bg-blue-500'
      };
    case ChangeCategory.NewFeature:
      return {
        icon: Sparkles,
        label: 'Feature',
        bgClass: 'bg-green-50 dark:bg-green-900/20',
        textClass: 'text-green-700 dark:text-green-400',
        borderClass: 'border-green-200 dark:border-green-800',
        dotClass: 'bg-green-500'
      };
    case ChangeCategory.Performance:
      return {
        icon: Zap,
        label: 'Performance',
        bgClass: 'bg-yellow-50 dark:bg-yellow-900/20',
        textClass: 'text-yellow-700 dark:text-yellow-400',
        borderClass: 'border-yellow-200 dark:border-yellow-800',
        dotClass: 'bg-yellow-500'
      };
    case ChangeCategory.Security:
      return {
        icon: Shield,
        label: 'Security',
        bgClass: 'bg-orange-50 dark:bg-orange-900/20',
        textClass: 'text-orange-700 dark:text-orange-400',
        borderClass: 'border-orange-200 dark:border-orange-800',
        dotClass: 'bg-orange-500'
      };
    case ChangeCategory.Deprecation:
      return {
        icon: XCircle,
        label: 'Deprecated',
        bgClass: 'bg-purple-50 dark:bg-purple-900/20',
        textClass: 'text-purple-700 dark:text-purple-400',
        borderClass: 'border-purple-200 dark:border-purple-800',
        dotClass: 'bg-purple-500'
      };
    case ChangeCategory.Other:
    default:
      return {
        icon: Info,
        label: 'Change',
        bgClass: 'bg-gray-50 dark:bg-gray-900/20',
        textClass: 'text-gray-700 dark:text-gray-400',
        borderClass: 'border-gray-200 dark:border-gray-800',
        dotClass: 'bg-gray-500'
      };
  }
}
