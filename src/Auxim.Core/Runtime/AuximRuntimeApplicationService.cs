using System.Diagnostics;
using System.Text.Json;
using Auxim.Core.Approval;
using Auxim.Core.Config;
using Auxim.VAFS;

namespace Auxim.Core.Runtime;

public sealed partial class AuximRuntimeService
{
    private static readonly JsonSerializerOptions ApplicationJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public AuximApplicationPaths GetApplicationPaths()
    {
        var home = _homeDirectory();
        return new AuximApplicationPaths(
            home,
            Path.Combine(home, "config.json"),
            Path.Combine(home, ".env"),
            Path.Combine(home, "history"),
            Path.Combine(home, "logs", "agent.log"));
    }

    public string GetConfigJson() =>
        JsonSerializer.Serialize(_configLoader(), ApplicationJsonOptions);

    public void SetConfigValue(string keyPath, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentNullException.ThrowIfNull(value);
        ConfigLoader.SetValue(keyPath, value, GetApplicationPaths().ConfigPath);
    }

    public AuximModelSettings GetModelSettings()
    {
        var config = _configLoader();
        var paths = GetApplicationPaths();
        return new AuximModelSettings(
            config.Model.Provider,
            config.Model.Name,
            config.Model.BaseUrl,
            paths.ConfigPath,
            paths.SecretsPath);
    }

    public AuximModelSettings SetModelSettings(string provider, string model, string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var config = _configLoader();
        var updated = new AuximConfig
        {
            Model = new ModelConfig
            {
                Provider = provider.Trim(),
                Name = model.Trim(),
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/'),
            },
            Agent = config.Agent,
            Display = config.Display,
            Sandbox = config.Sandbox,
        };
        ConfigLoader.Save(updated, GetApplicationPaths().ConfigPath);
        return new AuximModelSettings(
            updated.Model.Provider,
            updated.Model.Name,
            updated.Model.BaseUrl,
            GetApplicationPaths().ConfigPath,
            GetApplicationPaths().SecretsPath);
    }

    public AuximCredentialStatus GetCredentialStatus(string? provider = null)
    {
        provider = string.IsNullOrWhiteSpace(provider) ? _configLoader().Model.Provider : provider.Trim();
        var keyName = ProviderCatalog.ApiKeyNameForProvider(provider);
        var required = ProviderCatalog.RequiresApiKey(provider);
        var secretsPath = GetApplicationPaths().SecretsPath;
        var configured = !required
            || DotEnvStore.HasValue(keyName, secretsPath)
            || DotEnvStore.HasValue("AUXIM_API_KEY", secretsPath);
        return new AuximCredentialStatus(provider, keyName, required, configured, secretsPath);
    }

    public void SetApiKey(string provider, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        DotEnvStore.SetValue(
            ProviderCatalog.ApiKeyNameForProvider(provider),
            apiKey,
            GetApplicationPaths().SecretsPath);
    }

    public IReadOnlyList<ApprovalGrant> ListApprovalGrants() =>
        ApprovalService().ListGrants();

    public void ClearApprovalGrants() =>
        ApprovalService().ClearGrants();

    public bool RevokeApprovalGrant(string grantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);
        return ApprovalService().RevokeGrant(grantId);
    }

    public AuximSandboxStatus GetSandboxStatus()
    {
        var vafs = VirtualAgentFileSystem.FromEnvironment();
        var mounts = vafs.ListMounts();
        var workspace = mounts.Single(mount => mount.Name == "workspace");
        var temp = mounts.Single(mount => mount.Name == "tmp");
        return new AuximSandboxStatus(
            GetApplicationPaths().ConfigPath,
            workspace.HostPath,
            temp.HostPath,
            mounts.Where(mount => mount.Name is not "workspace" and not "tmp")
                .Select(mount => new AuximSandboxMount(
                    mount.Name,
                    mount.VirtualPath,
                    mount.HostPath,
                    mount.ReadOnly))
                .OrderBy(mount => mount.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            vafs.DescribeForAgent());
    }

    public AuximSandboxStatus SetSandboxWorkspace(string hostPath)
    {
        var fullPath = ValidateHostDirectory(hostPath);
        var config = _configLoader();
        SaveSandbox(config, new SandboxConfig
        {
            Workspace = fullPath,
            Mounts = config.Sandbox.Mounts,
            Shell = config.Sandbox.Shell,
        });
        return GetSandboxStatus();
    }

    public AuximSandboxStatus MountSandboxVolume(string name, string hostPath, bool readOnly = false)
    {
        if (!IsValidMountName(name))
        {
            throw new ArgumentException(
                "Mount name must contain only letters, digits, '-', or '_'.",
                nameof(name));
        }

        var fullPath = ValidateHostDirectory(hostPath);
        var config = _configLoader();
        var mounts = config.Sandbox.Mounts
            .Where(mount => !mount.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        mounts.Add(new SandboxMountConfig
        {
            Name = name,
            HostPath = fullPath,
            ReadOnly = readOnly,
        });
        SaveSandbox(config, new SandboxConfig
        {
            Workspace = config.Sandbox.Workspace,
            Mounts = mounts,
            Shell = config.Sandbox.Shell,
        });
        return GetSandboxStatus();
    }

    public bool UnmountSandboxVolume(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var config = _configLoader();
        var mounts = config.Sandbox.Mounts
            .Where(mount => !mount.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (mounts.Count == config.Sandbox.Mounts.Count)
        {
            return false;
        }

        SaveSandbox(config, new SandboxConfig
        {
            Workspace = config.Sandbox.Workspace,
            Mounts = mounts,
            Shell = config.Sandbox.Shell,
        });
        return true;
    }

    public AuximDiagnostics GetDiagnostics()
    {
        var paths = GetApplicationPaths();
        var model = GetModelSettings();
        var sandbox = GetSandboxStatus();
        return new AuximDiagnostics(
            paths,
            File.Exists(paths.ConfigPath),
            File.Exists(paths.SecretsPath),
            model,
            GetCredentialStatus(model.Provider),
            ListTools().Count,
            "/workspace",
            sandbox.Mounts.Count,
            ListSessions().Count,
            "VAShell policy");
    }

    public IReadOnlyList<string> LoadInputHistory()
    {
        try
        {
            var path = GetApplicationPaths().HistoryPath;
            return File.Exists(path) ? File.ReadAllLines(path) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void SaveInputHistory(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        try
        {
            var path = GetApplicationPaths().HistoryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllLines(path, entries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task<int> RunHostCommandAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        using var process = new Process { StartInfo = CreateHostCommandStartInfo(command) };
        process.Start();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private ToolApprovalService ApprovalService() => new(home: _homeDirectory());

    private void SaveSandbox(AuximConfig config, SandboxConfig sandbox) =>
        ConfigLoader.Save(new AuximConfig
        {
            Model = config.Model,
            Agent = config.Agent,
            Display = config.Display,
            Sandbox = sandbox,
        }, GetApplicationPaths().ConfigPath);

    private static string ValidateHostDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(ExpandHome(path));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
        }

        return fullPath;
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;
    }

    private static bool IsValidMountName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
        && name is not "." and not "..";

    private static ProcessStartInfo CreateHostCommandStartInfo(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
                ArgumentList = { "/C", command },
            };
        }

        var shell = Environment.GetEnvironmentVariable("SHELL");
        return new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(shell) ? "/bin/sh" : shell,
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
            ArgumentList = { "-lc", command },
        };
    }
}
