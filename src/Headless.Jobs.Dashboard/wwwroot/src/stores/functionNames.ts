import { defineStore } from 'pinia'
import { jobsService } from '@/http/services/jobsService';
import { computed } from 'vue';

export const useFunctionNameStore = defineStore('functionNames', () => {
    const getFunctionData = jobsService.getFunctionData();

    const loadData = async () => {
        if (getFunctionData.response.value == undefined) {
            await getFunctionData.requestAsync();
            return data;
        }
        else
            return data;
    }

    loadData();
    
    const data = computed(() => getFunctionData.response.value);

    const getNamespaceOrNull = (functionName: string) : string | null => {
        const result = data.value?.find(x => x.functionName == functionName)?.functionRequestNamespace ?? null;

        if(result == '' || result == null)
            return null;

        return result;
    }

    const getContractVersion = (functionName: string): string => {
        const descriptor = data.value?.find(x => x.functionName === functionName);
        if (!descriptor) throw new Error(`Function '${functionName}' is not registered on this host`);
        return descriptor.contractVersion;
    }

    return{
        loadData,
        data,
        getNamespaceOrNull,
        getContractVersion
    }
})
