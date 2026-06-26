// nameof.ts

// Overload for a single property selector
export function nameof<T>(expr: (obj: T) => unknown): string;

// Overload for two property selectors
export function nameof<T>(
    expr1: (obj: T) => unknown,
    expr2: (obj: T) => unknown
): [string, string];

// Overload for multiple property selectors
export function nameof<T>(
    expr1: (obj: T) => unknown,
    expr2: (obj: T) => unknown,
    ...exprs: Array<(obj: T) => unknown>
): string[];

// Implementation Signature
export function nameof<T>(
    ...exprs: Array<(obj: T) => unknown>
): string | [string, string] | string[] {
    const paths: string[] = [];

    exprs.forEach(expr => {
        const path: string[] = [];

        const handler: ProxyHandler<object> = {
            get(target, prop, receiver) {
                if (typeof prop === 'symbol') {
                    return receiver;
                }
                path.push(prop.toString());
                return new Proxy(target, handler);
            }
        };

        const proxy = new Proxy({}, handler) as T;
        expr(proxy);

        paths.push(path.join('.'));
    });

    // Determine return type based on the number of expressions
    if (paths.length === 1) {
        return paths[0];
    } else if (paths.length === 2) {
        return [paths[0], paths[1]];
    } else {
        return paths;
    }
}