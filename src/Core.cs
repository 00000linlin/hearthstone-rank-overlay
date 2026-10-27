using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HearthstoneRank
{
    [DataContract]
    public sealed class Config
    {
        [DataMember] public string Player = "";
        [DataMember] public string Region = "AP";
        [DataMember] public string Mode = "standard";
        [DataMember] public int Season = 0;
        [DataMember] public int HintRank = 0;
        [DataMember] public bool Monitoring = true;
        [DataMember] public bool OverlayVisible = true;
        [DataMember] public bool Topmost = true;
        [DataMember] public bool CloseToTray = true;
        [DataMember] public int X = -1;
        [DataMember] public int Y = -1;
        public Config Copy() { return (Config)MemberwiseClone(); }
        public string IdentityKey { get { return Region + "/" + Mode + "/" + Season + "/" + Player; } }
        public void Validate()
        {
            Player = (Player ?? "").Trim();
            if (Player.Length > 80) throw new ArgumentException("玩家名称最多 80 个字符。");
            if (!new[] { "AP", "US", "EU", "CN" }.Contains(Region)) throw new ArgumentException("服务器无效。");
            if (!new[] { "standard", "wild", "battlegrounds", "battlegroundsduo" }.Contains(Mode))
                throw new ArgumentException("模式无效。");
            if (Season < 0 || Season > 9999 || HintRank < 0 || HintRank > 10000000)
                throw new ArgumentException("赛季或定位参考排名无效。");
        }
    }

    public static class Json
    {
        public static T Read<T>(Stream stream)
        { return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
        public static T Load<T>(string path)
        { using (var stream = File.OpenRead(path)) return Read<T>(stream); }
        public static void Save<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }

    // The international endpoint appends hundreds of KB of website metadata to
    // every 25-row page. Stop reading once the two required root values arrive.
    // Never deserialize that metadata or retain the full response in the LOH.
    public sealed class BoardProjection
    {
        readonly TextReader reader;
        int consumed;
        BoardProjection(TextReader reader) { this.reader = reader; }
        int Read()
        {
            if (++consumed > 4 * 1024 * 1024) throw new InvalidDataException("榜单响应过大。");
            return reader.Read();
        }
        int NonSpace()
        { int ch; do { ch = Read(); } while (ch >= 0 && Char.IsWhiteSpace((char)ch)); return ch; }
        string Value(int first, bool keep)
        {
            var output = keep ? new StringBuilder(2048) : null;
            bool quoted = first == '"', escaped = false;
            int depth = first == '{' || first == '[' ? 1 : 0;
            Action<int> append = ch => {
                if (ch < 0) throw new InvalidDataException("榜单 JSON 意外结束。");
                if (keep) { if (output.Length > 65536) throw new InvalidDataException("单页榜单过大。"); output.Append((char)ch); }
            };
            append(first);
            if (quoted || depth > 0)
            {
                while (true)
                {
                    int ch = Read(); append(ch);
                    if (quoted)
                    {
                        if (escaped) escaped = false;
                        else if (ch == '\\') escaped = true;
                        else if (ch == '"') { quoted = false; if (depth == 0) break; }
                    }
                    else if (ch == '"') quoted = true;
                    else if (ch == '{' || ch == '[') { if (++depth > 128) throw new InvalidDataException("榜单嵌套层数过多。"); }
                    else if (ch == '}' || ch == ']') { if (--depth == 0) break; }
                }
            }
            else
            {
                while (reader.Peek() >= 0 && reader.Peek() != ',' && reader.Peek() != '}' && !Char.IsWhiteSpace((char)reader.Peek())) append(Read());
            }
            return keep ? output.ToString() : null;
        }
        string Project()
        {
            if (NonSpace() != '{') throw new InvalidDataException("官网没有返回 JSON 对象。");
            string season = null, board = null;
            while (true)
            {
                int first = NonSpace();
                if (first == '}') break;
                if (first != '"') throw new InvalidDataException("榜单字段格式无效。");
                string keyJson = Value(first, true);
                string key;
                using (var input = new MemoryStream(Encoding.UTF8.GetBytes(keyJson))) key = Json.Read<string>(input);
                if (NonSpace() != ':') throw new InvalidDataException("榜单字段缺少冒号。");
                first = NonSpace();
                string value = Value(first, key == "seasonId" || key == "leaderboard");
                if (key == "seasonId") season = value;
                if (key == "leaderboard") board = value;
                if (season != null && board != null) return "{\"seasonId\":" + season + ",\"leaderboard\":" + board + "}";
                int delimiter = NonSpace();
                if (delimiter == '}') break;
                if (delimiter != ',') throw new InvalidDataException("榜单字段分隔符无效。");
            }
            throw new InvalidDataException("国际服榜单缺少赛季或排名字段。");
        }
        public static GlobalWire Read(Stream stream)
        {
            using (var input = new StreamReader(stream, Encoding.UTF8, true, 2048))
            using (var projection = new MemoryStream(Encoding.UTF8.GetBytes(new BoardProjection(input).Project())))
                return Json.Read<GlobalWire>(projection);
        }
    }

    public sealed class RankEntry
    {
        public string Name;
        public int Rank;
        public double? Score;
        public int Page;
        public override string ToString()
        { return "#" + Rank + "  " + Name + (Score.HasValue ? "  积分 " + Score.Value.ToString("0.##", CultureInfo.InvariantCulture) : ""); }
    }

    public sealed class BoardPage
    {
        public int Season;
        public int Total;
        public int TotalPages;
        public List<RankEntry> Rows = new List<RankEntry>();
    }

    public enum RankStatus { Searching, Ready, Ambiguous, IdentityUnavailable, NotFound, Offline, Paused }

    public sealed class Snapshot
    {
        public string QueryKey = "";
        public RankStatus Status;
        public string Message = "正在查询";
        public List<RankEntry> Candidates = new List<RankEntry>();
        public DateTime CheckedAt;
        public int Season;
        public int Total;
        public int CompletedPages;
        public int TotalPages;
        public bool Complete;
        public string Error = "";
    }

    public static class Identity
    {
        public static string BaseName(string name)
        {
            int hash = (name ?? "").LastIndexOf('#');
            return hash > 0 ? name.Substring(0, hash) : (name ?? "");
        }
        public static bool HasTag(string name)
        {
            int hash = (name ?? "").LastIndexOf('#');
            return hash > 0 && hash < name.Length - 1 && name.Substring(hash + 1).All(Char.IsDigit);
        }
        public static bool IsCandidate(string query, string name)
        {
            return String.Equals(BaseName(query), BaseName(name), StringComparison.OrdinalIgnoreCase);
        }
        public static Snapshot Resolve(string query, IEnumerable<RankEntry> rows, bool complete, int season, int total)
        {
            var candidates = rows.Where(r => IsCandidate(query, r.Name))
                .GroupBy(r => r.Rank).Select(g => g.First()).OrderBy(r => r.Rank).ToList();
            var result = new Snapshot { Candidates = candidates, Complete = complete, Season = season,
                Total = total, CheckedAt = DateTime.Now };
            if (HasTag(query))
            {
                var exact = candidates.Where(r => String.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase)).ToList();
                if (exact.Count > 0) result.Candidates = candidates = exact;
                else if (candidates.Count > 0)
                {
                    result.Status = RankStatus.IdentityUnavailable;
                    result.Message = "官网未公开战网编号，无法核验完整 BattleTag";
                    return result;
                }
            }
            if (!complete)
            { result.Status = RankStatus.Searching; result.Message = "正在核对全榜同名玩家"; }
            else if (candidates.Count > 1)
            { result.Status = RankStatus.Ambiguous; result.Message = "发现 " + candidates.Count + " 个同名结果；请在设置中查看"; }
            else if (candidates.Count == 1)
            { result.Status = RankStatus.Ready; result.Message = HasTag(query) ? "完整 BattleTag 匹配" : "昵称匹配 · 官网未公开战网编号"; }
            else
            { result.Status = RankStatus.NotFound; result.Message = "未在官方公开榜单找到该玩家"; }
            return result;
        }
    }

    [DataContract] public sealed class GlobalWire
    { [DataMember] public int seasonId; [DataMember] public GlobalBoard leaderboard; }
    [DataContract] public sealed class GlobalBoard
    { [DataMember] public List<GlobalRow> rows; [DataMember] public Pagination pagination; }
    [DataContract] public sealed class GlobalRow
    { [DataMember] public int rank; [DataMember] public string accountid; [DataMember] public double? rating; }
    [DataContract] public sealed class Pagination
    { [DataMember] public int totalPages; [DataMember] public int totalSize; }
    [DataContract] public sealed class ChinaWire
    { [DataMember] public int code; [DataMember] public string message; [DataMember] public ChinaBoard data; }
    [DataContract] public sealed class ChinaBoard
    { [DataMember] public List<ChinaRow> list; [DataMember] public int total; }
    [DataContract] public sealed class ChinaRow
    { [DataMember] public int position; [DataMember] public string battle_tag; [DataMember] public double? score; }
    [DataContract] public sealed class ChinaModeWire
    { [DataMember] public int code; [DataMember] public ChinaModeData data; }
    [DataContract] public sealed class ChinaModeData
    { [DataMember] public ChinaSeasons season_map; }
    [DataContract] public sealed class ChinaSeasons
    {
        [DataMember] public List<ChinaSeason> standard;
        [DataMember] public List<ChinaSeason> wild;
        [DataMember] public List<ChinaSeason> battlegrounds;
        [DataMember] public List<ChinaSeason> battlegroundsduo;
        public List<ChinaSeason> Get(string mode)
        {
            switch (mode)
            { case "wild": return wild; case "battlegrounds": return battlegrounds;
              case "battlegroundsduo": return battlegroundsduo; default: return standard; }
        }
    }
    [DataContract] public sealed class ChinaSeason
    { [DataMember] public int season_id; [DataMember] public string season; }

    public interface IBoardApi
    {
        int LatestChinaSeason(string mode, CancellationToken token);
        BoardPage Fetch(Config config, int page, int resolvedSeason, CancellationToken token);
    }

    public sealed class BoardApi : IBoardApi
    {
        const string GlobalUrl = "https://hearthstone.blizzard.com/en-us/api/community/leaderboardsData";
        const string ChinaUrl = "https://webapi.blizzard.cn/hs-rank-api-server/api/game/ranks";
        public static string PageUrl(Config c, int page, int season)
        {
            if (c.Region == "CN") return ChinaUrl + "?mode_name=" + c.Mode + "&season_id=" + season + "&page=" + page + "&page_size=25";
            return GlobalUrl + "?region=" + c.Region + "&leaderboardId=" + c.Mode + "&page=" + page
                + (season > 0 ? "&seasonId=" + season : "");
        }
        public static string WebsiteUrl(Config c, int page, int season)
        {
            if (c.Region == "CN") return "https://hs.blizzard.cn/community/leaderboards/index.html";
            return "https://hearthstone.blizzard.com/en-us/community/leaderboards?region=" + c.Region
                + "&leaderboardId=" + c.Mode + "&seasonId=" + season + "&page=" + page;
        }
        public static T Get<T>(string url, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "HearthstoneRank/1.0 (+public-leaderboard-client)";
            request.Accept = "application/json";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            using (token.Register(request.Abort))
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            {
                if (typeof(T) == typeof(GlobalWire)) return (T)(object)BoardProjection.Read(stream);
                using (var buffer = new MemoryStream())
                {
                    byte[] bytes = new byte[8192];
                    int count;
                    while ((count = stream.Read(bytes, 0, bytes.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        buffer.Write(bytes, 0, count);
                        if (buffer.Length > 4 * 1024 * 1024) throw new InvalidDataException("榜单响应超过 4 MB，已停止读取。");
                    }
                    buffer.Position = 0;
                    return Json.Read<T>(buffer);
                }
            }
        }
        public int LatestChinaSeason(string mode, CancellationToken token)
        {
            var data = Get<ChinaModeWire>("https://webapi.blizzard.cn/hs-rank-api-server/api/v2/game/mode", token);
            if (data.code != 0 || data.data == null || data.data.season_map == null)
                throw new InvalidDataException("国服赛季列表暂不可用。");
            var seasons = data.data.season_map.Get(mode);
            if (seasons == null || seasons.Count == 0) throw new InvalidDataException("此模式当前没有公开赛季。");
            // Use the official ordering; the displayed season number can differ from its ID.
            return seasons[0].season_id;
        }
        public BoardPage Fetch(Config config, int page, int resolvedSeason, CancellationToken token)
        {
            if (config.Region == "CN") return FromChina(Get<ChinaWire>(PageUrl(config, page, resolvedSeason), token), page, resolvedSeason);
            return FromGlobal(Get<GlobalWire>(PageUrl(config, page, resolvedSeason), token), page, resolvedSeason);
        }
        public static BoardPage FromGlobal(GlobalWire wire, int page, int expectedSeason)
        {
            if (wire == null || wire.leaderboard == null || wire.leaderboard.rows == null || wire.leaderboard.pagination == null)
                throw new InvalidDataException("国际服榜单格式发生变化。");
            if (expectedSeason > 0 && wire.seasonId != expectedSeason)
                throw new InvalidDataException("官网返回了其他赛季的数据。");
            var result = new BoardPage { Season = wire.seasonId, Total = wire.leaderboard.pagination.totalSize,
                TotalPages = wire.leaderboard.pagination.totalPages };
            foreach (var row in wire.leaderboard.rows)
                if (row.rank > 0 && row.accountid != null) result.Rows.Add(new RankEntry { Name = row.accountid, Rank = row.rank, Score = row.rating, Page = page });
            ValidatePage(result);
            return result;
        }
        public static BoardPage FromChina(ChinaWire wire, int page, int season)
        {
            if (wire == null || wire.code != 0 || wire.data == null || wire.data.list == null)
                throw new InvalidDataException("国服榜单暂不可用：" + (wire == null ? "空响应" : wire.message));
            var result = new BoardPage { Season = season, Total = wire.data.total, TotalPages = (wire.data.total + 24) / 25 };
            foreach (var row in wire.data.list)
                if (row.position > 0 && row.battle_tag != null) result.Rows.Add(new RankEntry { Name = row.battle_tag, Rank = row.position, Score = row.score, Page = page });
            ValidatePage(result);
            return result;
        }
        static void ValidatePage(BoardPage page)
        {
            if (page.Total < 0 || page.TotalPages < 0 || page.TotalPages > 50000)
                throw new InvalidDataException("榜单分页信息无效。");
        }
    }

    public sealed class RankSearch
    {
        readonly IBoardApi api;
        public RankSearch(IBoardApi api) { this.api = api; }
        public Snapshot All(Config c, Action<Snapshot> progress, CancellationToken token)
        {
            int season = c.Season;
            if (c.Region == "CN" && season == 0) season = api.LatestChinaSeason(c.Mode, token);
            var first = api.Fetch(c, 1, season, token);
            season = first.Season;
            var matches = first.Rows.Where(r => Identity.IsCandidate(c.Player, r.Name)).ToList();
            int done = 1, failed = 0;
            object gate = new object();
            DateTime lastProgress = DateTime.MinValue;
            Action report = delegate {
                var snapshot = Identity.Resolve(c.Player, matches, false, season, first.Total);
                snapshot.CompletedPages = done; snapshot.TotalPages = first.TotalPages;
                progress(snapshot);
            };
            report();
            var pages = Enumerable.Range(2, Math.Max(0, first.TotalPages - 1)).ToList();
            int hint = c.HintRank > 0 ? (c.HintRank - 1) / 25 + 1 : 1;
            pages.Sort((a, b) => Math.Abs(a - hint).CompareTo(Math.Abs(b - hint)));
            Parallel.ForEach(pages, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, page => {
                try
                {
                    var data = api.Fetch(c, page, season, token);
                    lock (gate) matches.AddRange(data.Rows.Where(r => Identity.IsCandidate(c.Player, r.Name)));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { if (token.IsCancellationRequested) token.ThrowIfCancellationRequested(); Interlocked.Increment(ref failed); }
                lock (gate)
                {
                    done++;
                    if ((DateTime.UtcNow - lastProgress).TotalMilliseconds > 750)
                    { lastProgress = DateTime.UtcNow; report(); }
                }
            });
            token.ThrowIfCancellationRequested();
            var result = Identity.Resolve(c.Player, matches, failed == 0, season, first.Total);
            result.CompletedPages = done; result.TotalPages = first.TotalPages;
            if (failed > 0) { result.Message = "有 " + failed + " 页暂未查到，尚不能排除其他同名玩家"; result.Error = result.Message; }
            return result;
        }
        public Snapshot Nearby(Config c, Snapshot previous, CancellationToken token)
        {
            var matches = new List<RankEntry>();
            var pages = previous.Candidates.Select(r => r.Page)
                .Where(p => p <= previous.TotalPages).Distinct();
            foreach (int page in pages)
            {
                var data = api.Fetch(c, page, previous.Season, token);
                matches.AddRange(data.Rows.Where(r => Identity.IsCandidate(c.Player, r.Name)));
            }
            if (matches.Count < previous.Candidates.Count)
            {
                var neighbors = previous.Candidates.SelectMany(r => new[] { Math.Max(1, r.Page - 1), r.Page + 1 })
                    .Where(p => p <= previous.TotalPages && !pages.Contains(p)).Distinct();
                foreach (int page in neighbors)
                {
                    var data = api.Fetch(c, page, previous.Season, token);
                    matches.AddRange(data.Rows.Where(r => Identity.IsCandidate(c.Player, r.Name)));
                }
            }
            var result = Identity.Resolve(c.Player, matches, true, previous.Season, previous.Total);
            result.TotalPages = previous.TotalPages;
            // Missing candidates or changed duplicate counts require another full scan.
            if (result.Candidates.Count != previous.Candidates.Count || result.Candidates.Count == 0)
                result.Complete = false;
            return result;
        }
    }

    public sealed class RankMonitor : IDisposable
    {
        readonly RankSearch search;
        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        CancellationTokenSource cancel = new CancellationTokenSource();
        Config config;
        int generation;
        bool scanRequested;
        bool disposed;
        public event Action<Snapshot> Changed;
        public RankMonitor(IBoardApi api)
        { search = new RankSearch(api); new Thread(Loop) { IsBackground = true, Name = "Leaderboard poller" }.Start(); }
        public void Apply(Config value)
        {
            lock (gate) {
                if (config == null || config.IdentityKey != value.IdentityKey) scanRequested = true;
                config = value.Copy(); generation++; cancel.Cancel(); cancel = new CancellationTokenSource();
            }
            wake.Set();
        }
        public void Refresh()
        { lock (gate) { scanRequested = true; generation++; cancel.Cancel(); cancel = new CancellationTokenSource(); } wake.Set(); }
        void Publish(int version, Snapshot snapshot)
        {
            Action<Snapshot> handler;
            lock (gate) { if (disposed || version != generation) return; snapshot.QueryKey = config.IdentityKey; handler = Changed; }
            if (handler != null) handler(snapshot);
        }
        void Loop()
        {
            Snapshot cached = null;
            DateTime scanned = DateTime.MinValue;
            int seenVersion = -1, failures = 0;
            while (true)
            {
                Config c; int version; CancellationToken token; bool rescan;
                lock (gate)
                { if (disposed) return; c = config == null ? null : config.Copy(); version = generation; token = cancel.Token; rescan = scanRequested; scanRequested = false; }
                if (c == null) { wake.WaitOne(); continue; }
                if (rescan) { cached = null; scanned = DateTime.MinValue; }
                if (version != seenVersion) { seenVersion = version; failures = 0; }
                if (!c.Monitoring || String.IsNullOrWhiteSpace(c.Player))
                { Publish(version, new Snapshot { Status = RankStatus.Paused, Message = c.Player.Length == 0 ? "请在设置中填写玩家名称" : "监测已暂停" }); wake.WaitOne(); continue; }
                DateTime started = DateTime.UtcNow;
                int wait = 5000;
                try
                {
                    bool scan = cached == null || !cached.Complete || cached.Candidates.Count == 0 || (started - scanned).TotalMinutes >= 15;
                    if (scan)
                    {
                        cached = search.All(c, s => Publish(version, s), token);
                        scanned = DateTime.UtcNow;
                    }
                    else
                    {
                        var next = search.Nearby(c, cached, token);
                        if (!next.Complete)
                        {
                            cached = search.All(c, s => Publish(version, s), token);
                            scanned = DateTime.UtcNow;
                            scan = true;
                        }
                        else cached = next;
                    }
                    Publish(version, cached);
                    // Discovery is an infrequent bulk operation. Reclaim its
                    // temporary HTTPS/JSON objects before returning to polling.
                    // No per-poll GC or artificial working-set trimming.
                    if (scan) GC.Collect(2, GCCollectionMode.Forced);
                    failures = 0;
                    if (!cached.Complete || cached.Candidates.Count == 0) wait = 60000;
                    else wait = Math.Max(1000, 5000 - (int)(DateTime.UtcNow - started).TotalMilliseconds);
                }
                catch (Exception error)
                {
                    if (token.IsCancellationRequested) continue;
                    failures++;
                    Publish(version, new Snapshot { Status = RankStatus.Offline, Message = "连接失败，自动重试",
                        Error = error.GetBaseException().Message, Candidates = cached == null ? new List<RankEntry>() : cached.Candidates,
                        CheckedAt = cached == null ? DateTime.MinValue : cached.CheckedAt,
                        Season = cached == null ? 0 : cached.Season, Total = cached == null ? 0 : cached.Total });
                    wait = Math.Min(60000, 5000 * (1 << Math.Min(failures, 3)));
                }
                wake.WaitOne(wait);
            }
        }
        public void Dispose()
        { lock (gate) { disposed = true; cancel.Cancel(); } wake.Set(); }
    }
}
