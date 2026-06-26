import BaseHub from "./base/baseHub";

export const methodName = {
  onReceiveAddCronJob: "AddCronJobNotification",
  onReceiveUpdateCronJob: "UpdateCronJobNotification",
  onReceiveDeleteCronJob: "RemoveCronJobNotification",
  onReceiveUpdateCronJobOccurrence: "UpdateCronOccurrenceNotification",
  onReceiveAddCronJobOccurrence: "AddCronOccurrenceNotification",
  onReceiveAddTimeJob: "AddTimeJobNotification",
  onReceiveAddTimeJobsBatch: "AddTimeJobsBatchNotification",
  onReceiveUpdateTimeJob: "UpdateTimeJobNotification",
  onReceiveCanceledJob: "CanceledJobNotification",
  onReceiveDeleteTimeJob: "RemoveTimeJobNotification",
  onReceiveThreadsActive: "GetActiveThreadsNotification",
  onReceiveNextOccurrence: "GetNextOccurrenceNotification",
  onReceiveHostStatus: "GetHostStatusNotification",
  onReceiveHostExceptionMessage: "UpdateHostExceptionNotification",
  onReceiveNodesUpdate: "UpdateNodesNotification"
}
// Define a SignalR service class
class JobNotificationHub extends BaseHub {  

  async startConnection(): Promise<void> {
    await this.startConnectionAsync();

    Object.values(methodName).forEach((name) => {
      this.connection.on(name, () => {});
    });
  }

  async stopConnection(): Promise<void> {
    await this.stopConnectionAsync();
  }

  onReceiveAddCronJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddCronJob, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateCronJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateCronJob, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveDeleteCronJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveDeleteCronJob, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateCronJobOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateCronJobOccurrence, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveAddCronJobOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddCronJobOccurrence, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  // Batch add time jobs (used as a lightweight signal to refresh data)
  onReceiveAddTimeJobsBatch(callback: () => void): void {
    this.connection.on(methodName.onReceiveAddTimeJobsBatch, () => {
      callback();
    });
  }

  onReceiveAddTimeJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddTimeJob, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateTimeJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateTimeJob, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveCanceledJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveCanceledJob, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveDeleteTimeJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveDeleteTimeJob, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveThreadsActive<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveThreadsActive, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveNextOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveNextOccurrence, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveHostStatus<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveHostStatus, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  onReceiveHostExceptionMessage<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveHostExceptionMessage, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  // Live-nodes delta pushed by the membership dashboard bridge (one node-state change per event).
  onReceiveNodesUpdate<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveNodesUpdate, (responseFromHub: T) => {
      callback(responseFromHub);
    });
  }

  stopReceiver(methodName: string): void {
    this.connection.off(methodName);
  }

}
export type JobNotificationHubType = InstanceType<typeof JobNotificationHub>;
// Export as a singleton instance
export default new JobNotificationHub();
