// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Framework.Kernel.BuildingBlocks;
using Framework.Kernel.Checks;
using Framework.Kernel.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Framework.Blobs.SshNet;

public sealed class SshBlobStorage : IBlobStorage
{
    private readonly SftpClient _client;
    private readonly ILogger _logger;

    public SshBlobStorage(IOptions<SshBlobStorageSettings> options)
    {
        var sshOptions = options.Value;
        var connectionInfo = _BuildConnectionInfo(sshOptions);
        _client = new SftpClient(connectionInfo);
        _logger = sshOptions.LoggerFactory?.CreateLogger(typeof(SshBlobStorage)) ?? NullLogger.Instance;
    }

    #region Get Client

    public async ValueTask<SftpClient> GetClient(CancellationToken cancellationToken)
    {
        await _EnsureClientConnectedAsync(cancellationToken);

        return _client;
    }

    #endregion

    #region Create Container

    public async ValueTask CreateContainerAsync(string[] container, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(container);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogTrace("Ensuring {Directory} directory exists", (object)container);

        var currentDirectory = string.Empty;

        foreach (var segment in container)
        {
            // If the current directory is empty, use the current working directory instead of a rooted path.
            currentDirectory = string.IsNullOrEmpty(currentDirectory) ? segment : $"{currentDirectory}/{segment}";

            if (await _client.ExistsAsync(currentDirectory, cancellationToken))
            {
                continue;
            }

            _logger.LogInformation("Creating Container segment {Segment}", segment);
            await _client.CreateDirectoryAsync(currentDirectory, cancellationToken);
        }
    }

    #endregion

    #region Upload

    public async ValueTask UploadAsync(
        string[] container,
        BlobUploadRequest blob,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blob);
        Argument.IsNotNull(container);

        var blobPath = _BuildBlobPath(container, blob.FileName);

        _logger.LogTrace("Saving {Path}", blobPath);

        await _EnsureClientConnectedAsync(cancellationToken);

