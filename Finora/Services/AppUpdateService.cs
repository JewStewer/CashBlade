using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Finora.Services;

public sealed class AppUpdateService
{
    private const string ManifestUrlEnvironmentVariable = "CASHGLADE_UPDATE_MANIFEST_URL";
    private const string LegacyManifestUrlEnvironmentVariable = "FINORA_UPDATE_MANIFEST_URL";
    private const string AppName = "Cashglade";
    private const string LocalConfigFileName = "Finora.updater.json";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    public async Task<bool> TryStartUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestUrl = await GetManifestUrlAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                return false;
            }

            var manifest = await DownloadJsonAsync<UpdateManifest>(manifestUrl, cancellationToken);
            if (manifest is null || !await IsUpdateAvailableAsync(manifest, cancellationToken))
            {
                return false;
            }

            var installerPath = await DownloadUpdateAsync(manifest, cancellationToken);
            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                await VerifySha256Async(installerPath, manifest.Sha256, cancellationToken);
            }

            StartUpdate(installerPath, manifest.InstallerArguments);
            return true;
        }
        catch (Exception ex)
        {
            await LogFailureAsync(ex);
            return false;
        }
    }

    private static async Task<string?> GetManifestUrlAsync(CancellationToken cancellationToken)
    {
        var environmentUrl = Environment.GetEnvironmentVariable(ManifestUrlEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable(LegacyManifestUrlEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentUrl))
        {
            return environmentUrl;
        }

        foreach (var configPath in GetConfigPaths())
        {
            if (!File.Exists(configPath))
            {
                continue;
            }

            var config = await ReadJsonFileAsync<UpdaterConfig>(configPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(config?.ManifestUrl))
            {
                return config.ManifestUrl;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetConfigPaths()
    {
        yield return Path.Combine(GetAppDataDirectory(), LocalConfigFileName);
    }

    private static async Task<T?> ReadJsonFileAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions.Default, cancellationToken);
    }

    private static async Task<T?> DownloadJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        if (TryGetLocalPath(url, out var localPath))
        {
            return await ReadJsonFileAsync<T>(localPath, cancellationToken);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsHttpUri(uri))
        {
            throw new InvalidOperationException("The update manifest URL must be an absolute HTTP/HTTPS URL, file URL, or local file path.");
        }

        using var httpClient = CreateHttpClient();
        await using var stream = await httpClient.GetStreamAsync(uri, cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions.Default, cancellationToken);
    }

    private static async Task<string> DownloadUpdateAsync(UpdateManifest manifest, CancellationToken cancellationToken)
    {
        var updateUrl = manifest.ExecutableUrl ?? manifest.InstallerUrl;
        if (string.IsNullOrWhiteSpace(updateUrl))
        {
            throw new InvalidOperationException("The update URL must not be blank.");
        }

        var extension = GetUpdateExtension(updateUrl);
        if (!string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{AppName} updates must be MSI installers or EXE app updates.");
        }

        var downloadDirectory = Path.Combine(GetAppDataDirectory(), "Updates");
        Directory.CreateDirectory(downloadDirectory);

        var version = SanitizeFileName(manifest.Version);
        var installerPath = Path.Combine(downloadDirectory, $"{AppName}-{version}{extension}");

        if (TryGetLocalPath(updateUrl, out var localPath))
        {
            File.Copy(localPath, installerPath, overwrite: true);
            return installerPath;
        }

        if (!Uri.TryCreate(updateUrl, UriKind.Absolute, out var uri) || !IsHttpUri(uri))
        {
            throw new InvalidOperationException("The update URL must be an absolute HTTP/HTTPS URL, file URL, or local file path.");
        }

        using var httpClient = CreateHttpClient();
        await using var remoteStream = await httpClient.GetStreamAsync(uri, cancellationToken);
        await using var localStream = File.Create(installerPath);
        await remoteStream.CopyToAsync(localStream, cancellationToken);

        return installerPath;
    }

    private static string GetUpdateExtension(string updateUrl)
    {
        if (TryGetLocalPath(updateUrl, out var localPath))
        {
            return Path.GetExtension(localPath);
        }

        return Uri.TryCreate(updateUrl, UriKind.Absolute, out var uri)
            ? Path.GetExtension(uri.LocalPath)
            : Path.GetExtension(updateUrl);
    }

    private static bool TryGetLocalPath(string value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = uri.LocalPath;
            return true;
        }

        if (Path.IsPathFullyQualified(value))
        {
            path = value;
            return true;
        }

        return false;
    }

    private static async Task VerifySha256Async(string filePath, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualSha256 = Convert.ToHexString(hash);

        if (!string.Equals(actualSha256, NormalizeSha256(expectedSha256), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The downloaded update did not match the expected SHA-256 hash.");
        }
    }

    private static void StartUpdate(string updatePath, string? installerArguments)
    {
        var extension = Path.GetExtension(updatePath);
        ProcessStartInfo startInfo;

        if (string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = string.IsNullOrWhiteSpace(installerArguments)
                ? $"/i \"{updatePath}\" /passive /norestart"
                : $"/i \"{updatePath}\" {installerArguments}";

            startInfo = new ProcessStartInfo("msiexec.exe", arguments);
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
            return;
        }

        StartExecutableReplacement(updatePath);
    }

    private static void StartExecutableReplacement(string updatePath)
    {
        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
        {
            throw new InvalidOperationException("The current executable path could not be found.");
        }

        var scriptPath = Path.Combine(GetAppDataDirectory(), "Updates", $"Apply{AppName}Update.cmd");
        var processId = Environment.ProcessId;
        var script = $$"""
            @echo off
            setlocal
            set "SOURCE={{updatePath}}"
            set "TARGET={{currentExePath}}"
            :wait
            tasklist /FI "PID eq {{processId}}" 2>NUL | find "{{processId}}" >NUL
            if not errorlevel 1 (
                timeout /t 1 /nobreak >NUL
                goto wait
            )
            copy /Y "%SOURCE%" "%TARGET%" >NUL
            start "" "%TARGET%"
            del "%~f0"
            """;

        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{scriptPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }

    private static async Task<bool> IsUpdateAvailableAsync(UpdateManifest manifest, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version) || !Version.TryParse(manifest.Version, out var availableVersion))
        {
            return false;
        }

        var currentVersion = GetCurrentVersion();
        if (availableVersion > currentVersion)
        {
            return true;
        }

        if (availableVersion < currentVersion || string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            return false;
        }

        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
        {
            return false;
        }

        await using var stream = File.OpenRead(currentExePath);
        var currentHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return !string.Equals(currentHash, NormalizeSha256(manifest.Sha256), StringComparison.OrdinalIgnoreCase);
    }

    private static Version GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = HttpTimeout
        };
    }

    private static bool IsHttpUri(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAppDataDirectory()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, AppName);
    }

    private static string SanitizeFileName(string? value)
    {
        var fileName = string.IsNullOrWhiteSpace(value) ? "update" : value;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '-');
        }

        return fileName;
    }

    private static string NormalizeSha256(string value)
    {
        return value.Replace(" ", string.Empty).Replace("-", string.Empty);
    }

    private static async Task LogFailureAsync(Exception exception)
    {
        try
        {
            var appDataDirectory = GetAppDataDirectory();
            Directory.CreateDirectory(appDataDirectory);

            var message = $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}";
            await File.AppendAllTextAsync(Path.Combine(appDataDirectory, "updater.log"), message);
        }
        catch
        {
            // Update failures should never prevent the app from launching.
        }
    }

    private sealed class UpdaterConfig
    {
        public string? ManifestUrl { get; set; }
    }

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }

        public string? InstallerUrl { get; set; }

        public string? ExecutableUrl { get; set; }

        public string? Sha256 { get; set; }

        public string? InstallerArguments { get; set; }
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
