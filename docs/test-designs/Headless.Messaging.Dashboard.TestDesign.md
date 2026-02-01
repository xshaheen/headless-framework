# Test Case Design: Headless.Messaging.Dashboard

**Package:** `src/Headless.Messaging.Dashboard`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

Dashboard provides HTTP endpoints for monitoring and managing messages. Contains security-critical code.

| File | Type | Priority |
|------|------|----------|
| `RouteActionProvider.cs` | HTTP endpoints | P1 (Security) |
| `DashboardOptions.cs` | Configuration | P1 (Security) |
| `GatewayProxy/GatewayProxyAgent.cs` | Multi-node proxy | P2 |
| `GatewayProxy/Requester/*.cs` | HTTP client abstractions | P3 |
| `NodeDiscovery/INodeDiscoveryProvider.cs` | Node discovery | P2 |
| `NodeDiscovery/INodeDiscoveryProvider.Consul.cs` | Consul impl | P3 |
| `MessagingCache.cs` | Metrics caching | P2 |
| `CircularBuffer.cs` | History buffer | P2 |
| `HtmlHelper.cs` | HTML generation | P3 |

## Test Recommendation

**Create: `Headless.Messaging.Dashboard.Tests.Unit`**

### Security Tests (CRITICAL)

#### Authentication/Authorization Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_require_auth_when_AllowAnonymous_is_false` | P1 | Default secure |
| `should_allow_anonymous_when_AllowAnonymous_is_true` | P1 | Explicit opt-in |
| `should_apply_authorization_policy` | P1 | Policy enforcement |
| `health_endpoint_should_always_be_anonymous` | P1 | Health check access |
| `ping_endpoint_should_always_be_anonymous` | P1 | Ping access (but see SSRF) |

#### SSRF Prevention Tests (CRITICAL - Known Vulnerability)
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `PingServices_should_reject_internal_ip_ranges` | P1 | Block 10.x, 172.16-31.x, 192.168.x |
| `PingServices_should_reject_localhost` | P1 | Block 127.0.0.1, localhost |
| `PingServices_should_reject_metadata_endpoints` | P1 | Block 169.254.169.254 |
| `PingServices_should_timeout_slow_requests` | P1 | DoS prevention |
| `PingServices_should_sanitize_error_messages` | P1 | No info leakage |

#### Input Validation Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_limit_page_size_to_maximum` | P1 | **BUG: Unbounded perPage** |
| `should_validate_message_id_parameter` | P2 | ID validation |
| `should_validate_status_parameter` | P2 | Status enum validation |
| `should_handle_missing_query_parameters` | P2 | Defaults |

### Endpoint Tests

#### Published Messages Endpoints
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `PublishedList_should_return_paginated_messages` | P1 | Pagination |
| `PublishedList_should_filter_by_status` | P1 | Status filtering |
| `PublishedMessageDetails_should_return_message_content` | P1 | Detail view |
| `PublishedMessageDetails_should_return_404_for_missing` | P2 | Not found |
| `PublishedRequeue_should_reset_message_status` | P1 | Requeue action |
| `PublishedDelete_should_remove_message` | P1 | Delete action |

#### Received Messages Endpoints
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `ReceivedList_should_return_paginated_messages` | P1 | Pagination |
| `ReceivedList_should_filter_by_status` | P1 | Status filtering |
| `ReceivedMessageDetails_should_return_message_content` | P1 | Detail view |
| `ReceivedRequeue_should_reset_for_reprocessing` | P1 | Re-execute |
| `ReceivedDelete_should_remove_message` | P1 | Delete action |

#### Monitoring Endpoints
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `Metrics_should_return_realtime_stats` | P2 | Current metrics |
| `MetricsHistory_should_return_time_series` | P2 | Historical data |
| `Stats_should_return_aggregate_counts` | P2 | Summary stats |
| `Subscribers_should_list_registered_consumers` | P2 | Consumer list |
| `Health_should_return_healthy_status` | P1 | Health check |

#### Gateway Proxy Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_forward_request_to_other_nodes` | P2 | Multi-node |
| `should_aggregate_responses` | P2 | Response merge |
| `should_handle_node_failures_gracefully` | P2 | Resilience |

### DashboardOptions Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `AllowAnonymousExplicit_should_default_to_false` | P1 | **BUG: Currently true** |
| `PathMatch_should_have_sensible_default` | P2 | Default path |
| `should_validate_PathMatch_format` | P2 | Path validation |

### CircularBuffer Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_maintain_fixed_capacity` | P2 | Size limit |
| `should_overwrite_oldest_on_overflow` | P2 | FIFO behavior |
| `should_support_enumeration` | P2 | IEnumerable |

## Integration Tests

**Create: `Headless.Messaging.Dashboard.Tests.Integration`**

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_serve_dashboard_html` | P1 | UI rendering |
| `should_handle_multi_node_discovery` | P2 | K8s/Consul discovery |
| `should_work_with_real_monitoring_api` | P1 | Full stack |

## Test Infrastructure

```csharp
// Use WebApplicationFactory for endpoint tests
public class DashboardTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DashboardTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }
}

// Mock IMonitoringApi for unit tests
var mockMonitoringApi = Substitute.For<IMonitoringApi>();
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | 18 |
| HTTP Endpoints | 16 |
| **Required Unit Tests** | **~50 cases** |
| **Required Integration Tests** | **~10 cases** |
| Priority | P1 - Security critical |

**Critical Security Issues to Test:**
1. SSRF in PingServices (todo #033)
2. Unbounded page size (todo #034)
3. Default anonymous access (todo #005)
