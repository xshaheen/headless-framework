import * as signalR from "@microsoft/signalr";
import { getBasePath, getBackendUrl } from '@/utilities/pathResolver';
import { getHubAccessToken } from '@/services/auth';

class BaseHub {
    public connection: signalR.HubConnection;

    constructor() {
        this.connection = this.createConnection();
    }

    private createConnection(): signalR.HubConnection {

        const basePath = getBasePath();
        const backendUrl = getBackendUrl();

        // Use backend domain for WebSocket if configured, otherwise use base path
        let hubUrl: string;
        if (backendUrl) {
            hubUrl = `${backendUrl}/job-notification-hub`;
        } else {
            // Avoid leading '//' when basePath is '/'
            hubUrl = basePath === '/' 
                ? '/job-notification-hub' 
                : `${basePath}/job-notification-hub`;
        }

        const accessToken = getHubAccessToken();
        
        // WebSockets cannot send custom headers, so we need to use query parameters
        // For other transports (ServerSentEvents, LongPolling), we can use headers
        const useWebSocketsOnly = true; // Set to false if you want to allow fallback transports
        
        if (useWebSocketsOnly) {
            // WebSocket transport - use access_token query parameter for auth
            const connectionOptions: signalR.IHttpConnectionOptions = {
                transport: signalR.HttpTransportType.WebSockets
            };
            
            let finalHubUrl = hubUrl;
            
            if (accessToken) {
                finalHubUrl = `${hubUrl}?access_token=${encodeURIComponent(accessToken)}`;
            }
            
            return new signalR.HubConnectionBuilder()
                .withUrl(finalHubUrl, connectionOptions)
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: retryContext => {
                        // Exponential backoff with max 3 retries
                        if (retryContext.previousRetryCount >= 3) {
                            console.log('🛑 SignalR: Max retry attempts reached, stopping reconnection');
                            return null; // Stop retrying
                        }
                        const delay = Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
                        console.log(`🔄 SignalR: Retrying connection in ${delay}ms (attempt ${retryContext.previousRetryCount + 1}/3)`);
                        return delay;
                    }
                })
                .configureLogging(signalR.LogLevel.Information)
                .build();
        } else {
            // Allow fallback transports - use headers for auth
            const connectionOptions: signalR.IHttpConnectionOptions = {};
            
            if (accessToken) {
                connectionOptions.headers = { 'Authorization': accessToken };
            }
            
            return new signalR.HubConnectionBuilder()
                .withUrl(hubUrl, connectionOptions)
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: retryContext => {
                        // Exponential backoff with max 3 retries
                        if (retryContext.previousRetryCount >= 3) {
                            console.log('🛑 SignalR: Max retry attempts reached, stopping reconnection (fallback transport)');
                            return null; // Stop retrying
                        }
                        const delay = Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
                        console.log(`🔄 SignalR: Retrying connection in ${delay}ms (attempt ${retryContext.previousRetryCount + 1}/3)`);
                        return delay;
                    }
                })
                .configureLogging(signalR.LogLevel.Information)
                .build();
        }
    }

    // Method to rebuild connection with new auth token
    public rebuildConnection(): void {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            this.connection.stop();
        }
        this.connection = this.createConnection();
    }

    // Send a message to the server
    protected async sendMessage(methodName: string): Promise<void> {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            try {
                await this.connection.invoke(methodName);
            } catch {
                // Error sending message
            }
        } else {
            // Cannot send message: SignalR connection is not active.
        }
    }

    // Start Connection
    async startConnectionAsync(): Promise<void> {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            return;
        }
        
        if (this.connection.state === signalR.HubConnectionState.Connecting) {
            return;
        }
        
        try {
            console.log('🔗 SignalR: Starting connection...');
            await this.connection.start();
            console.log('✅ SignalR: Connection established successfully');
        } catch (err) {
            console.error('🚨 SignalR Connection Error:', err);
            
            // Check if it's an authentication error
            const message = err instanceof Error ? err.message : '';
            if (message.includes('401') ||
                message.includes('Unauthorized') ||
                message.includes('Authentication failed')) {
                console.error('🚫 SignalR: Authentication failed - connection will not retry');
                // Don't rethrow authentication errors to prevent infinite retry
                return;
            }
            
            // For other errors, rethrow to allow normal error handling and retry logic
            throw err;
        }
    }

    async stopConnectionAsync(): Promise<void> {
        try {
            await this.connection.stop();
        } catch {
            // Error stopping SignalR connection
        }
    }

    joinGroup(groupName: string): void {
        this.connection.invoke("JoinGroup", groupName);
    }

    leaveGroup(groupName: string): void {
        this.connection.invoke("LeaveGroup", groupName);
    }

    // Subscribe to messages from the server
    onReceiveMessageAsSingle<T>(methodName: string, callback: (response: T) => void): void {
        this.connection.on(methodName, (responseFromHub: unknown) => {
            if (Array.isArray(responseFromHub)) {
                responseFromHub.forEach((response) => {
                    callback(response as T);
                });
            } else {
                callback(responseFromHub as T);
            }
        });
    }
}

export default BaseHub;
