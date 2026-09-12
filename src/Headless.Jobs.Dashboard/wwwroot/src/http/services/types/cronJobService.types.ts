export class GetCronJobRequest {
}

export class GetCronJobResponse {
    contractVersion!: string;
    correlationId?: string;
    causationId?: string;
    tenantId?: string;
    id!:string;
    function!:string;
    expression!:string;
    initIdentifier!:string;
    retryIntervals!:string[];
    description!:string;
    requestType!:string;
    createdAt!:string;
    updatedAt!:string;
    retries!:number;
    actions:string|undefined = undefined;
}

export class UpdateCronJobRequest {
    contractVersion?: string;
    function!:string;
    expression!:string;
    request?:string;
    retries?: number;
    description?: string;
    intervals?:number[];
}

export class GetCronJobGraphDataRangeResponse{
    date!:string;
    results!:{status:number, count:number }[];
}

export class GetCronJobGraphDataResponse{
    item1!:number;
    item2!:number;
}

export class AddCronJobRequest {
    contractVersion?: string;
    function!:string;
    expression!:string;
    request?:string;
    retries?: number;
    description?: string;
    intervals?:number[];
}
