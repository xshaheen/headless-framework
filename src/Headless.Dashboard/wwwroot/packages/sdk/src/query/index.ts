// Modules reach TanStack Query only through this entry, so every view reads the shell's one data client.
export {
  infiniteQueryOptions,
  keepPreviousData,
  queryOptions,
  skipToken,
  useInfiniteQuery,
  useIsFetching,
  useMutation,
  useQueries,
  useQuery,
  useQueryClient,
  useSuspenseQuery,
} from '@tanstack/react-query'
export type { QueryClient, QueryKey, UseMutationResult, UseQueryOptions, UseQueryResult } from '@tanstack/react-query'
