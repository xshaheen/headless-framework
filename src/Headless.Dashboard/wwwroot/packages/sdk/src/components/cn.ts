import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/** Joins class names, letting a later utility replace an earlier one of the same kind, as shadcn's components do. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
