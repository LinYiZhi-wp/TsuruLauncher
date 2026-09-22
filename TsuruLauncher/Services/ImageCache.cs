using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace TsuruLauncher.Services
{
    public static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, BitmapImage> _cache = new();
        private static readonly ConcurrentDictionary<string, long> _cacheSizes = new();
        private static readonly LinkedList<string> _lruList = new();
        private static readonly object _lruLock = new();
        private static long _totalMemoryBytes;
        private const long MaxMemoryBytes = 80 * 1024 * 1024;
        private const int MaxCacheCount = 200;

        private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 8
        });

        public static async Task<BitmapImage?> GetOrLoadAsync(string url, int decodeWidth = 0)
        {
            if (string.IsNullOrEmpty(url)) return null;

            if (_cache.TryGetValue(url, out var cached))
            {
                TouchLru(url);
                return cached;
            }

            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
                bitmap.StreamSource = new MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();

                AddToCache(url, bitmap, bytes.Length);
                return bitmap;
            }
            catch (Exception ex)
            {
                TsuruLauncher.Utilities.Logger.LogInfo("[ImgCache] FAIL " + url + " :: " + ex.GetType().Name + " :: " + ex.Message);
                return null;
            }
        }

        private static void AddToCache(string url, BitmapImage bitmap, int rawDataSize)
        {
            long estimatedSize = EstimateBitmapSize(bitmap, rawDataSize);

            if (_cache.TryAdd(url, bitmap))
            {
                _cacheSizes.TryAdd(url, estimatedSize);
                Interlocked.Add(ref _totalMemoryBytes, estimatedSize);

                lock (_lruLock)
                {
                    _lruList.AddLast(url);
                }

                EvictIfNeeded();
            }
        }

        private static void TouchLru(string url)
        {
            lock (_lruLock)
            {
                _lruList.Remove(url);
                _lruList.AddLast(url);
            }
        }

        private static void EvictIfNeeded()
        {
            while ((_totalMemoryBytes > MaxMemoryBytes || _cache.Count > MaxCacheCount) && _cache.Count > 10)
            {
                string? oldest;
                lock (_lruLock)
                {
                    if (_lruList.Count == 0) break;
                    oldest = _lruList.First?.Value;
                    if (oldest == null) break;
                    _lruList.RemoveFirst();
                }

                if (_cache.TryRemove(oldest, out _))
                {
                    if (_cacheSizes.TryRemove(oldest, out var size))
                        Interlocked.Add(ref _totalMemoryBytes, -size);
                }
            }
        }

        private static long EstimateBitmapSize(BitmapImage bitmap, int rawDataSize)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            if (width > 0 && height > 0)
                return (long)width * height * 4;
            return rawDataSize;
        }

        public static void Clear()
        {
            _cache.Clear();
            _cacheSizes.Clear();
            Interlocked.Exchange(ref _totalMemoryBytes, 0);
            lock (_lruLock)
            {
                _lruList.Clear();
            }
        }
    }
}