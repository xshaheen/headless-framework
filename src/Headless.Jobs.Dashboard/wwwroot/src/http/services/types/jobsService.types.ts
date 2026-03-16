export class CancelJobRequest {
    
}

export class CancelJobResponse {

}


export class GetJobDataRequest{

}

export class GetJobDataResponse{
    result?:string;
    matchType!:number;
}

export class GetFunctionDataRequest{

}

export class GetFunctionDataResponse{
    functionName!:string;
    functionRequestNamespace!:string;
    functionRequestType!:string;
    priority!:number
}

export class GetNextPlannedJobResponse{
    nextOccurrence?:string;
}

export class GetJobHostStatusResponse{
    isRunning!:boolean;
}


export class GetOptions{
    maxConcurrency!:number;
    currentMachine!:string;
    lastHostExceptionMessage!:string;
    schedulerTimeZone?:string;
}

export class GetMachineJobs{
    item1!:string;
    item2!:number;
}


export class GetJobStatusesPastWeek{
    item1!:string;
    item2!:number;
}

export class GetJobStatusesOverall{
    item1!:string;
    item2!:number;
}
