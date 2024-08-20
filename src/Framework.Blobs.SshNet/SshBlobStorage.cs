using Framework.Arguments;
using Framework.BuildingBlocks.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Framework.Blobs.SshNet;

public sealed class SshBlobStorage : IBlobStorage, IDisposable
{
    private readonly SftpClient _client;
    private readonly ILogger _logger;

    public SshBlobStorage(IOptions<SshBlobStorageOptions> options)
    {
        var sshOptions = options.Value;
        var connectionInfo = _CreateConnectionInfo(sshOptions);
        _client = new SftpClient(connectionInfo);
        _logger = sshOptions.LoggerFactory?.CreateLogger(typeof(SshBlobStorage)) ?? NullLogger.Instance;
    }

    public SftpClient GetClient()
    {
        _EnsureClientConnected();

        return _client;
    }

    public async ValueTask<IReadOnlyList<BlobUploadResult>> BulkUploadAsync(
        IReadOnlyCollection<BlobUploadRequest> blobs,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(container);

        var tasks = blobs.Select(async blob => await UploadAsync(blob, container, cancellationToken));

        // TODO: Task.WhenAll has exception handling issues and should be replaced with a more robust
        //       solution like Polly and handling exceptions in a more controlled manner.
        var result = await Task.WhenAll(tasks);

        return result;
    }

    public async ValueTask<IReadOnlyList<bool>> BulkDeleteAsync(
        IReadOnlyCollection<string> blobNames,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobNames);
        Argument.IsNotNullOrEmpty(container);

        var tasks = blobNames.Select(async fileName => await DeleteAsync(fileName, container, cancellationToken));
        var result = await Task.WhenAll(tasks);

