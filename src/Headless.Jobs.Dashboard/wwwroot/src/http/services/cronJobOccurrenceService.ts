
import { formatDate, formatTime } from '@/utilities/dateTimeParser';
import { useBaseHttpService } from '../base/baseHttpService';
import { Status } from './types/base/baseHttpResponse.types';
import { GetCronJobOccurrenceGraphDataRequest, GetCronJobOccurrenceGraphDataResponse, GetCronJobOccurrenceResponse } from './types/cronJobOccurrenceService.types';
import { format} from 'timeago.js';
import { useTimeZoneStore } from '@/stores/timeZoneStore';

interface PaginatedCronJobOccurrenceResponse {
    items: GetCronJobOccurrenceResponse[]
    totalCount: number
    pageNumber: number
    pageSize: number
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
                        const statusValue = Status[item.status as number];
                        item.status = statusValue !== undefined ? statusValue : String(item.status);
                    } else {
                        item.status = 'Unknown';
                    }
                    
                    if (item.dateExecuted != null || item.dateExecuted != undefined) {
                        // Ensure the datetime is treated as UTC by adding 'Z' if missing
                        const utcDateExecuted = item.dateExecuted.endsWith('Z') ? item.dateExecuted : item.dateExecuted + 'Z';
                        item.dateExecuted = `${format(utcDateExecuted)} (took ${formatTime(item.elapsedTime as number, true)})`;
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
                statuses: item.results.map(x => Status[x.status])
            }
        });

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("GET", `cron-job-occurrences/${id}/graph-data`));

    return {
        ...baseHttp,
        requestAsync
    };
}

export const cronJobOccurrenceService = {
    getByCronJobIdPaginated,
    deleteCronJobOccurrence,
    getCronJobOccurrenceGraphData
};
