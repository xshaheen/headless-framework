import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";
import { listUploads, terminateUpload } from "./api";

export const uploadsQueryKey = ["uploads"] as const;

export function useUploadsQuery() {
  return useQuery({ queryKey: uploadsQueryKey, queryFn: listUploads });
}

export function useDeleteUpload() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: terminateUpload,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: uploadsQueryKey }),
  });
}

/** Stable callback both uploader parts call when a transfer completes. */
export function useInvalidateUploads() {
  const queryClient = useQueryClient();

  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: uploadsQueryKey });
  }, [queryClient]);
}
