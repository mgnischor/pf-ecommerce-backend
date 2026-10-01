using System.Text.RegularExpressions;

namespace Portfolio.ArchitectureTests;

/// <summary>
/// Guards the container standards (ai/CONTAINERS.md §2, §3, §5) on the files themselves, so a floating tag, a
/// dropped hardening flag, or a published backing service fails the build instead of reaching production.
/// </summary>
public sealed partial class ContainerConventionTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string[] ComposeFiles = ["docker-compose-dev.yml", "docker-compose-prod.yml"];

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    [Fact]
    public void Dockerfile_should_pin_every_base_image_to_tag_and_digest()
    {
        var froms = FromLine().Matches(Read("Dockerfile")).Select(match => match.Groups["image"].Value).ToArray();

        froms.ShouldNotBeEmpty();
        froms.ShouldAllBe(image =>
            DigestPinned().IsMatch(image) && !image.Contains(":latest", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Dockerfile_should_use_the_chiseled_runtime_a_non_root_numeric_user_and_exec_form_entrypoint()
    {
        var dockerfile = Read("Dockerfile");

        dockerfile.ShouldContain("aspnet:10.0.");
        dockerfile.ShouldContain("-noble-chiseled@sha256:");
        dockerfile.ShouldContain("USER $APP_UID");
        dockerfile.ShouldContain("ENTRYPOINT [\"dotnet\", \"Portfolio.dll\"]");
        dockerfile.ShouldContain("--locked-mode");
    }

    [Fact]
    public void Dockerfile_should_never_embed_credentials()
    {
        Read("Dockerfile").ShouldNotMatch(@"(?im)^\s*(ENV|ARG)\s+\S*(PASSWORD|SECRET|TOKEN|KEY)\S*\s*=");
    }

    [Fact]
    public void Dockerignore_should_exclude_secrets_and_non_runtime_content()
    {
        var ignored = Read(".dockerignore").Split('\n').Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);

        ignored.ShouldContain(".git");
        ignored.ShouldContain(".env");
        ignored.ShouldContain("secrets");
        ignored.ShouldContain("*.pem");
        ignored.ShouldContain("ai");
        ignored.ShouldContain("docs");
        ignored.ShouldContain("**/bin");
        ignored.ShouldContain("**/obj");
    }

    [Theory]
    [InlineData("docker-compose-dev.yml")]
    [InlineData("docker-compose-prod.yml")]
    public void Compose_images_should_be_pinned_by_digest_and_never_use_latest(string file)
    {
        var images = ImageLine()
            .Matches(Read(file))
            .Select(match => match.Groups["image"].Value.Trim().Trim('"'))
            .Where(image =>
                !image.StartsWith("${", StringComparison.Ordinal)
                && !image.StartsWith("pf-ecommerce-api", StringComparison.Ordinal)
            )
            .ToArray();

        images.ShouldNotBeEmpty();
        images.ShouldAllBe(image => DigestPinned().IsMatch(image), "Backing-service images are pinned by digest.");
        images.ShouldNotContain(image => image.Contains(":latest", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("docker-compose-dev.yml")]
    [InlineData("docker-compose-prod.yml")]
    public void Compose_should_not_declare_the_obsolete_version_field_or_dangerous_options(string file)
    {
        var text = Read(file);

        text.ShouldNotMatch(@"(?m)^version\s*:");
        text.ShouldNotContain("privileged: true");
        text.ShouldNotContain("docker.sock");
        text.ShouldNotContain("network_mode: host");
        text.ShouldNotContain("pid: host");
    }

    [Theory]
    [InlineData("docker-compose-dev.yml", 5)]
    [InlineData("docker-compose-prod.yml", 9)]
    public void Compose_should_limit_resources_and_define_a_security_context_for_every_service(
        string file,
        int services
    )
    {
        var text = Read(file);

        CountOf(text, @"(?m)^\s+memory:\s+\S+").ShouldBe(services, "every service sets a memory limit");
        CountOf(text, @"(?m)^\s+cpus:\s+""").ShouldBe(services, "every service sets a CPU limit");
        CountOf(text, @"(?m)^\s+pids:\s+\d+").ShouldBe(services, "every service sets a PID limit");
        CountOf(text, @"no-new-privileges:true").ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Production_compose_should_apply_the_hardening_anchor_to_every_service()
    {
        var text = Read("docker-compose-prod.yml");

        CountOf(text, @"(?m)^\s+<<:\s+\*hardening").ShouldBe(9);
        text.ShouldContain("read_only: true");
        text.ShouldContain("cap_drop: [ALL]");
        text.ShouldContain("restart: unless-stopped");
    }

    [Fact]
    public void Production_compose_should_keep_backing_services_off_the_host_and_off_the_internet()
    {
        var text = Read("docker-compose-prod.yml");

        // Only the API and Grafana publish ports, both on a loopback-by-default address.
        CountOf(text, @"(?m)^\s+ports:\s*$").ShouldBe(2);
        text.ShouldNotContain("\"5432:");
        text.ShouldNotContain("\"6379:");
        text.ShouldNotContain("\"5672:");
        text.ShouldNotContain("\"15672:");
        CountOf(text, @"internal:\s+true").ShouldBe(2);
    }

    [Fact]
    public void Production_compose_should_take_credentials_from_secret_files_not_the_environment()
    {
        var text = Read("docker-compose-prod.yml");

        // Environment-style names (UPPER_CASE) may only point at a mounted secret file, never carry a value.
        text.ShouldNotMatch(@"(?m)^\s+[A-Z][A-Z0-9_]*(PASSWORD|SECRET|TOKEN)[A-Z0-9_]*:[ 	]+(?!/run/secrets/)\S");
        text.ShouldNotContain("env_file");
        text.ShouldContain("POSTGRES_PASSWORD_FILE");
        text.ShouldContain("GF_SECURITY_ADMIN_PASSWORD__FILE");
    }

    [Fact]
    public void Compose_files_should_run_the_application_probe_as_the_api_healthcheck()
    {
        foreach (var file in ComposeFiles)
        {
            Read(file).ShouldContain("\"dotnet\", \"Portfolio.dll\", \"--health-check\"");
        }
    }

    [Fact]
    public void Environment_templates_should_hold_only_placeholders_and_secrets_should_be_ignored_by_git()
    {
        Read(".env.example").ShouldContain("change-me");
        Read(".env.prod.example").ShouldContain("replace-with");

        var gitignore = Read(".gitignore");
        gitignore.ShouldContain(".env");
        gitignore.ShouldContain("secrets/");
        gitignore.ShouldContain("*.pem");
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

    [GeneratedRegex(@"(?m)^FROM\s+(?<image>\S+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FromLine();

    [GeneratedRegex(@"(?m)^\s+image:\s*(?<image>\S+)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageLine();

    [GeneratedRegex(@"^[^@\s]+:[^@\s]+@sha256:[0-9a-f]{64}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DigestPinned();
}
