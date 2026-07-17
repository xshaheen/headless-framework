
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { Status } from './types/base/baseHttpResponse.types';
import {
  AddTimeJobRequest,
  AddChainJobsRequest,
  GetTimeJobGraphDataRangeResponse,
  GetTimeJobGraphDataResponse,
  GetTimeJobResponse,
  UpdateTimeJobRequest
} from './types/timeJobService.types'
import { format} from 'timeago.js';
import { useFunctionNameStore } from '@/stores/functionNames';
import { useTimeZoneStore } from '@/stores/timeZoneStore';

interface PaginatedTimeJobResponse {
    items: GetTimeJobResponse[]
    totalCount: number
    pageNumber: number
    pageSize: number
}

const getTimeJobsPaginated = () => {
    const functionNamesStore = useFunctionNameStore();
    const timeZoneStore = useTimeZoneStore();
    
    const baseHttp = useBaseHttpService<object, PaginatedTimeJobResponse>('single');
    
    const processResponse = (response: PaginatedTimeJobResponse): PaginatedTimeJobResponse => {
            // Process items in the paginated response
            if (response && response.items && Array.isArray(response.items)) {
                response.items = response.items.map((item: GetTimeJobResponse) => {
                    const processItem = (item: GetTimeJobResponse): GetTimeJobResponse => {
                        if (item.status !== undefined && item.status !== null) {
                            item.status = Status[item.status as number];
                        }
                        
                        if (item.executedAt != null || item.executedAt != undefined)
                            item.executedAt = `${format(item.executedAt)} (took ${formatTime(item.elapsedTime as number, true)})`;
                        
                        item.executionTimeFormatted = formatDate(item.executionTime, true, timeZoneStore.effectiveTimeZone);
                        item.requestType = functionNamesStore.getNamespaceOrNull(item.function) ?? '';
                        
                        if (item.retryIntervals == null || item.retryIntervals.length == 0 && item.retries != null && (item.retries as number) > 0)
                            item.retryIntervals = Array(1).fill(`${30}s`);
                        else
                            item.retryIntervals = (item.retryIntervals as string[]).map((x: string) => formatTime(x as unknown as number, false));
                        
                        item.lockHolder = item.lockHolder ?? '-';
                        
                        if (item.children && Array.isArray(item.children)) {
                            item.children = item.children.map(child => processItem(child));
                        }
                        
                        return item;
                    };
                    
                    return processItem(item);
                });
                
                // Sort items
                response.items.sort((a: GetTimeJobResponse, b: GetTimeJobResponse) => 
                    new Date(b.executionTime).getTime() - new Date(a.executionTime).getTime()
                );
            }
            
            return response;
    };
    
    const requestAsync = async (pageNumber: number = 1, pageSize: number = 20) => {
        const response = await baseHttp.sendAsync("GET", "time-jobs/paginated", { 
            paramData: { pageNumber, pageSize } 
        });
        return processResponse(response);
    };
    
    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeJobsGraphDataRange = () => {
    const baseHttp = useBaseHttpService<object, GetTimeJobGraphDataRangeResponse>('array')
        .FixToResponseModel(GetTimeJobGraphDataRangeResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false),
            }
        });

    const requestAsync = async (startDate: number, endDate: number) => (await baseHttp.sendAsync("GET", "time-jobs/graph-data-range", {paramData: {pastDays: startDate, futureDays: endDate}}));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTimeJobsGraphData = () => {
    const baseHttp = useBaseHttpService<object, GetTimeJobGraphDataResponse>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "time-jobs/graph-data"));

    return {
        ...baseHttp,
        requestAsync
    };
}


const deleteTimeJob = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "time-job/delete", { paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const deleteTimeJobsBatch = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async (ids: string[]) =>
        await baseHttp.sendAsync("DELETE", "time-job/delete-batch", { bodyData: ids });

    return {
        ...baseHttp,
        requestAsync
    };
}

const addTimeJob = () => {
    const baseHttp = useBaseHttpService<AddTimeJobRequest, object>('single');

    const requestAsync = async (data: AddTimeJobRequest, timeZoneId?: string | null) => {
        const paramData: Record<string, string> = {};
        if (timeZoneId) {
            paramData.timeZoneId = timeZoneId;
        }
        return await baseHttp.sendAsync("POST", "time-job/add", { bodyData: data, paramData });
    };

    return {
        ...baseHttp,
        requestAsync
    };
}

const updateTimeJob = () => {
    const baseHttp = useBaseHttpService<UpdateTimeJobRequest, object>('single');

    const requestAsync = async (id: string, data: UpdateTimeJobRequest, timeZoneId?: string | null) => {
        const paramData: Record<string, string> = { id };
        if (timeZoneId) {
            paramData.timeZoneId = timeZoneId;
        }
        return await baseHttp.sendAsync("PUT", "time-job/update", { bodyData: data, paramData });
    };

    return {
        ...baseHttp,
        requestAsync
    };
}

const addChainJobs = () => {
  const baseHttp = useBaseHttpService<AddChainJobsRequest, object>('single');

  const requestAsync = async (data: AddChainJobsRequest) => (await baseHttp.sendAsync("POST", "time-job/add", { bodyData: data }));

  return {
    ...baseHttp,
    requestAsync
  };
}



export const timeJobService = {
    getTimeJobsPaginated,
    deleteTimeJob,
    deleteTimeJobsBatch,
    getTimeJobsGraphDataRange,
    getTimeJobsGraphData,
    addTimeJob,
    updateTimeJob,
    addChainJobs
};
