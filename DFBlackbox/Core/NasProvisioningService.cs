namespace DFBlackbox.Core;

public interface INasProvisioningService
{
    Task<NasProvisioningResult> ProvisionAsync(
        string rootFolder,
        string relativePath,
        CancellationToken cancellationToken);
}

public sealed record NasProvisioningResult(bool IsReady, string? ErrorCode = null);

public sealed class NasProvisioningService : INasProvisioningService
{
    private static readonly string[] RequiredFolders = { "live", "recordings", "events", "temp" };

    public async Task<NasProvisioningResult> ProvisionAsync(
        string rootFolder,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            return new NasProvisioningResult(false, "nas_root_not_configured");
        }

        string targetFolder;
        try
        {
            if (!Path.IsPathFullyQualified(rootFolder) || !Directory.Exists(rootFolder))
            {
                return new NasProvisioningResult(false, "nas_root_unavailable");
            }

            targetFolder = ResolveWithinRoot(rootFolder, relativePath);
        }
        catch (ArgumentException)
        {
            return new NasProvisioningResult(false, "invalid_nas_relative_path");
        }
        catch (NotSupportedException)
        {
            return new NasProvisioningResult(false, "invalid_nas_relative_path");
        }

        string? testFile = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(targetFolder);
            foreach (string folder in RequiredFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(targetFolder, folder));
            }

            testFile = Path.Combine(targetFolder, $".dfblackbox-write-test-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(testFile, "DFBlackbox", cancellationToken);
            File.Delete(testFile);
            testFile = null;
            return new NasProvisioningResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return new NasProvisioningResult(false, "nas_access_denied");
        }
        catch (IOException)
        {
            return new NasProvisioningResult(false, "nas_io_error");
        }
        finally
        {
            if (testFile is not null)
            {
                try
                {
                    File.Delete(testFile);
                }
                catch
                {
                }
            }
        }
    }

    internal static string ResolveWithinRoot(string rootFolder, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            throw new ArgumentException("A relative NAS path is required.", nameof(relativePath));
        }

        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootFolder));
        string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullTarget = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelativePath));
        string rootPrefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullTarget.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The NAS path is outside the configured root.", nameof(relativePath));
        }

        return fullTarget;
    }
}
