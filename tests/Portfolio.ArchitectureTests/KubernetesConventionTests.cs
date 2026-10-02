using System.Text.RegularExpressions;

namespace Portfolio.ArchitectureTests;

/// <summary>
/// Guards the Kubernetes standards (ai/CONTAINERS.md §3, §6, §7, §12) on the manifests themselves, so a plain Secret, a
/// host path, a floating tag, a weakened security context or an overlay that forgot to pin its image fails the build.
/// The rendered output is validated separately in CI (kustomize + kubeconform).
/// </summary>
public sealed partial class KubernetesConventionTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string Kubernetes = Path.Combine(Root, "kubernetes");
    private static readonly string[] Environments = ["development", "staging", "production"];
    private static readonly string[] Parts = ["foundation", "migrations", "workloads"];

    private static IEnumerable<string> Manifests() =>
        Directory.EnumerateFiles(Kubernetes, "*.yaml", SearchOption.AllDirectories);

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Kubernetes, relativePath));

    // The manifest without its comments, which explain what is absent and would trip a "does not contain" check.
    private static string Code(string relativePath) =>
        WithoutComments(File.ReadAllLines(Path.Combine(Kubernetes, relativePath)));

    private static string WithoutComments(IEnumerable<string> lines) =>
        string.Join(Environment.NewLine, lines.Where(line => !line.TrimStart().StartsWith('#')));

    private static string Relative(string path) => Path.GetRelativePath(Kubernetes, path).Replace('\\', '/');

    public static TheoryData<string> WorkloadFiles() =>
        [
            "base/app/api-deployment.yaml",
            "base/app/worker-deployment.yaml",
            "base/otel-collector/deployment.yaml",
            "migrations/db-release-job.yaml",
        ];

    public static TheoryData<string, string> Overlays()
    {
        var data = new TheoryData<string, string>();
        foreach (var environment in Environments)
        {
            foreach (var part in Parts)
            {
                data.Add(environment, part);
            }
        }

        return data;
    }

    [Fact]
    public void Manifests_should_exist_and_never_contain_a_plain_secret()
    {
        var manifests = Manifests().ToArray();

        manifests.ShouldNotBeEmpty();
        manifests
            .Where(path => PlainSecret().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .ShouldBeEmpty(
                "Secrets come from External Secrets Operator; no Secret value is committed (ai/CONTAINERS.md §6.4)."
            );
    }

    [Fact]
    public void Manifests_should_never_use_host_namespaces_ports_or_paths()
    {
        Manifests().Where(path => HostAccess().IsMatch(File.ReadAllText(path))).Select(Relative).ShouldBeEmpty();
    }

    [Fact]
    public void Images_should_never_use_a_floating_tag()
    {
        var images = Manifests()
            .SelectMany(path =>
                ImageLine()
                    .Matches(File.ReadAllText(path))
                    .Select(match => (File: Relative(path), match.Groups["image"].Value))
            )
            .ToArray();

        images.ShouldNotBeEmpty();
        images.Where(image => image.Value.EndsWith(":latest", StringComparison.Ordinal)).ShouldBeEmpty();
        // Third-party images are pinned in the base; our own are pinned by digest in each overlay (see below).
        images
            .Where(image => !image.Value.StartsWith("registry.example.com/", StringComparison.Ordinal))
            .ShouldAllBe(image => DigestPinned().IsMatch(image.Value));
    }

    [Theory]
    [MemberData(nameof(WorkloadFiles))]
    public void A_workload_should_run_non_root_with_a_read_only_filesystem_and_no_capabilities(string file)
    {
        var text = Read(file);

        text.ShouldContain("runAsNonRoot: true");
        text.ShouldContain("allowPrivilegeEscalation: false");
        text.ShouldContain("readOnlyRootFilesystem: true");
        text.ShouldContain("drop: [\"ALL\"]");
        text.ShouldContain("type: RuntimeDefault");
        text.ShouldContain("automountServiceAccountToken: false");
        text.ShouldContain("requests:");
        text.ShouldContain("limits:");
        text.ShouldContain("ephemeral-storage");
    }

    [Theory]
    [InlineData("base/app/api-deployment.yaml")]
    [InlineData("base/app/worker-deployment.yaml")]
    [InlineData("base/otel-collector/deployment.yaml")]
    public void A_long_running_workload_should_define_all_three_probes(string file)
    {
        var text = Read(file);

        text.ShouldContain("startupProbe:");
        text.ShouldContain("livenessProbe:");
        text.ShouldContain("readinessProbe:");
    }

    [Theory]
    [InlineData("base/app/api-deployment.yaml")]
    [InlineData("base/app/worker-deployment.yaml")]
    public void The_application_roles_should_drain_gracefully_and_roll_without_downtime(string file)
    {
        var text = Read(file);

        text.ShouldContain("terminationGracePeriodSeconds: 60");
        text.ShouldContain("preStop:");
        text.ShouldContain("maxUnavailable: 0");
        text.ShouldContain("path: /health/live");
        text.ShouldContain("path: /health/ready");
        text.ShouldContain("emptyDir:");
    }

    [Fact]
    public void The_worker_role_should_enable_the_relays_and_the_consumers_and_the_api_should_not()
    {
        Read("base/app/worker-deployment.yaml").ShouldContain("Outbox__Relay__Enabled");
        Read("base/app/worker-deployment.yaml").ShouldContain("RabbitMq__ConsumersEnabled");
        Read("base/app/api-deployment.yaml").ShouldNotContain("Outbox__Relay__Enabled");
        Read("base/app/api-deployment.yaml").ShouldNotContain("RabbitMq__ConsumersEnabled");
    }

    [Fact]
    public void The_worker_should_not_receive_the_bootstrap_account_secret()
    {
        Read("base/app/api-deployment.yaml").ShouldContain("ecommerce-api-bootstrap");
        Read("base/app/worker-deployment.yaml").ShouldNotContain("ecommerce-api-bootstrap");
    }

    [Fact]
    public void Every_namespace_should_deny_traffic_by_default()
    {
        var policies = Code("base/app/network-policies.yaml");

        policies.ShouldContain("name: default-deny-all");
        policies.ShouldContain("policyTypes: [Ingress, Egress]");
        policies.ShouldContain("podSelector: {}");
        policies.ShouldNotContain("0.0.0.0/0");
        Manifests()
            .Where(path => WithoutComments(File.ReadAllLines(path)).Contains("0.0.0.0/0", StringComparison.Ordinal))
            .Select(Relative)
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_ingress_should_list_explicit_routes_and_never_expose_probes_docs_or_diagnostics()
    {
        var ingress = Read("base/app/ingress.yaml");

        ingress.ShouldContain("tls:");
        ingress.ShouldContain("path: /api/v1/auth");
        ingress.ShouldNotContain("path: /api/v1,");
        ingress.ShouldNotContain("path: /,");
        foreach (var forbidden in new[] { "/health", "/api/v1/docs", "/api/v1/openapi", "/api/v1/diagnostics" })
        {
            ingress.ShouldNotContain($"path: {forbidden}");
        }
    }

    [Fact]
    public void Secrets_should_be_read_through_the_environment_secret_store_only()
    {
        var externalSecrets = Manifests().Where(path => ExternalSecretKind().IsMatch(File.ReadAllText(path))).ToArray();

        externalSecrets.ShouldNotBeEmpty();
        foreach (var path in externalSecrets)
        {
            var text = File.ReadAllText(path);
            CountOf(text, @"(?m)^kind:\s*ExternalSecret\s*$")
                .ShouldBe(CountOf(text, @"(?m)^\s+name:\s*ecommerce-secrets\s*$"), Relative(path));
        }
    }

    [Theory]
    [MemberData(nameof(Overlays))]
    public void Every_overlay_part_should_exist_and_build_in_its_environments_namespace(string environment, string part)
    {
        var file = Read($"overlays/{environment}/{part}/kustomization.yaml");

        if (part != "foundation")
        {
            var expected = environment == "development" ? "ecommerce-dev" : $"ecommerce-{environment}";
            file.ShouldContain($"namespace: {expected}");
        }
    }

    [Theory]
    [InlineData("development", "ecommerce-dev")]
    [InlineData("staging", "ecommerce-staging")]
    [InlineData("production", "ecommerce-production")]
    public void An_environment_namespace_should_enforce_the_restricted_pod_security_standard_and_have_quotas(
        string environment,
        string name
    )
    {
        var text = Read($"overlays/{environment}/foundation/namespace.yaml");

        text.ShouldContain($"name: {name}");
        text.ShouldContain("pod-security.kubernetes.io/enforce: restricted");
        text.ShouldContain($"environment: {environment}");
        text.ShouldContain("kind: ResourceQuota");
        text.ShouldContain("kind: LimitRange");
        Read($"overlays/{environment}/foundation/secret-store.yaml")
            .ShouldContain($"prefix: pf-ecommerce-{environment}/");
    }

    [Theory]
    [InlineData("workloads", "registry.example.com/ecommerce-api")]
    [InlineData("migrations", "registry.example.com/ecommerce-migrations")]
    public void Every_overlay_should_pin_our_images_by_digest(string part, string image)
    {
        foreach (var environment in Environments)
        {
            var text = Read($"overlays/{environment}/{part}/kustomization.yaml");

            text.ShouldContain($"- name: {image}");
            text.ShouldMatch(@"digest:\s*sha256:[0-9a-f]{64}");
        }
    }

    [Fact]
    public void Production_should_run_at_least_two_replicas_of_the_api_and_a_budget_for_each_workload()
    {
        Read("base/app/pod-disruption-budgets.yaml").ShouldContain("name: ecommerce-api");
        Read("base/app/pod-disruption-budgets.yaml").ShouldContain("name: ecommerce-worker");
        CountOf(Read("overlays/production/workloads/kustomization.yaml"), @"path: /spec/minReplicas\s+value: 3\b")
            .ShouldBe(1);
        Read("base/app/api-hpa.yaml").ShouldContain("stabilizationWindowSeconds: 300");
    }

    [Fact]
    public void The_database_release_should_run_as_a_bounded_job_before_the_workloads()
    {
        var job = Read("migrations/db-release-job.yaml");

        job.ShouldContain("kind: Job");
        job.ShouldContain("activeDeadlineSeconds:");
        job.ShouldContain("ttlSecondsAfterFinished:");
        job.ShouldContain("backoffLimit:");
        job.ShouldContain("restartPolicy: Never");
        job.ShouldContain("/migrations/migrate-identity");
        job.ShouldContain("/migrations/migrate-catalog");
        job.ShouldContain("/migrations/migrate-inventory");
        Code("base/kustomization.yaml").ShouldNotContain("migrations");
    }

    [Fact]
    public void Configuration_should_be_generated_with_a_content_hash_and_be_immutable()
    {
        Read("base/app/kustomization.yaml").ShouldContain("immutable: true");
        Read("base/app/kustomization.yaml").ShouldContain("configMapGenerator:");
        Read("migrations/kustomization.yaml").ShouldContain("immutable: true");
        Read("base/app/kustomization.yaml").ShouldNotContain("disableNameSuffixHash");
    }

    private static int CountOf(string text, string pattern) =>
        Regex.Count(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(2));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Portfolio.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Portfolio.csproj not found above the test binaries.");
    }

    [GeneratedRegex(@"(?m)^kind:\s*Secret\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlainSecret();

    [GeneratedRegex(@"(?m)^kind:\s*ExternalSecret\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExternalSecretKind();

    [GeneratedRegex(
        @"(?m)^\s*(?:hostNetwork|hostPID|hostIPC|hostPort|hostPath)\s*:",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex HostAccess();

    [GeneratedRegex(@"(?m)^\s+(?:- )?image:\s*(?<image>\S+)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageLine();

    [GeneratedRegex(@"^[^@\s]+:[^@\s]+@sha256:[0-9a-f]{64}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigestPinned();
}
