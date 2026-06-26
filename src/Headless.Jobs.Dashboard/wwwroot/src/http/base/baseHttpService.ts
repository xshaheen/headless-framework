// useBaseHttpService.ts

import axios, { type Canceler, type Method } from "axios";
import { type Ref, ref } from "vue";
import http from "./axiosConfig";
import type { Path, PathValue } from "@/utilities/pathTypes";
import { useAlert } from "@/composables/useAlert";
import { getStatusValueSafe } from "../services/types/base/baseHttpResponse.types";

/* ------------------------------------------------------------------
   1) Define TableHeader Interface and Helper Function
------------------------------------------------------------------ */

/**
 * Describes the structure of each table header.
 */
export interface TableHeader {
  title: string;
  key: string;
  align?: 'start' | 'center' | 'end';
  sortable?: boolean;
  visibility: boolean;
  // Add any additional properties as needed
}

/**
 * Converts a camelCase or snake_case string to Title Case.
 * E.g., 'firstName' -> 'First Name', 'last_name' -> 'Last Name'
 */
function formatKeyToTitle(key: string): string {
  // Replace underscores with spaces
  let result = key.replace(/_/g, ' ');

  // Insert spaces before capital letters (for camelCase)
  result = result.replace(/([A-Z])/g, ' $1');

  // Capitalize the first letter of each word
  return result.replace(/\b\w/g, char => char.toUpperCase()).trim();
}

/* ------------------------------------------------------------------
   2) Define Service Interfaces
------------------------------------------------------------------ */

/** Single-response interface */
export interface BaseHttpServiceSingle<TRequest, TSingle extends object> {
  loader: Ref<boolean>;
  response: Ref<TSingle | undefined>;
  headers: Ref<TableHeader[] | undefined>;
  updateResponse(response: TSingle | undefined): void;
  updateProperty<T extends Path<TSingle>>(key: T, keyValue: PathValue<TSingle, T>): void;

  sendAsync(
    methodType: Method,
    url: string,
    options?: {
      bodyData?: TRequest;
      paramData?: Record<string, unknown>;
      suppressAlert?: boolean; // Set to false to force show alert (default: axios interceptor handles it)
    }
  ): Promise<TSingle>;

  /**
   * Called if you want to "filter" or "transform" the server response
   * to match the shape of `TSingle`. 
   * 
   * For single-mode, TModel = TSingle.
   */
  FixToResponseModel(
    model: new () => TSingle,
    transform?: (item: TSingle) => TSingle
  ): this;

  /**
   * Generate or adjust table headers based on the model's keys.
   * The transform function operates on individual TableHeader items.
   * Must be called after FixToResponseModel.
   */
  FixToHeaders(
    transform?: (header: TableHeader) => TableHeader
  ): this;
}

/** Array-response interface */
export interface BaseHttpServiceArray<TRequest, TItem extends object> {
  loader: Ref<boolean>;
  response: Ref<TItem[] | undefined>;
  headers: Ref<TableHeader[] | undefined>;
  updateResponse(newResponse: TItem[] | undefined): void;
  addToResponse(newItem: TItem): void;
  removeFromResponse<T extends Path<TItem>>(key: T, value: PathValue<TItem, T>): void;
  updateByKey<T extends Path<TItem>>(key: T, value: TItem, ignoreKeys: T[]): void;
  updateByNestedKey(nestedObjectKey: string, key: string, value: TItem, ignoreKeys: string[]): void;
  updatePropertyByKey<T extends Path<TItem>, V extends Path<TItem>>(key: T, keyValue: PathValue<TItem, T>, property: V, value: PathValue<TItem, V>): void;
  updateProperty<T extends Path<TItem>>(key: T, keyValue: PathValue<TItem, T>): void;

  sendAsync(
    methodType: Method,
    url: string,
    options?: {
      bodyData?: TRequest;
      paramData?: Record<string, unknown>;
      suppressAlert?: boolean; // Set to false to force show alert (default: axios interceptor handles it)
    }
  ): Promise<TItem[]>;

  /**
   * For array-mode, you pass a model describing a single item `TItem`.
   * Then optionally transform each item.
   */
  FixToResponseModel(
    model: new () => TItem,
    transform?: (item: TItem) => TItem
  ): this;

  /**
   * Generate or adjust table headers based on the model's keys.
   * The transform function operates on individual TableHeader items.
   * Must be called after FixToResponseModel.
   */
  FixToHeaders(
    transform?: (header: TableHeader) => TableHeader
  ): this;

