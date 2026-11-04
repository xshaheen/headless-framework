import * as signalR from "@microsoft/signalr";
import { getBasePath, getBackendUrl } from '@/utilities/pathResolver';
import { getHubAccessToken } from '@/services/auth';

type HubHandler = (...args: unknown[]) => void;

// A failed negotiate surfaces as an HttpError, or as an error whose message carries the status code.
function isUnauthorized(error: unknown): boolean {
    if (error instanceof signalR.HttpError && error.statusCode === 401) {
        return true;
    }

    const message = error instanceof Error ? error.message : '';
    return message.includes('401') || message.includes('Unauthorized');
}

/** The server refused the hub with 401. The session is gone, so retrying cannot connect until the user signs in. */
export class HubUnauthorizedError extends Error {
    constructor() {
        super('The notification hub refused the connection: the session has ended.');
        this.name = 'HubUnauthorizedError';
    }
}

class BaseHub {
    public connection: signalR.HubConnection;

    /** Called when the server refuses the connection with 401: the session is gone and the user must sign in. */
    public onUnauthorized: (() => void) | null = null;

    // Basic and API key carry the credential in the URL, so a credential that changes (sign-in, sign-out, another
    // user) needs a new connection. Handlers are kept here and replayed onto it, so subscriptions survive the rebuild.
    private readonly handlers: Array<[string, HubHandler]> = [];
    private builtWithToken: string | null;
    // True while this client stops the connection itself, so its close is not mistaken for the server dropping it.
    private stopRequested = false;

    constructor() {
        this.builtWithToken = getHubAccessToken();
        this.connection = this.createConnection(this.builtWithToken);
    }

    private createConnection(accessToken: string | null): signalR.HubConnection {
        const basePath = getBasePath();
        const backendUrl = getBackendUrl();

        let hubUrl: string;
        if (backendUrl) {
            hubUrl = `${backendUrl}/job-notification-hub`;
        } else {
            // Avoid leading '//' when basePath is '/'
            hubUrl = basePath === '/'
                ? '/job-notification-hub'
                : `${basePath}/job-notification-hub`;
        }

        // A browser cannot set headers on a WebSocket, so Basic and API key send their credential as the
        // access_token query parameter. Host mode has none: the same-origin session cookie rides the handshake.
        const finalHubUrl = accessToken
            ? `${hubUrl}?access_token=${encodeURIComponent(accessToken)}`
            : hubUrl;

        const connection = new signalR.HubConnectionBuilder()
            .withUrl(finalHubUrl, { transport: signalR.HttpTransportType.WebSockets })
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: retryContext => {
                    if (retryContext.previousRetryCount >= 3) {
                        return null;
                    }
                    return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
                }
            })
            .configureLogging(signalR.LogLevel.Information)
            .build();

        connection.onclose(() => this.handleClose(connection));
        return connection;
    }

    // The server closes the connection when the session it was opened with expires. Reconnecting sends the current
    // cookie (or stored credential): a live session reconnects, and a 401 sends the user to sign in.
    private async handleClose(closed: signalR.HubConnection): Promise<void> {
        if (this.stopRequested || closed !== this.connection) {
            return;
        }

        try {
            await this.startConnectionAsync();
        } catch (err) {
            // A refused session already sent the user to sign in; any other failure is retried by the health check.
            if (!(err instanceof HubUnauthorizedError)) {
                console.error('SignalR: reconnect after close failed', err);
            }
        }
    }

    // Method to rebuild connection with new auth token
    public rebuildConnection(): void {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            this.stopRequested = true;
            this.connection.stop().finally(() => {
                this.stopRequested = false;
            });
        }
        this.replaceConnection(getHubAccessToken());
    }

    private replaceConnection(accessToken: string | null): void {
        this.builtWithToken = accessToken;
        this.connection = this.createConnection(accessToken);
        for (const [methodName, handler] of this.handlers) {
            this.connection.on(methodName, handler);
        }
    }

    // Send a message to the server
    protected async sendMessage(methodName: string): Promise<void> {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            try {
                await this.connection.invoke(methodName);
            } catch {
                // Error sending message
            }
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

        // Built before sign-in, or for a credential that has since changed: connect with the current one.
        const accessToken = getHubAccessToken();
        if (
            this.connection.state === signalR.HubConnectionState.Disconnected &&
            accessToken !== this.builtWithToken
        ) {
            this.replaceConnection(accessToken);
        }

        try {
            await this.connection.start();
        } catch (err) {
            if (isUnauthorized(err)) {
                // Thrown rather than returned so no caller mistakes the refused connection for an open one.
                this.onUnauthorized?.();
                throw new HubUnauthorizedError();
            }

            throw err;
        }
    }

    async stopConnectionAsync(): Promise<void> {
        this.stopRequested = true;
        try {
            await this.connection.stop();
        } catch {
            // Error stopping SignalR connection
        } finally {
            this.stopRequested = false;
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
        const handler: HubHandler = (responseFromHub: unknown) => {
            if (Array.isArray(responseFromHub)) {
                responseFromHub.forEach((response) => {
                    callback(response as T);
                });
            } else {
                callback(responseFromHub as T);
            }
        };
        this.handlers.push([methodName, handler]);
        this.connection.on(methodName, handler);
    }
}

export default BaseHub;
