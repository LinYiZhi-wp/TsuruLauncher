using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TsuruLauncher.Services
{
    /// <summary>
    /// 本地皮肤库。皮肤就是一张 PNG（64×64 或旧的 64×32），存在
    /// <c>%APPDATA%\TsuruLauncher\skins\</c> 下，文件名即皮肤名。
    /// </summary>
    public static class SkinService
    {
        private static string? _dir;

        /// <summary>皮肤库目录（首次访问时自动创建）。</summary>
        public static string SkinDirectory
        {
            get
            {
                if (_dir != null) return _dir;
                try
                {
                    _dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "TsuruLauncher", "skins");
                    Directory.CreateDirectory(_dir);
                }
                catch
                {
                    _dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skins");
                    try { Directory.CreateDirectory(_dir); } catch { }
                }
                return _dir;
            }
        }

        /// <summary>列出皮肤库里所有 PNG（按文件名排序）。</summary>
        /// <summary>
        /// 账号皮肤备份目录（<c>skins/.account/</c>）。
        /// ⚠ 放**子目录**里，`ListSkins()` 只扫顶层 ——
        ///   这样自动备份不会出现在用户的「已保存皮肤」列表里。
        ///   用户明确说过：那个列表只该有「我自己的皮肤 + 我自己加的文件」，
        ///   自动备份混进去会让人以为是自己加的。
        /// </summary>
        public static string AccountBackupRoot => Path.Combine(SkinDirectory, ".account");

        /// <summary>
        /// **某个账号**的皮肤备份目录：<c>skins/.account/&lt;用户名&gt;/</c>。
        ///
        /// ⚠ 必须按账号分开！之前所有账号的备份都堆在 <c>.account/</c> 一个目录里，
        ///   切到离线账号时会把正版账号的皮肤历史也显示出来。
        /// </summary>
        public static string AccountBackupDirectory(string username)
        {
            string safe = string.Join("_", (username ?? "").Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(AccountBackupRoot, string.IsNullOrWhiteSpace(safe) ? "_unknown" : safe);
        }

        /// <summary>
        /// 把账号当前皮肤备份到**隐藏目录**，返回文件路径。
        ///
        /// ⚠⚠ **只增不改，绝不覆盖已有备份。**
        ///   之前我写成「每次加载都覆盖 <username>.png」—— 结果用户换成 Steve 之后，
        ///   备份也跟着变成 Steve，他原来那张就被冲掉了，等于没备份。
        ///   现在每个**不同内容**的皮肤各存一份（<username>.png / <username> 2.png …），
        ///   全部留在皮肤库里当卡片，随时能点回去。
        ///
        /// 内容相同（字节完全一致）就直接复用已有文件，不重复存。
        /// </summary>
        public static string? BackupAccountSkin(string username, byte[]? bytes, string? preferredName = null)
        {
            if (bytes == null || bytes.Length == 0 || string.IsNullOrWhiteSpace(username)) return null;

            try
            {
                string backupDir = AccountBackupDirectory(username);
                Directory.CreateDirectory(backupDir);

                // 文件名：优先用调用方给的「这张皮肤叫什么」（能对上默认皮肤就是 Steve 之类），
                // 比「Player 2」清楚得多。用户名里可能有非法字符，洗一下。
                string raw = string.IsNullOrWhiteSpace(preferredName) ? username : preferredName!;
                string safe = string.Join("_", raw.Split(Path.GetInvalidFileNameChars()));
                string safeUser = string.Join("_", username.Split(Path.GetInvalidFileNameChars()));

                // 已经有同样内容的 → 复用（避免每次进页面都新建一份）
                foreach (var existing in Directory.EnumerateFiles(backupDir, "*.png"))
                {
                    try
                    {
                        var old = File.ReadAllBytes(existing);
                        if (old.Length == bytes.Length && old.AsSpan().SequenceEqual(bytes))
                            return existing;
                    }
                    catch { }
                }

                // 新皮肤 → 找一个没被占用的名字，**不动旧文件**
                string path = Path.Combine(backupDir, safe + ".png");
                for (int i = 2; File.Exists(path); i++)
                    path = Path.Combine(backupDir, $"{safe} {i}.png");

                File.WriteAllBytes(path, bytes);

                // 记下「当前用的是哪张」—— 离线/接口失败时靠它标出「当前」
                // （只靠"最近修改"不准：用户换回旧皮肤时旧文件的时间戳不会更新）
                try { File.WriteAllText(Path.Combine(backupDir, ".current"), Path.GetFileName(path)); }
                catch { }

                return path;
            }
            catch { return null; }
        }

        /// <summary>
        /// 读上次确认的「当前账号皮肤」备份路径。
        /// 网络/接口挂了的时候用它标出哪张是当前，比"最近修改时间"准得多。
        /// </summary>
        public static string? ReadCurrentAccountSkin(string? username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username)) return null;
                string dir = AccountBackupDirectory(username);
                string marker = Path.Combine(dir, ".current");
                if (!File.Exists(marker)) return null;

                string name = File.ReadAllText(marker).Trim();
                if (string.IsNullOrWhiteSpace(name)) return null;

                string path = Path.Combine(dir, name);
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// 贴图像素指纹 —— 用来判断「账号当前皮肤是不是就是某款默认皮肤」。
        ///
        /// ⚠ 不能直接比 PNG 字节：同一张图重新编码后字节完全不同
        ///   （调色板 vs RGBA、压缩级别都会变）。要比**解码后的像素**。
        /// </summary>
        public static string PixelHash(BitmapSource? bmp)
        {
            if (bmp == null) return "";
            try
            {
                var conv = new FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var buf = new byte[w * h * 4];
                conv.CopyPixels(buf, w * 4, 0);

                using var sha = System.Security.Cryptography.SHA1.Create();
                return Convert.ToHexString(sha.ComputeHash(buf));
            }
            catch { return ""; }
        }

        /// <summary>
        /// 列出账号皮肤的所有历史备份（隐藏目录里的）。
        /// ⚠ 这些**要显示给用户** —— 用户换完皮肤想换回去时，
        ///   靠的就是这里的历史。之前只显示「当前那张」，换完就找不回上一张了。
        /// </summary>
        public static List<string> ListAccountBackups(string? username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username)) return new List<string>();
                string dir = AccountBackupDirectory(username);
                if (!Directory.Exists(dir)) return new List<string>();

                return Directory.EnumerateFiles(dir, "*.png")
                                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
            catch { return new List<string>(); }
        }

        public static List<string> ListSkins()
        {
            try
            {
                if (!Directory.Exists(SkinDirectory)) return new List<string>();
                return Directory.EnumerateFiles(SkinDirectory, "*.png")
                                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// 把外部 PNG 复制进皮肤库。重名时自动加 (2) (3)…
        /// 返回入库后的完整路径；失败返回 null。
        /// </summary>
        public static string? Import(string sourcePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
                if (!string.Equals(Path.GetExtension(sourcePath), ".png", StringComparison.OrdinalIgnoreCase)) return null;

                Directory.CreateDirectory(SkinDirectory);

                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                string target = Path.Combine(SkinDirectory, baseName + ".png");

                int n = 2;
                while (File.Exists(target))
                {
                    target = Path.Combine(SkinDirectory, $"{baseName} ({n}).png");
                    n++;
                }

                File.Copy(sourcePath, target);
                return target;
            }
            catch { return null; }
        }

        /// <summary>从皮肤库删除一张皮肤。</summary>
        /// <summary>
        /// 删除皮肤文件 —— **送进回收站，不是永久删除**。
        ///
        /// ⚠ 用户自己找的皮肤文件删掉就没了，所以：
        ///   1) 调用方必须先弹确认框
        ///   2) 这里用回收站，误删还能捞回来
        /// </summary>
        public static bool DeleteToRecycleBin(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;

                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

                return true;
            }
            catch { return false; }
        }

        public static bool Delete(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

                // 只允许删皮肤库里的文件，防止误删别处
                string full = Path.GetFullPath(path);
                if (!full.StartsWith(Path.GetFullPath(SkinDirectory), StringComparison.OrdinalIgnoreCase))
                    return false;

                File.Delete(full);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 读成 BitmapImage（解码后就 Freeze，跨线程 / 多次绑定都安全）。
        /// 用 OnLoad + 独立的流，避免锁住文件。
        /// </summary>
        // 解码缓存：路径 + mtime → 已解码的 BitmapImage。
        // ⚠ `Reload()` 会重建所有 SkinEntry，不缓存就是每次切页签把整库 PNG 重解码一遍。
        private static readonly Dictionary<string, BitmapImage> _loadCache = new();
        private static readonly object _loadLock = new();
        private const int LoadCacheLimit = 128;

        public static BitmapImage? Load(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

                string key;
                try { key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}"; }
                catch { key = path; }

                lock (_loadLock)
                {
                    if (_loadCache.TryGetValue(key, out var hit)) return hit;
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                // ⚠ 不要加 BitmapCreateOptions.IgnoreImageCache：
                //   它会让 WPF 拿 UriSource 去清图片缓存，而这里只给了 StreamSource
                //   → 内部缓存 key 为 null，EndInit 直接抛 "Value cannot be null. (Parameter 'key')"。
                using (var fs = File.OpenRead(path))
                {
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                }
                bmp.Freeze();

                lock (_loadLock)
                {
                    if (_loadCache.Count >= LoadCacheLimit) _loadCache.Clear();
                    _loadCache[key] = bmp;
                }
                return bmp;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, $"SkinService.Load({path})");
                return null;
            }
        }

        /// <summary>
        /// 判断皮肤是不是**纤细（Slim / Alex）**模型。
        ///
        /// 判据：看右臂**背面**那一块 —— 经典模型的右臂背在 (52,20)-(56,32)，
        /// 纤细的背在 (51,20)-(54,32)。所以取 **x 54..56 / y 20..32** 这 2×12 格：
        ///   经典皮肤 → 属于右臂背面，不透明
        ///   纤细皮肤 → 布局里没用到，全透明
        ///
        /// ⚠ 之前我写的是 (54,16)-(56,20)，那块在**两种布局里都没用到**
        ///   （顶部/底面只到 x 52），所以永远读到透明 → 全部误判成纤细。
        /// 64×32 的旧皮肤一律按经典处理。
        /// </summary>
        public static bool DetectSlim(BitmapSource bmp)
        {
            try
            {
                if (bmp.PixelWidth < 64 || bmp.PixelHeight < 64) return false;

                var conv = new FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
                int w = conv.PixelWidth;
                var buf = new byte[w * conv.PixelHeight * 4];
                conv.CopyPixels(buf, w * 4, 0);

                int opaque = 0;
                for (int y = 20; y < 32; y++)
                    for (int x = 54; x < 56; x++)
                        if (buf[(y * w + x) * 4 + 3] > 8) opaque++;

                return opaque == 0;
            }
            catch { return false; }
        }

        /// <summary>皮肤名（不含扩展名）。</summary>
        public static string DisplayName(string path)
        {
            try { return Path.GetFileNameWithoutExtension(path); }
            catch { return path; }
        }
    }
}
