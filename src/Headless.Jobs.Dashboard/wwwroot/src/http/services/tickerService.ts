
import { useBaseHttpService } from '../base/baseHttpService';
import { CancelJobRequest, CancelJobResponse, GetFunctionDataRequest, GetJobStatusesOverall, GetFunctionDataResponse, GetJobStatusesPastWeek, GetMachineJobs, GetNextPlannedJobResponse, GetOptions, GetJobDataRequest, GetJobDataResponse, GetJobHostStatusResponse } from './types/tickerService.types';

const requestCancel = () => {
    const baseHttp = useBaseHttpService<CancelJobRequest, CancelJobResponse>('single');

    const requestAsync = async (id: string) => (await baseHttp.sendAsync("POST", "job/cancel", { paramData: { id: id } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getRequestData = () => {
    const baseHttp = useBaseHttpService<GetJobDataRequest, GetJobDataResponse>('single');

    const requestAsync = async (id: string, type: number) => (await baseHttp.sendAsync("GET", "job-request/id", { paramData: { tickerId: id, tickerType: type } }));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getFunctionData = () => {
    const baseHttp = useBaseHttpService<GetFunctionDataRequest, GetFunctionDataResponse>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "job-functions"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getNextPlannedTicker = () => {
    const baseHttp = useBaseHttpService<object, GetNextPlannedJobResponse>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "job-host/next-ticker"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const stopTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("POST", "job-host/stop"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const startTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("POST", "job-host/start"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const restartTicker = () => {
    const baseHttp = useBaseHttpService<object, object>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("POST", "job-host/restart"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getTickerHostStatus = () => {
    const baseHttp = useBaseHttpService<object, GetJobHostStatusResponse>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "job-host/status"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getOptions = () => {
    const baseHttp = useBaseHttpService<object, GetOptions>('single');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "options"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getMachineJobs = () => {
    const baseHttp = useBaseHttpService<object, GetMachineJobs>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "ticker/machine/jobs"));

    return {
        ...baseHttp,
        requestAsync
    };
}


const getJobStatusesPastWeek = () => {
    const baseHttp = useBaseHttpService<object, GetJobStatusesPastWeek>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "/ticker/statuses/get-last-week"));

    return {
        ...baseHttp,
        requestAsync
    };
}

const getJobStatusesOverall = () => {
    const baseHttp = useBaseHttpService<object, GetJobStatusesOverall>('array');

    const requestAsync = async () => (await baseHttp.sendAsync("GET", "/ticker/statuses/get"));

    return {
        ...baseHttp,
        requestAsync
    };
}


export const tickerService = {
    requestCancel,
    getRequestData,
    getFunctionData,
    getNextPlannedTicker,
    stopTicker,
    startTicker,
    restartTicker,
    getTickerHostStatus,
    getOptions,
    getMachineJobs,
    getJobStatusesPastWeek,
    getJobStatusesOverall
};