        try
        {
            await using var sftpFileStream = await _client.OpenAsync(
                blobPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                cancellationToken
            );

            await blob.Stream.CopyToAsync(sftpFileStream, cancellationToken);
        }
        catch (SftpPathNotFoundException e)
        {
            _logger.LogDebug(e, "Error saving {Path}: Attempting to create directory", blobPath);
            await CreateContainerAsync(container, cancellationToken);

            _logger.LogTrace("Saving {Path}", blobPath);

            await using var sftpFileStream = await _client.OpenAsync(
                blobPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                cancellationToken
            );

            await blob.Stream.CopyToAsync(sftpFileStream, cancellationToken);
        }
    }

    #endregion

    #region Bulk Upload

    public async ValueTask<IReadOnlyList<Result<Exception>>> BulkUploadAsync(
        string[] container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobs);
        Argument.IsNotNullOrEmpty(container);

        var tasks = blobs.Select(async blob =>
        {
            try
            {
                await UploadAsync(container, blob, cancellationToken);

                return Result<Exception>.Success();
            }
            catch (Exception e)
            {
                return Result<Exception>.Fail(e);
            }
        });

        return await Task.WhenAll(tasks).WithAggregatedExceptions();
    }

    #endregion

    #region Delete

    public async ValueTask<bool> DeleteAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        await _EnsureClientConnectedAsync(cancellationToken);

        var blobPath = _BuildBlobPath(container, blobName);

        _logger.LogTrace("Deleting {Path}", blobPath);

        return await _DeleteAsync(blobPath, cancellationToken);
    }

    private async Task<bool> _DeleteAsync(string blobPath, CancellationToken cancellationToken)
    {
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

    #endregion

    #region Bulk Delete

    public async ValueTask<IReadOnlyList<Result<bool, Exception>>> BulkDeleteAsync(
        string[] container,
        IReadOnlyCollection<string> blobNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(blobNames);
        Argument.IsNotNullOrEmpty(container);

        var tasks = blobNames.Select(async fileName =>
        {
            try
            {
                return await DeleteAsync(container, fileName, cancellationToken);
            }
            catch (Exception e)
            {
                return Result<bool, Exception>.Fail(e);
            }
        });

        return await Task.WhenAll(tasks).WithAggregatedExceptions();
    }

    public async ValueTask<int> DeleteAllAsync(
        string[] container,
        string? searchPattern = null,
        CancellationToken cancellationToken = default
    )
    {
        await _EnsureClientConnectedAsync(cancellationToken);

        var containerPath = _BuildContainerPath(container);

        if (searchPattern is null)
        {
            var directoryPath = Path.Combine(_client.WorkingDirectory, containerPath);

            return await DeleteDirectoryAsync(directoryPath, includeSelf: false, cancellationToken);
        }

        searchPattern = Path.Combine(containerPath, _NormalizePath(searchPattern));

        if (searchPattern.EndsWith("/*", StringComparison.Ordinal))
        {
            return await DeleteDirectoryAsync(searchPattern[..^2], includeSelf: false, cancellationToken);
        }

        var files = await _GetFileListAsync(searchPattern, cancellationToken: cancellationToken).AnyContext();
        var count = 0;

        _logger.LogInformation("Deleting {FileCount} files matching {SearchPattern}", files.Count, searchPattern);

        foreach (var file in files)
        {
            var result = await _DeleteAsync(file.Path, cancellationToken);

            if (result)
            {
                count++;
            }
            else
            {
                _logger.LogWarning("Failed to delete {Path}", file.Path);
            }
        }

        _logger.LogTrace("Finished deleting {FileCount} files matching {SearchPattern}", count, searchPattern);

        return count;
    }

    public async Task<int> DeleteDirectoryAsync(
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
                count += await DeleteDirectoryAsync(file.FullName, includeSelf: true, cancellationToken);
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
            await _client.DeleteDirectoryAsync(directory, cancellationToken);
        }

        _logger.LogTrace("Finished deleting {Directory} directory with {FileCount} files", directory, count);

        return count;
    }

    #endregion

    #region Rename

    public async ValueTask<bool> RenameAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(blobContainer);
        Argument.IsNotNull(newBlobName);
        Argument.IsNotNull(newBlobContainer);

        await _EnsureClientConnectedAsync(cancellationToken);

        var blobPath = _BuildBlobPath(blobContainer, blobName);
        var targetPath = _BuildBlobPath(newBlobContainer, newBlobName);

        _logger.LogInformation("Renaming {Path} to {TargetPath}", blobPath, targetPath);

        // If the target path already exists, delete it.
        if (await ExistsAsync(newBlobContainer, newBlobName, cancellationToken))
        {
            _logger.LogDebug("Removing existing {TargetPath} path for rename operation", targetPath);
            _ = await DeleteAsync(newBlobContainer, newBlobName, cancellationToken);
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

            await CreateContainerAsync(newBlobContainer, cancellationToken);
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

    #endregion

    #region Copy

    public async ValueTask<bool> CopyAsync(
        string[] blobContainer,
        string blobName,
        string[] newBlobContainer,
        string newBlobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(blobContainer);
        Argument.IsNotNull(newBlobName);
        Argument.IsNotNull(newBlobContainer);

        await _EnsureClientConnectedAsync(cancellationToken);

        _logger.LogInformation(
            "Copying {@Container}/{Path} to {@TargetContainer}/{TargetPath}",
            blobContainer,
            blobName,
            newBlobContainer,
            newBlobName
        );

        try
        {
            var blob = await DownloadAsync(blobContainer, blobName, cancellationToken);

            if (blob is null)
            {
                return false;
            }

            await using var stream = blob.Stream;

            await UploadAsync(newBlobContainer, new BlobUploadRequest(stream, newBlobName), cancellationToken);

            return true;
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

            return false;
        }
    }

    #endregion

    #region Exists

    public async ValueTask<bool> ExistsAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        await _EnsureClientConnectedAsync(cancellationToken);

        var blobPath = _BuildBlobPath(container, blobName);

        _logger.LogTrace("Checking if {Path} exists", blobPath);

        var exists = await _client.ExistsAsync(blobPath, cancellationToken);

        return exists;
    }

    #endregion

    #region Downalod

    public async ValueTask<BlobDownloadResult?> DownloadAsync(
        string[] container,
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobName);
        Argument.IsNotNull(container);

        await _EnsureClientConnectedAsync(cancellationToken);

        var blobPath = _BuildBlobPath(container, blobName);

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

    #endregion

    #region List

    public async ValueTask<PagedFileListResult> GetPagedListAsync(
        string[] containers,
        string? searchPattern = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(containers);
        Argument.IsPositive(pageSize);

        var containerPath = _BuildContainerPath(containers);
        searchPattern = string.IsNullOrEmpty(searchPattern) ? containerPath : $"{containerPath}/{searchPattern}";

        var result = new PagedFileListResult(_ => _GetFiles(searchPattern, 1, pageSize, cancellationToken));
        await result.NextPageAsync().AnyContext();

        return result;
    }

    private async ValueTask<INextPageResult> _GetFiles(
        string? searchPattern,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var pagingLimit = pageSize;
        var skip = (page - 1) * pagingLimit;

        if (pagingLimit < int.MaxValue)
        {
            pagingLimit++;
        }

        var list = await _GetFileListAsync(searchPattern, pagingLimit, skip, cancellationToken).AnyContext();
        var hasMore = false;

        if (list.Count == pagingLimit)
        {
            hasMore = true;
            list.RemoveAt(pagingLimit - 1);
        }

        return new NextPageResult
        {
            Success = true,
            HasMore = hasMore,
            Blobs = list,
            NextPageFunc = hasMore ? _ => _GetFiles(searchPattern, page + 1, pageSize, cancellationToken) : null,
        };
    }

    private async Task<List<BlobSpecification>> _GetFileListAsync(
        string? searchPattern = null,
        int? limit = null,
        int? skip = null,
        CancellationToken cancellationToken = default
    )
    {
        if (limit is <= 0)
        {
            return [];
        }

        var list = new List<BlobSpecification>();
        var criteria = _GetRequestCriteria(searchPattern);

        await _EnsureClientConnectedAsync(cancellationToken);

        // NOTE: This could be expensive the larger the directory structure you have as we aren't efficiently doing paging.
        var recordsToReturn = limit.HasValue ? (skip.GetValueOrDefault() * limit) + limit : null;

        _logger.LogTrace(
            "Getting file list recursively matching {Prefix} and {Pattern}...",
            criteria.Prefix,
            criteria.Pattern
        );

        await _GetFileListRecursivelyAsync(criteria.Prefix, criteria.Pattern, list, recordsToReturn, cancellationToken)
            .AnyContext();

        if (skip.HasValue)
        {
            list = list.Skip(skip.Value).ToList();
        }

        if (limit.HasValue)
        {
            list = list.Take(limit.Value).ToList();
        }

        return list;
    }

    private async Task _GetFileListRecursivelyAsync(
        string pathPrefix,
        Regex? pattern,
        ICollection<BlobSpecification> list,
        int? recordsToReturn = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(pathPrefix);
        Argument.IsNotNull(pattern);

        if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Cancellation requested");

            return;
        }

        var files = new List<ISftpFile>();

        try
        {
            await foreach (var file in _client.ListDirectoryAsync(pathPrefix, cancellationToken).AnyContext())
            {
                files.Add(file);
            }
        }
        catch (SftpPathNotFoundException)
        {
            _logger.LogDebug("Directory not found with {PathPrefix}", pathPrefix);

            return;
        }

        foreach (
            var file in files
                .Where(f => f.IsRegularFile || f.IsDirectory)
                .OrderByDescending(f => f.IsRegularFile)
                .ThenBy(f => f.Name, StringComparer.Ordinal)
        )
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Cancellation requested");

                return;
            }

            if (recordsToReturn.HasValue && list.Count >= recordsToReturn)
            {
                break;
            }

            // If the prefix (current directory) is empty, use the current working directory instead of a rooted path.
            var path = string.IsNullOrEmpty(pathPrefix) ? file.Name : $"{pathPrefix}/{file.Name}";

            if (file.IsDirectory)
            {
                if (file.Name is "." or "..")
                {
                    continue;
                }

                await _GetFileListRecursivelyAsync(path, pattern, list, recordsToReturn, cancellationToken)
                    .AnyContext();

                continue;
            }

            if (!file.IsRegularFile)
            {
                continue;
            }

            if (pattern?.IsMatch(path) == false)
            {
                _logger.LogTrace("Skipping {Path}: Doesn't match pattern", path);

                continue;
            }

            list.Add(
                new BlobSpecification
                {
                    Path = path,
                    Created = file.LastWriteTimeUtc,
                    Modified = file.LastWriteTimeUtc,
                    Size = file.Length,
                }
            );
        }
    }

    private static SearchCriteria _GetRequestCriteria(string? searchPattern)
    {
        if (string.IsNullOrEmpty(searchPattern))
        {
            return new SearchCriteria { Prefix = string.Empty };
        }

        var normalizedSearchPattern = _NormalizePath(searchPattern);
        var wildcardPos = normalizedSearchPattern.IndexOf('*', StringComparison.Ordinal);
        var hasWildcard = wildcardPos >= 0;

        string prefix;
        Regex patternRegex;

        if (hasWildcard)
        {
            var searchRegexText = Regex.Escape(normalizedSearchPattern).Replace("\\*", ".*?", StringComparison.Ordinal);
            patternRegex = new Regex($"^{searchRegexText}$", RegexOptions.ExplicitCapture, RegexPatterns.MatchTimeout);
            var beforeWildcard = normalizedSearchPattern[..wildcardPos];
            var slashPos = beforeWildcard.LastIndexOf('/');
            prefix = slashPos >= 0 ? normalizedSearchPattern[..slashPos] : string.Empty;
        }
        else
        {
            patternRegex = new Regex(
                $"^{normalizedSearchPattern}$",
                RegexOptions.ExplicitCapture,
                RegexPatterns.MatchTimeout
            );

            var slashPos = normalizedSearchPattern.LastIndexOf('/');
            prefix = slashPos >= 0 ? normalizedSearchPattern[..slashPos] : string.Empty;
        }

        return new SearchCriteria(prefix, patternRegex);
    }

    private sealed record SearchCriteria(string Prefix = "", Regex? Pattern = null);

    #endregion

    #region Build Paths

    private static string _BuildBlobPath(string[] container, string blobName)
    {
        return _BuildContainerPath(container) + "/" + blobName;
    }

    private static string _BuildContainerPath(string[] container)
    {
        return string.Join('/', container);
    }

    [return: NotNullIfNotNull(nameof(path))]
    private static string? _NormalizePath(string? path)
    {
        return path?.Replace('\\', '/');
    }

    #endregion

    #region Build Clients

    private async ValueTask _EnsureClientConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
        {
            return;
        }

        _logger.LogTrace("Connecting to {Host}:{Port}", _client.ConnectionInfo.Host, _client.ConnectionInfo.Port);

        await _client.ConnectAsync(cancellationToken);

        _logger.LogTrace(
            "Connected to {Host}:{Port} in {WorkingDirectory}",
            _client.ConnectionInfo.Host,
            _client.ConnectionInfo.Port,
            _client.WorkingDirectory
        );
    }

    private static ConnectionInfo _BuildConnectionInfo(SshBlobStorageSettings settings)
    {
        Argument.IsNotNull(settings);

        if (
            !Uri.TryCreate(settings.ConnectionString, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.UserInfo)
        )
        {
            throw new InvalidOperationException("Unable to parse connection string uri");
        }

        var userParts = uri.UserInfo.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var username = Uri.UnescapeDataString(userParts[0]);
        var password = Uri.UnescapeDataString(userParts.Length > 1 ? userParts[1] : string.Empty);
        var port = uri.Port > 0 ? uri.Port : 22;

        var authenticationMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrEmpty(password))
        {
            authenticationMethods.Add(new PasswordAuthenticationMethod(username, password));
        }

        if (settings.PrivateKey is not null)
        {
            authenticationMethods.Add(
                new PrivateKeyAuthenticationMethod(
                    username,
                    new PrivateKeyFile(settings.PrivateKey, settings.PrivateKeyPassPhrase)
                )
            );
        }

        if (authenticationMethods.Count == 0)
        {
            authenticationMethods.Add(new NoneAuthenticationMethod(username));
        }

        if (string.IsNullOrEmpty(settings.Proxy))
        {
            return new ConnectionInfo(uri.Host, port, username, [.. authenticationMethods]);
        }

        if (
            !Uri.TryCreate(settings.Proxy, UriKind.Absolute, out var proxyUri)
            || string.IsNullOrEmpty(proxyUri.UserInfo)
        )
        {
            throw new InvalidOperationException("Unable to parse proxy uri");
        }

        var proxyParts = proxyUri.UserInfo.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var proxyUsername = proxyParts[0];
        var proxyPassword = proxyParts.Length > 1 ? proxyParts[1] : null;

        var proxyType = settings.ProxyType;

        if (proxyType is ProxyTypes.None && proxyUri.Scheme.StartsWith("http", StringComparison.Ordinal))
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

    #endregion

    #region Dispose

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
