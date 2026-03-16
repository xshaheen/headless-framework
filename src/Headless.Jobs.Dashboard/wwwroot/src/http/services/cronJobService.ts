
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { AddCronJobRequest, GetCronJobGraphDataRangeResponse, GetCronJobGraphDataResponse, GetCronJobRequest, GetCronJobResponse, UpdateCronJobRequest } from './types/cronJobService.types';
import { nameof } from '@/utilities/nameof';
import { useFunctionNameStore } from '@/stores/functionNames';
import { useTimeZoneStore } from '@/stores/timeZoneStore';

interface PaginatedCronJobResponse {
    items: GetCronJobResponse[]
    totalCount: number
    pageNumber: number
    pageSize: number
}

const getCronJobs = () => {
    const functionNamesStore = useFunctionNameStore();
    const timeZoneStore = useTimeZoneStore();

    const baseHttp = useBaseHttpService<GetCronJobRequest, GetCronJobResponse>('array')
        .FixToResponseModel(GetCronJobResponse, response => {
            response.requestType = functionNamesStore.getNamespaceOrNull(response.function) ?? 'N/A';
            response.createdAt = formatDate(response.createdAt, true, timeZoneStore.effectiveTimeZone);
            response.updatedAt = formatDate(response.updatedAt, true, timeZoneStore.effectiveTimeZone);
            response.initIdentifier = response.initIdentifier?.split("_").slice(0, 2).join("_");
            if ((response.retryIntervals == null || response.retryIntervals.length == 0) && (response.retries == null || (response.retries as number) == 0))
                response.retryIntervals = [];
            else if ((response.retryIntervals == null || response.retryIntervals.length == 0) && (response.retries != null && (response.retries as number) > 0))
                response.retryIntervals = Array(1).fill(`${30}s`);
            else 
                response.retryIntervals = (response.retryIntervals as string[]).map((x: any) => formatTime(x as number, false));
            
            return response;
        })
        .FixToHeaders((header) => {
            if (header.key == nameof<GetCronJobResponse>(x => x.actions)) {
                header.sortable = false;
            }
            if (nameof<GetCronJobResponse>(x => x.id, x => x.retries).includes(header.key)) {
                header.visibility = false;
            }
            return header;
        });

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "cron-jobs"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getCronJobsPaginated = () => {
    const functionNamesStore = useFunctionNameStore();
    const timeZoneStore = useTimeZoneStore();
    
    const baseHttp = useBaseHttpService<object, PaginatedCronJobResponse>('single');
    
    const processResponse = (response: PaginatedCronJobResponse): PaginatedCronJobResponse => {
            // Process items in the paginated response
            if (response && response.items && Array.isArray(response.items)) {
                response.items = response.items.map((item: GetCronJobResponse) => {
                    item.requestType = functionNamesStore.getNamespaceOrNull(item.function) ?? 'N/A';
                    item.createdAt = formatDate(item.createdAt, true, timeZoneStore.effectiveTimeZone);
                    item.updatedAt = formatDate(item.updatedAt, true, timeZoneStore.effectiveTimeZone);
                    item.initIdentifier = item.initIdentifier?.split("_").slice(0, 2).join("_");
                    if ((item.retryIntervals == null || item.retryIntervals.length == 0) && (item.retries == null || (item.retries as number) == 0))
                        item.retryIntervals = [];
                    else if ((item.retryIntervals == null || item.retryIntervals.length == 0) && (item.retries != null && (item.retries as number) > 0))
                        item.retryIntervals = Array(1).fill(`${30}s`);
                    else 
                        item.retryIntervals = (item.retryIntervals as string[]).map((x: any) => formatTime(x as number, false));
                    
                    return item;
                });
            }
            
            return response;
    };
    
    const requestAsync = async (pageNumber: number = 1, pageSize: number = 20) => {
        const response = await baseHttp.sendAsync("GET", "cron-jobs/paginated", { 
            paramData: { pageNumber, pageSize } 
        });
        return processResponse(response);
    };
    
    return {
        ...baseHttp,
        requestAsync
    };
}

const updateCronJob = () => {
    const baseHttp = useBaseHttpService<UpdateCronJobRequest, object>('single')
    const requestAsync = async (id: string, request: UpdateCronJobRequest) => (await baseHttp.sendAsync("PUT", "cron-job/update", { bodyData: request, paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const addCronJob = () => {
    const baseHttp = useBaseHttpService<AddCronJobRequest, object>('single')
    const requestAsync = async (request: AddCronJobRequest) => (await baseHttp.sendAsync("POST", "cron-job/add", { bodyData: request }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const deleteCronJob = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "cron-job/delete", { paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const runCronJobOnDemand = () => {
    const baseHttp = useBaseHttpService<object, object>('single')
    const requestAsync = async (id: string) => (await baseHttp.sendAsync("POST", "cron-job/run", { paramData: { id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeJobsGraphDataRange = () => {
    const baseHttp = useBaseHttpService<object, GetCronJobGraphDataRangeResponse>('array')
        .FixToResponseModel(GetCronJobGraphDataRangeResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false),
            }
        });

    const requestAsync = async (startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "cron-jobs/graph-data-range", {paramData: {pastDays: startDate, futureDays: endDate}}));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeJobsGraphDataRangeById = () => {
    const baseHttp = useBaseHttpService<object, GetCronJobGraphDataRangeResponse>('array')
        .FixToResponseModel(GetCronJobGraphDataRangeResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false),
            }
        });

    const requestAsync = async (id:string ,startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "cron-jobs/graph-data-range-id", {paramData: {id: id, pastDays: startDate, futureDays: endDate}}));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeJobsGraphData = () => {
    const baseHttp = useBaseHttpService<object, GetCronJobGraphDataResponse>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "cron-jobs/graph-data"));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const cronJobService = {
    getCronJobs,
    getCronJobsPaginated,
    updateCronJob,
    addCronJob,
    deleteCronJob,
    runCronJobOnDemand,
    getTimeJobsGraphDataRange,
    getTimeJobsGraphDataRangeById,
    getTimeJobsGraphData
};
