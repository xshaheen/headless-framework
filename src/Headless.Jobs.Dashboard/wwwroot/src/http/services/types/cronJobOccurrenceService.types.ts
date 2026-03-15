export class GetCronJobOccurrenceRequest {
    id!:string
}

export class GetCronJobOccurrenceResponse {
    id!: string;
    status!:number|string;
    exceptionMessage?:string;
    skippedReason?:string;
    retryIntervals!:string[]|string|null;
    lockHolder!:string;
    lockedAt!:string;
    executionTime!:string;
    executionTimeFormatted!:string;
    executedAt!:string;
    elapsedTime!:string|number;
    retryCount!:number;
    actions:string|undefined = undefined;
}


export class GetCronJobOccurrenceGraphDataRequest{
}

export class GetCronJobOccurrenceGraphDataResponse{
    date!:string;
    results!:{item1:number, item2:number }[];
    type!: string;
    statuses!:string[]
}