  ReOrganizeResponse(
    transform: (response: TItem[]) => TItem[] | undefined
  ): this;
}

/* ------------------------------------------------------------------
   3) Overloads: "single" vs "array"
------------------------------------------------------------------ */

/**
 * Initializes the HTTP service in "single" mode.
 * @param mode - Must be "single".
 * @returns A service tailored for single-item responses.
 */
export function useBaseHttpService<TRequest extends object, TSingle extends object>(
  mode: "single"
): BaseHttpServiceSingle<TRequest, TSingle>;

/**
 * Initializes the HTTP service in "array" mode.
 * @param mode - Must be "array".
 * @returns A service tailored for array responses.
 */
export function useBaseHttpService<TRequest extends object, TItem extends object>(
  mode: "array"
): BaseHttpServiceArray<TRequest, TItem>;

/* ------------------------------------------------------------------
   4) Implementation of useBaseHttpService
------------------------------------------------------------------ */

/**
 * Factory function to create an HTTP service tailored for single or array responses.
 * @param mode - "single" or "array".
 * @returns An instance of BaseHttpServiceSingle or BaseHttpServiceArray.
 */
export function useBaseHttpService(
  mode: "single" | "array"
): BaseHttpServiceSingle<object, object> | BaseHttpServiceArray<object, object> {

  const cancelRequest: Ref<Canceler | undefined> = ref(undefined);
  const loader = ref(false);
  const { showHttpError } = useAlert();

  // Will be set if you call FixToResponseModel
  const responseModelKeys: Ref<string[] | undefined> = ref([]);
  let transformFn: ((item: object) => object) | undefined;
  let reOrganizeFn: ((response: object[]) => object[] | undefined) | undefined;

  /**
   * A helper to process raw data from the server:
   * - Filter to `responseModelKeys` (case-insensitive)
   * - Then apply `transformFn` if present
   */
  function processResponse<T extends object>(
    data: unknown,
    keys?: string[],
    transform?: (x: T) => T
  ): T | T[] {
    if (Array.isArray(data)) {
      return data.map((item) => processOneItem<T>(item, keys, transform));
    }
    return processOneItem<T>(data, keys, transform);
  }

  function processOneItem<T extends object>(
    item: unknown,
    keys?: string[],
    transform?: (x: T) => T
  ): T {
    if (!item || typeof item !== "object") {
      return {} as T;
    }

    const source = item as Record<string, unknown>;

    let filtered: Partial<T>;
    if (keys && keys.length > 0) {
      filtered = {};
      // For each model key (case-insensitive), find the property in `item`
      for (const modelKey of keys) {
        const foundItemKey = Object.keys(source).find(
          (k) => k.toLowerCase() === modelKey.toLowerCase()
        );
        if (foundItemKey) {
          filtered[modelKey as keyof T] = source[foundItemKey] as T[keyof T];
        }
      }
    } else {
      // If no keys specified, clone entire item
      filtered = { ...source } as Partial<T>;
    }

    return transform ? transform(filtered as T) : (filtered as T);
  }

  /* --------------------------------------------------------------
     "single" mode
     -------------------------------------------------------------- */
  if (mode === "single") {
    const response = ref<object>();
    const headers = ref<TableHeader[] | undefined>(undefined);

    const sendAsync = async (
      methodType: Method,
      url: string,
      options?: { bodyData?: object; paramData?: Record<string, unknown>; suppressAlert?: boolean }
    ): Promise<object> => {
      if (cancelRequest.value) cancelRequest.value();
      loader.value = true;
      try {
        const res = await http.request({
          url,
          method: methodType,
          params: options?.paramData,
          headers: { 'Content-Type': 'application/json' },
          data: options?.bodyData,
          cancelToken: new axios.CancelToken((exec) => {
            cancelRequest.value = exec;
          }),
        });

        if (res.data.status != undefined) {
          res.data.status = getStatusValueSafe(res.data.status);
        }


        const processed = processResponse(res.data, responseModelKeys.value, transformFn);

        if (reOrganizeFn) {
          response.value = reOrganizeFn(processed as object[]);
        } else {
          response.value = processed;
        }

        return response.value as object;
      } catch (error) {
        // Don't show error alert here since axios interceptor handles it
        // Only show if explicitly requested and not suppressed
        if (options?.suppressAlert === false) {
          showHttpError(error);
        }
        throw error;
      } finally {
        loader.value = false;
      }
    };

    const FixToResponseModel = (model: new () => object, transform?: (item: object) => object) => {
      responseModelKeys.value = Object.keys(new model());
      transformFn = transform;
      return baseHttpService;
    };

    const updateResponse = (newResponse: object | undefined) => {
      const processed = processResponse(newResponse, responseModelKeys.value, transformFn);
      response.value = processed;
    };

    /**
     * FixToHeaders
     * Derive default headers from the model's keys,
     * optionally transform them individually.
     * Must be called after FixToResponseModel.
     */
    const FixToHeaders = (
      transform?: (header: TableHeader) => TableHeader
    ) => {
      const keys = responseModelKeys.value;
      if (!keys) {
        // FixToHeaders called before FixToResponseModel.
        return baseHttpService;
      }

      let defaultHeaders: TableHeader[] = keys.map((key) => ({
        title: formatKeyToTitle(key),
        key,
        sortable: true,
        visibility: true
      }));

      // Apply transform to each header if provided
      if (typeof transform === "function") {
        defaultHeaders = defaultHeaders.map(transform);
        defaultHeaders = defaultHeaders.filter(x => x.visibility == true);
      }

      headers.value = defaultHeaders;
      return baseHttpService;
    };


    const updateProperty = (property: string, value: unknown) => {
      (response.value as Record<string, unknown>)[property] = value;
    };

    const baseHttpService: BaseHttpServiceSingle<object, object> = {
      loader,
      response,
      headers,
      sendAsync,
      FixToResponseModel,
      FixToHeaders,
      updateResponse,
      updateProperty
    };

    return baseHttpService;
  }

  /* --------------------------------------------------------------
     "array" mode
     -------------------------------------------------------------- */
  else {
    const response = ref<object[]>();
    const headers = ref<TableHeader[] | undefined>(undefined);

    const sendAsync = async (
      methodType: Method,
      url: string,
      options?: { bodyData?: object; paramData?: Record<string, unknown>; suppressAlert?: boolean }
    ): Promise<object[]> => {
      if (cancelRequest.value) cancelRequest.value(); // Cancel existing request
      loader.value = true;
      try {
        const res = await http.request<object[]>({
          url,
          method: methodType,
          params: options?.paramData,
          data: options?.bodyData,
          headers: { 'Content-Type': 'application/json' },
          cancelToken: new axios.CancelToken((exec) => {
            cancelRequest.value = exec;
          }),
        });

        res.data = res.data.map((item) => {
          const record = item as Record<string, unknown>;
          if (record.status != undefined) {
            record.status = getStatusValueSafe(record.status as string | number);
          }
          return item;
        });

        const organized = reOrganizeFn ? reOrganizeFn(res.data) : res.data;

        const processed = processResponse(organized, responseModelKeys.value, transformFn);

        response.value = processed as object[];

        return response.value as object[];

      } catch (error) {
        // Don't show error alert here since axios interceptor handles it
        // Only show if explicitly requested and not suppressed
        if (options?.suppressAlert === false) {
          showHttpError(error);
        }
        throw error;
      } finally {
        loader.value = false;
      }
    };

    const FixToResponseModel = (model: new () => object, transform?: (item: object) => object) => {
      responseModelKeys.value = Object.keys(new model());
      transformFn = transform;
      return baseHttpService;
    };

    const updateResponse = (newResponse: object[] | undefined) => {
      if (reOrganizeFn) {
        newResponse = reOrganizeFn(newResponse as object[]);
      }
      const processed = processResponse(newResponse, responseModelKeys.value, transformFn);
      response.value = processed as object[];
    };

    const addToResponse = (newItem: object) => {
      const processed = processResponse(newItem, responseModelKeys.value, transformFn);
      response.value?.push(processed as object);
      if (reOrganizeFn) {
        response.value = reOrganizeFn(response.value as object[]);
      }
    };

    const removeFromResponse = (key: string, value: unknown) => {
      if (!response.value) return;

      // Handle both string and GUID comparisons for better reliability
      const filtered = response.value.filter(item => {
        const itemValue = (item as Record<string, unknown>)[key];
        // Convert both values to strings for comparison to handle GUID/string mismatches
        return String(itemValue) !== String(value);
      });

      response.value = filtered;

      // Apply reorganization if needed
      if (reOrganizeFn) {
        response.value = reOrganizeFn(response.value as object[]);
      }
    };

      const updateByNestedKey = (nestedObjectKey: string, key: string, value: object, ignoreKeys: string[] = []) => {
        const valueRecord = value as Record<string, unknown>;
        // First try to find at root level
        let itemToUpdate: object | null | undefined = response.value?.find(item => (item as Record<string, unknown>)[key] == valueRecord[key]);

        // Recursive function to search in nested children at any depth
        const findInNestedChildren = (items: object[], nestedKey: string): object | null => {
          for (const item of items) {
            const record = item as Record<string, unknown>;
            // Check if this item has the nested property (e.g., 'children')
            if (record[nestedKey] && Array.isArray(record[nestedKey])) {
              const children = record[nestedKey] as object[];
              // Search in direct children
              const foundChild = children.find((child) => (child as Record<string, unknown>)[key] == valueRecord[key]);
              if (foundChild) {
                return foundChild;
              }

              // Recursively search in grandchildren, great-grandchildren, etc.
              const foundInDeeper = findInNestedChildren(children, nestedKey);
              if (foundInDeeper) {
                return foundInDeeper;
              }
            }
          }
          return null;
        };

        // If not found at root, search recursively in nested children
        if (!itemToUpdate && response.value) {
          itemToUpdate = findInNestedChildren(response.value, nestedObjectKey);
        }

        if (!itemToUpdate) {
          return; // Exit early if no matching item found anywhere
        }

        const processed = processResponse(value, responseModelKeys.value, transformFn);

        Object.keys(processed).forEach((itemKey) => {
          if (!ignoreKeys.includes(itemKey)) {
            (itemToUpdate as Record<string, unknown>)[itemKey] = (processed as Record<string, unknown>)[itemKey];
          }
        });

        if (reOrganizeFn) {
          response.value = reOrganizeFn(response.value as object[]);
        }
      }

    const updateByKey = (key: string, value: object, ignoreKeys: string[] = []) => {
      const valueRecord = value as Record<string, unknown>;
      const item = response.value?.find(item => (item as Record<string, unknown>)[key] == valueRecord[key]);

      if (!item) {
        return; // Exit early if no matching item found
      }

      const processed = processResponse(value, responseModelKeys.value, transformFn);

      Object.keys(processed).forEach((itemKey) => {
        if (!ignoreKeys.includes(itemKey)) {
          (item as Record<string, unknown>)[itemKey] = (processed as Record<string, unknown>)[itemKey];
        }
      });

      if (reOrganizeFn) {
        response.value = reOrganizeFn(response.value as object[]);
      }
    };

    const updatePropertyByKey = (key: string, keyValue: unknown, property: string, value: unknown) => {
      const item = response.value?.find(item => (item as Record<string, unknown>)[key] === keyValue);
      if (item) {
        (item as Record<string, unknown>)[property] = value;
      }
    };

    const updateProperty = (property: string, value: unknown) => {
      response.value?.forEach(x => {
        (x as Record<string, unknown>)[property] = value
      })
    };

    const ReOrganizeResponse = (transform: (response: object[]) => object[] | undefined) => {
      reOrganizeFn = transform;
      return baseHttpService;
    };

    /**
     * FixToHeaders
     * Derive default headers from the model's keys,
     * optionally transform them individually.
     * Must be called after FixToResponseModel.
     */
    const FixToHeaders = (
      transform?: (header: TableHeader) => TableHeader
    ) => {
      const keys = responseModelKeys.value;
      if (!keys) {
        // FixToHeaders called before FixToResponseModel.
        return baseHttpService;
      }

      let defaultHeaders: TableHeader[] = keys.map((key) => ({
        title: formatKeyToTitle(key),
        key,
        sortable: true,
        visibility: true
      }));

      // Apply transform to each header if provided
      if (typeof transform === "function") {
        defaultHeaders = defaultHeaders.map(transform);
        defaultHeaders = defaultHeaders.filter(x => x.visibility == true);
      }

      headers.value = defaultHeaders;
      return baseHttpService;
    };

    const baseHttpService: BaseHttpServiceArray<object, object> = {
      loader,
      response,
      headers,
      sendAsync,
      FixToResponseModel,
      FixToHeaders,
      updateResponse,
      addToResponse,
      removeFromResponse,
      updateByKey,
      updateByNestedKey,
      ReOrganizeResponse,
      updatePropertyByKey,
      updateProperty
    };

    return baseHttpService;
  }
}