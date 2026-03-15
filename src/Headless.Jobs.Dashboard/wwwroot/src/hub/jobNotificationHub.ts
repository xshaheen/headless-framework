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
  onReceiveHostExceptionMessage: "UpdateHostExceptionNotification"
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
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddCronJob, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateCronJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateCronJob, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveDeleteCronJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveDeleteCronJob, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateCronJobOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateCronJobOccurrence, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveAddCronJobOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddCronJobOccurrence, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  // Batch add time tickers (used as a lightweight signal to refresh data)
  onReceiveAddTimeJobsBatch(callback: () => void): void {
    this.connection.on(methodName.onReceiveAddTimeJobsBatch, () => {
      callback();
    });
  }

  onReceiveAddTimeJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveAddTimeJob, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveUpdateTimeJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveUpdateTimeJob, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveCanceledJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveCanceledJob, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveDeleteTimeJob<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveDeleteTimeJob, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveThreadsActive<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveThreadsActive, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveNextOccurrence<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveNextOccurrence, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveHostStatus<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveHostStatus, (responseFromHub: any) => {
      callback(responseFromHub);
    });
  }

  onReceiveHostExceptionMessage<T>(callback: (response: T) => void): void {
    this.onReceiveMessageAsSingle<T>(methodName.onReceiveHostExceptionMessage, (responseFromHub: any) => {
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