        return result;
    }

    public async ValueTask<BlobUploadResult> UploadAsync(
        BlobUploadRequest blob,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blob);
        Argument.IsNotNull(container);

        var (trustedFileNameForDisplay, uniqueSaveName) = FileHelper.GetTrustedFileNames(blob.FileName);
        var blobPath = _BuildPath(container, uniqueSaveName);

        _logger.LogTrace("Saving {Path}", blobPath);

        _EnsureClientConnected();

        try
        {
            await using var sftpFileStream = await _client.OpenAsync(
                blobPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                cancellationToken
            );

            await blob.Stream.CopyToAsync(sftpFileStream, cancellationToken);

            return new(uniqueSaveName, trustedFileNameForDisplay, blob.Stream.Length);
        }
        catch (SftpPathNotFoundException e)
        {
            _logger.LogDebug(e, "Error saving {Path}: Attempting to create directory", blobPath);
            _CreateDirectory(blobPath);
            _logger.LogTrace("Saving {Path}", blobPath);

            await using var sftpFileStream = await _client.OpenAsync(
                blobPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                cancellationToken
            );

            await blob.Stream.CopyToAsync(sftpFileStream, cancellationToken);

            return new(uniqueSaveName, trustedFileNameForDisplay, blob.Stream.Length);
        }
    }

    public async ValueTask<BlobDownloadResult?> DownloadAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        _EnsureClientConnected();

        var blobPath = _BuildPath(container, blobName);

        _logger.LogTrace("Getting file stream for {Path}", blobPath);

        try
        {
            var stream = await _client.OpenAsync(blobPath, FileMode.Open, FileAccess.Read, cancellationToken);

            return new(stream, blobName);
        }
        catch (SftpPathNotFoundException ex)
        {
            _logger.LogError(ex, "Unable to get file stream for {Path}: File Not Found", blobPath);

            return null;
        }
    }

    public ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] container,
        string? searchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public async ValueTask<bool> RenameFileAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(blobContainer);
        Argument.IsNotNull(newBlobName);
        Argument.IsNotNull(newBlobContainer);

        _EnsureClientConnected();

        var blobPath = _BuildPath(blobContainer, blobName);
        var targetPath = _BuildPath(newBlobContainer, newBlobName);

        _logger.LogInformation("Renaming {Path} to {TargetPath}", blobPath, targetPath);

        if (await ExistsAsync(newBlobName, newBlobContainer, cancellationToken))
        {
            _logger.LogDebug("Removing existing {TargetPath} path for rename operation", targetPath);
            _ = await DeleteAsync(newBlobName, newBlobContainer, cancellationToken);
            _logger.LogDebug("Removed existing {TargetPath} path for rename operation", targetPath);
        }

        try
        {
            await _client.RenameFileAsync(blobPath, targetPath, cancellationToken);
        }
        catch (SftpPathNotFoundException e)
        {
            _logger.LogDebug(
                e,
                "Error renaming {Path} to {NewPath}: Attempting to create directory",
                blobPath,
                targetPath
            );

            _CreateDirectory(targetPath);
            _logger.LogTrace("Renaming {Path} to {NewPath}", blobPath, targetPath);
            await _client.RenameFileAsync(blobPath, targetPath, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error renaming {Path} to {NewPath}", blobPath, targetPath);

            return false;
        }

        return true;
    }

    public async ValueTask<BlobUploadResult?> CopyFileAsync(
        string blobName,
        string[] blobContainer,
        string newBlobName,
        string[] newBlobContainer,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(blobContainer);
        Argument.IsNotNull(newBlobName);
        Argument.IsNotNull(newBlobContainer);

        _EnsureClientConnected();

        _logger.LogInformation(
            "Copying {@Container}/{Path} to {@TargetContainer}/{TargetPath}",
            blobContainer,
            blobName,
            newBlobContainer,
            newBlobName
        );

        try
        {
            var blob = await DownloadAsync(blobName, blobContainer, cancellationToken);

            if (blob is null)
            {
                return null;
            }

            await using var stream = blob.Stream;

            return await UploadAsync(new BlobUploadRequest(stream, newBlobName), newBlobContainer, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(
                e,
                "Error copying {@Container}/{Path} to {@TargetContainer}/{TargetPath}",
                blobContainer,
                blobName,
                newBlobContainer,
                newBlobName
            );

            return null;
        }
    }

    public ValueTask<bool> ExistsAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        _EnsureClientConnected();

        var blobPath = _BuildPath(container, blobName);
        _logger.LogTrace("Checking if {Path} exists", blobPath);
        var exists = _client.Exists(blobPath);

        return ValueTask.FromResult(exists);
    }

    public async ValueTask<bool> DeleteAsync(
        string blobName,
        string[] container,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        _EnsureClientConnected();
        var blobPath = _BuildPath(container, blobName);
        _logger.LogTrace("Deleting {Path}", blobPath);

        try
        {
            await _client.DeleteFileAsync(blobPath, cancellationToken);
        }
        catch (SftpPathNotFoundException ex)
        {
            _logger.LogError(ex, "Unable to delete {Path}: File not found", blobPath);

            return false;
        }

        return true;
    }

    #region Helpers

    private void _CreateDirectory(string directory)
    {
        _logger.LogTrace("Ensuring {Directory} directory exists", directory);
        var folderSegments = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentDirectory = string.Empty;

        foreach (var segment in folderSegments)
        {
            // If the current directory is empty, use the current working directory instead of a rooted path.
            currentDirectory = string.IsNullOrEmpty(currentDirectory)
                ? segment
                : string.Concat(currentDirectory, "/", segment);

            if (_client.Exists(currentDirectory))
            {
                continue;
            }

            _logger.LogInformation("Creating {Directory} directory", directory);
            _client.CreateDirectory(currentDirectory);
        }
    }

    private async Task<int> _DeleteDirectoryAsync(
        string directory,
        bool includeSelf = true,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Deleting {Directory} directory", directory);

        var count = 0;

        await foreach (var file in _client.ListDirectoryAsync(directory, cancellationToken))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            if (file.IsDirectory)
            {
                count += await _DeleteDirectoryAsync(file.FullName, true, cancellationToken);
            }
            else
            {
                _logger.LogTrace("Deleting file {Path}", file.FullName);
                await _client.DeleteFileAsync(file.FullName, cancellationToken);
                count++;
            }
        }

        if (includeSelf)
        {
            _client.DeleteDirectory(directory);
        }

        _logger.LogTrace("Finished deleting {Directory} directory with {FileCount} files", directory, count);

        return count;
    }

    private void _EnsureClientConnected()
    {
        if (_client.IsConnected)
        {
            return;
        }

        _logger.LogTrace("Connecting to {Host}:{Port}", _client.ConnectionInfo.Host, _client.ConnectionInfo.Port);

        _client.Connect();

        _logger.LogTrace(
            "Connected to {Host}:{Port} in {WorkingDirectory}",
            _client.ConnectionInfo.Host,
            _client.ConnectionInfo.Port,
            _client.WorkingDirectory
        );
    }

    private static string _BuildPath(string[] container, string uniqueSaveName)
    {
        var containerPath = string.Join("/", container.Concat(container));

        return containerPath + "/" + uniqueSaveName;
    }

    private static ConnectionInfo _CreateConnectionInfo(SshBlobStorageOptions options)
    {
        Argument.IsNotNull(options);

        if (
            !Uri.TryCreate(options.ConnectionString, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.UserInfo)
        )
        {
            throw new InvalidOperationException("Unable to parse connection string uri");
        }

        var userParts = uri.UserInfo.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var username = Uri.UnescapeDataString(userParts.First());
        var password = Uri.UnescapeDataString(userParts.Length > 1 ? userParts[1] : string.Empty);
        var port = uri.Port > 0 ? uri.Port : 22;

        var authenticationMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrEmpty(password))
        {
            authenticationMethods.Add(new PasswordAuthenticationMethod(username, password));
        }

        if (options.PrivateKey != null)
        {
            authenticationMethods.Add(
                new PrivateKeyAuthenticationMethod(
                    username,
                    new PrivateKeyFile(options.PrivateKey, options.PrivateKeyPassPhrase)
                )
            );
        }

        if (authenticationMethods.Count == 0)
        {
            authenticationMethods.Add(new NoneAuthenticationMethod(username));
        }

        if (string.IsNullOrEmpty(options.Proxy))
        {
            return new ConnectionInfo(uri.Host, port, username, [.. authenticationMethods]);
        }

        if (
            !Uri.TryCreate(options.Proxy, UriKind.Absolute, out var proxyUri) || string.IsNullOrEmpty(proxyUri.UserInfo)
        )
        {
            throw new InvalidOperationException("Unable to parse proxy uri");
        }

        var proxyParts = proxyUri.UserInfo.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var proxyUsername = proxyParts.First();
        var proxyPassword = proxyParts.Length > 1 ? proxyParts[1] : null;

        var proxyType = options.ProxyType;

        if (
            proxyType == ProxyTypes.None
            && proxyUri.Scheme != null
            && proxyUri.Scheme.StartsWith("http", StringComparison.Ordinal)
        )
        {
            proxyType = ProxyTypes.Http;
        }

        return new ConnectionInfo(
            uri.Host,
            port,
            username,
            proxyType,
            proxyUri.Host,
            proxyUri.Port,
            proxyUsername,
            proxyPassword,
            [.. authenticationMethods]
        );
    }

    public void Dispose()
    {
        if (_client.IsConnected)
        {
            _logger.LogTrace(
                "Disconnecting from {Host}:{Port}",
                _client.ConnectionInfo.Host,
                _client.ConnectionInfo.Port
            );

            _client.Disconnect();

            _logger.LogTrace(
                "Disconnected from {Host}:{Port}",
                _client.ConnectionInfo.Host,
                _client.ConnectionInfo.Port
            );
        }

        _client.Dispose();
    }

    #endregion
}
