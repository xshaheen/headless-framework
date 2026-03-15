
export class GetTimeJobResponse {
    id!:string;
    function!:string;
    status!:string|number;
    retries!:string|number;
    retryCount!:number;
    retryIntervals!:string[]|string|null;
    description!:string;
    requestType!:string;
    lockHolder!:string;
    lockedAt!:string;
    executionTime!:string;
    executionTimeFormatted!:string;
    createdAt!:string;
    updatedAt!:string;
    executedAt!:string;
    elapsedTime!:string|number;
    actions:string|undefined = undefined;
    exceptionMessage?:string;
    skippedReason?:string;
    batchParent?:string;
    batchRunCondition?:string|number;
    children?:GetTimeJobResponse[];
}

export class GetTimeJobGraphDataRangeResponse{
    date!:string;
    results!:{item1:number, item2:number }[];
}

export class GetTimeJobGraphDataResponse{
    item1!:number;
    item2!:number;
}

export class AddTimeJobRequest {
    function!:string;
    request!:string;
    retries!:number;
    description!:string;
    executionTime?:string;
    intervals?:number[];
}

export class UpdateTimeJobRequest {
    function!:string;
    request!:string;
    retries!:number;
    description!:string;
    executionTime?:string;
    intervals?:number[];
}

export class AddChainJobsRequest {
  function!: string;
  description!: string;
  executionTime?: string | null;
  retries!: number;
  request!: string | null; // string that gets converted to bytes by custom converter, or null if not set
  intervals?: number[];
  runCondition?: number;
  children?: AddChainJobsRequest[];
}
