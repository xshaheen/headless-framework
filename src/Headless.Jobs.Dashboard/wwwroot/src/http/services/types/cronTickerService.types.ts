export class GetCronJobRequest {
}

export class GetCronJobResponse {
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
    function!:string;
    expression!:string;
    request?:string;
    retries?: number;
    description?: string;
    intervals?:number[];
}

export class GetCronJobGraphDataRangeResponse{
    date!:string;
    results!:{item1:number, item2:number }[];
}

export class GetCronJobGraphDataResponse{
    item1!:number;
    item2!:number;
}

export class AddCronJobRequest {
    function!:string;
    expression!:string;
    request?:string;
    retries?: number;
    description?: string;
    intervals?:number[];
}
