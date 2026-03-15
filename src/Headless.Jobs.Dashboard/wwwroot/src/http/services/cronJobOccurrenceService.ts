
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { Status } from './types/base/baseHttpResponse.types';
import { GetCronJobOccurrenceGraphDataRequest, GetCronJobOccurrenceGraphDataResponse, GetCronJobOccurrenceRequest, GetCronJobOccurrenceResponse } from './types/cronJobOccurrenceService.types';
import { format} from 'timeago.js';
import { nameof } from '@/utilities/nameof';
import { useTimeZoneStore } from '@/stores/timeZoneStore';

interface PaginatedCronJobOccurrenceResponse {
    items: GetCronJobOccurrenceResponse[]
    totalCount: number
    pageNumber: number
    pageSize: number
}

const getByCronJobId = () => {
    const timeZoneStore = useTimeZoneStore();
    const baseHttp = useBaseHttpService<GetCronJobOccurrenceRequest, GetCronJobOccurrenceResponse>('array')
        .FixToResponseModel(GetCronJobOccurrenceResponse, response => {
            if (!response) {
                return response;
            }
            
            // Safely set status with null check
            if (response.status !== undefined && response.status !== null) {
                response.status = Status[response.status as any];
            }

            if (response.executedAt != null || response.executedAt != undefined) {
                // Ensure the datetime is treated as UTC by adding 'Z' if missing
                const utcExecutedAt = response.executedAt.endsWith('Z') ? response.executedAt : response.executedAt + 'Z';
                response.executedAt = `${format(utcExecutedAt)} (took ${formatTime(response.elapsedTime as number, true)})`;
            }

            const utcExecutionTime = response.executionTime.endsWith('Z') ? response.executionTime : response.executionTime + 'Z';
            response.executionTimeFormatted = formatDate(utcExecutionTime, true, timeZoneStore.effectiveTimeZone);
            response.lockedAt = formatDate(response.lockedAt, true, timeZoneStore.effectiveTimeZone)
            return response;
        })
        .FixToHeaders((header) => {
            if (header.key == nameof<GetCronJobOccurrenceResponse>(x => x.actions)) {
                header.sortable = false;
            }
            if (nameof<GetCronJobOccurrenceResponse>(x => x.id, x => x.elapsedTime, x => x.executionTime, x => x.retryCount, x => x.exceptionMessage, x => x.skippedReason).includes(header.key)) {
                header.visibility = false;
            }
            if (nameof<GetCronJobOccurrenceResponse>(x => x.executedAt) == header.key) {
                header.title = "Executed At (Elapsed Time)"
            }
            if (nameof<GetCronJobOccurrenceResponse>(x => x.executionTimeFormatted) == header.key) {
                header.title = "Execution Time"
            }
            return header;
        })
        .ReOrganizeResponse((res) => res.sort((a, b) => new Date(b.executionTime).getTime() - new Date(a.executionTime).getTime()));


    const requestAsync = async (id: string | undefined) => (await baseHttp.sendAsync("GET", `cron-job-occurrences/${id}`));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getByCronJobIdPaginated = () => {
    const timeZoneStore = useTimeZoneStore();
    const baseHttp = useBaseHttpService<object, PaginatedCronJobOccurrenceResponse>('single');
    
    const processResponse = (response: PaginatedCronJobOccurrenceResponse): PaginatedCronJobOccurrenceResponse => {
            // Process items in the paginated response
            if (response && response.items && Array.isArray(response.items)) {
                response.items = response.items.map((item: GetCronJobOccurrenceResponse) => {
                    if (!item) return item;
                    
                    // Safely set status with null check and ensure it's always a string
                    if (item.status !== undefined && item.status !== null) {
                        const statusValue = Status[item.status as any];
                        item.status = statusValue !== undefined ? statusValue : String(item.status);
                    } else {
                        item.status = 'Unknown';
                    }
                    
                    if (item.executedAt != null || item.executedAt != undefined) {
                        // Ensure the datetime is treated as UTC by adding 'Z' if missing
                        const utcExecutedAt = item.executedAt.endsWith('Z') ? item.executedAt : item.executedAt + 'Z';
                        item.executedAt = `${format(utcExecutedAt)} (took ${formatTime(item.elapsedTime as number, true)})`;
                    }
                    
                    const utcExecutionTime = item.executionTime.endsWith('Z') ? item.executionTime : item.executionTime + 'Z';
                    item.executionTimeFormatted = formatDate(utcExecutionTime, true, timeZoneStore.effectiveTimeZone);
                    item.lockedAt = formatDate(item.lockedAt, true, timeZoneStore.effectiveTimeZone);
                    
                    return item;
                });
                
                // Sort items
                response.items.sort((a: GetCronJobOccurrenceResponse, b: GetCronJobOccurrenceResponse) => 
                    new Date(b.executionTime).getTime() - new Date(a.executionTime).getTime()
                );
            }
            
            return response;
    };
    
    const requestAsync = async (id: string | undefined, pageNumber: number = 1, pageSize: number = 20) => {
        const response = await baseHttp.sendAsync("GET", `cron-job-occurrences/${id}/paginated`, { 
            paramData: { pageNumber, pageSize } 
        });
        return processResponse(response);
    };
    
    return {
        ...baseHttp,
        requestAsync
    };
}

const deleteCronJobOccurrence = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("DELETE", "cron-job-occurrence/delete", { paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getCronJobOccurrenceGraphData = () => {
    const timeZoneStore = useTimeZoneStore();
    const baseHttp = useBaseHttpService<GetCronJobOccurrenceGraphDataRequest, GetCronJobOccurrenceGraphDataResponse>('array')
        .FixToResponseModel(GetCronJobOccurrenceGraphDataResponse, (item) => {
            return {
                ...item,
                date: formatDate(item.date, false, timeZoneStore.effectiveTimeZone),
                type: "line",
                statuses: item.results.map(x => Status[x.item1])
            }
        });

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("GET", `cron-job-occurrences/${id}/graph-data`));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const cronJobOccurrenceService = {
    getByCronJobId,
    getByCronJobIdPaginated,
    deleteCronJobOccurrence,
    getCronJobOccurrenceGraphData
};
