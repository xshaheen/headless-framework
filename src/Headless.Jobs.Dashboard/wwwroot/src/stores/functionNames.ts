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
        var result = data.value?.find(x => x.functionName == functionName)?.functionRequestNamespace ?? null;

        if(result == '' || result == null)
            return null;

        return result;
    }

    return{
        loadData,
        data,
        getNamespaceOrNull
    }
})
