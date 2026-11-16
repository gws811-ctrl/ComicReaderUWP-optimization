// Copyright (c) aicd0. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;

using ComicReaderUWP.Common.Imaging;
using ComicReaderUWP.Core.Common.DebugTools;
using ComicReaderUWP.Core.Common.Utils;

namespace ComicReaderUWP.Helpers.Imaging;

internal sealed partial class LocalFileImageSource : IImageSource
{
    private const string TAG = nameof(LocalFileImageSource);

    /// <summary>
    /// Larger read buffer plus a sequential-scan hint: comic pages are read front to back and are
    /// often stored on spinning disks, where the default 4 KB buffer causes excessive seeks.
    /// </summary>
    private const int FileStreamBufferSize = 1 << 20;

    private readonly string _path;
    private readonly string _uri;
    private readonly ImageLoaderSchedulerGroup _preferredSchedulerGroup;

    public LocalFileImageSource(string path, string? uri = null)
    {
        _path = path;
        _uri = string.IsNullOrEmpty(uri) ? path : uri;
        _preferredSchedulerGroup = ImageLoaderSchedulerGroup.FromPath(path);
    }

    public string Uri => _uri;

    public ImageLoaderSchedulerGroup PreferredSchedulerGroup => _preferredSchedulerGroup;

    public bool IsCacheValidationEnabled => true;

    public Task<IImageConnection?> Open()
    {
        IImageConnection? connection = File.Exists(_path) ? new LocalFileImageConnection(_path) : null;
        return Task.FromResult(connection);
    }

    private sealed partial class LocalFileImageConnection(string path) : IImageConnection
    {
        public string Path => path;

        public string CacheKey => path;

        public string Fingerprint => FileUtils.GetFileSignature(path);

        public void Dispose()
        {
        }

        public Task<Stream?> OpenImageStream()
        {
            Stream? stream = null;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileStreamBufferSize, FileOptions.SequentialScan);
            }
            catch (Exception ex)
            {
                Logger.E(TAG, $"Failed to open image '{path}'", ex);
            }

            return Task.FromResult(stream);
        }

        public IVectorImageService? OpenVectorService()
        {
            return null;
        }
    }
}
