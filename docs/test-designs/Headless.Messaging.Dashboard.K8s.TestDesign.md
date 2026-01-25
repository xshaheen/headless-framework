# Test Case Design: Headless.Messaging.Dashboard.K8s

**Package:** `src/Headless.Messaging.Dashboard.K8s`
**Test Projects:** None ❌ (needs creation)
**Generated:** 2026-01-25

## Package Analysis

Kubernetes service discovery for multi-node dashboard.

| File | Type | Priority |
|------|------|----------|
| (K8s discovery implementation files) | Node discovery | P2 |

## Test Recommendation

**Create: `Headless.Messaging.Dashboard.K8s.Tests.Unit`**

### Unit Tests Needed

#### K8s Node Discovery Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_discover_pods_in_namespace` | P2 | Pod discovery |
| `should_filter_by_label_selector` | P2 | Label filtering |
| `should_use_service_account_token` | P2 | K8s auth |
| `should_handle_api_failure_gracefully` | P2 | Resilience |
| `should_refresh_node_list_periodically` | P2 | Auto-refresh |
| `should_return_pod_endpoints` | P2 | Endpoint extraction |

#### Setup Tests
| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_register_k8s_discovery_provider` | P2 | DI registration |
| `should_configure_k8s_options` | P2 | Options |

### Integration Tests

K8s integration tests require a Kubernetes cluster (kind, minikube, or CI k8s).

| Test Case | Priority | Description |
|-----------|----------|-------------|
| `should_discover_pods_in_real_cluster` | P3 | Real K8s test |

## Test Infrastructure

```csharp
// Mock K8s client for unit tests
var mockK8sClient = Substitute.For<IKubernetesClient>();
mockK8sClient.ListPodForNamespaceAsync(Arg.Any<string>())
    .Returns(new V1PodList { Items = [/* mock pods */] });
```

## Summary

| Metric | Value |
|--------|-------|
| Source Files | ~3-5 |
| **Required Unit Tests** | **~10 cases** (new project) |
| Integration Tests | Optional (requires K8s) |
| Priority | P3 - K8s-specific |

**Note:** This package is only needed for multi-node Kubernetes deployments. Lower priority unless actively using K8s.
