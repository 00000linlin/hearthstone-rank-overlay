using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using HearthstoneRank;

static class CoreTests
{
    static int passed;
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL: " + message); passed++; Console.WriteLine("PASS " + message); }
    static T Parse<T>(string json)
    { using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json))) return Json.Read<T>(stream); }
    static RankEntry Entry(int rank, string name, int page, double? score = null)
    { return new RankEntry { Rank = rank, Name = name, Page = page, Score = score }; }
    sealed class FakeApi : IBoardApi
    {
        public Func<Config, int, CancellationToken, BoardPage> Handler;
        public int LatestChinaSeason(string mode, CancellationToken token) { return 19; }
        public BoardPage Fetch(Config c, int page, int season, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Handler(c, page, token); }
    }
    static BoardPage Page(int page, params RankEntry[] rows)
    { return new BoardPage { Season = 156, Total = 75, TotalPages = 3, Rows = rows.ToList() }; }
    static void Offline()
    {
        var rows = new[] { Entry(12, "shenshui", 1), Entry(62, "SHENSHUI", 3) };
        var duplicate = Identity.Resolve("shenshui", rows, true, 156, 75);
        Check(duplicate.Status == RankStatus.Ambiguous && duplicate.Candidates.Count == 2, "duplicates never silently choose first result");
        Check(Identity.Resolve("shenshui#1234", rows, true, 156, 75).Status == RankStatus.IdentityUnavailable, "missing tag suffix is not authentication");
        var tagged = Identity.Resolve("shenshui#1234", new[] { Entry(1, "shenshui#9999", 1), Entry(20, "shenshui#1234", 1) }, true, 156, 75);
        Check(tagged.Status == RankStatus.Ready && tagged.Candidates.Single().Rank == 20, "public full BattleTag can be selected exactly");
        Check(!Identity.IsCandidate("shenshui", "shenshuip"), "prefixes do not count as exact names");
        Check(Identity.IsCandidate("深水", "深水"), "Chinese names preserved");
        Check(Identity.Resolve("shenshui", rows.Take(1), false, 156, 75).Status == RankStatus.Searching, "partial scan is not a verified unique result");
        var global = BoardApi.FromGlobal(Parse<GlobalWire>("{\"seasonId\":19,\"leaderboard\":{\"rows\":[{\"rank\":1,\"accountid\":\"测试\",\"rating\":17001}],\"pagination\":{\"totalPages\":1,\"totalSize\":1}},\"unknown\":{\"ignored\":true}}"), 1, 19);
        Check(global.Rows[0].Name == "测试" && global.Rows[0].Score == 17001, "international Battlegrounds schema and Unicode");
        string projectionJson = "{\"seasonId\":19,\"leaderboard\":{\"rows\":[{\"rank\":1,\"accountid\":\"a\\\"{x}\\\\b\",\"rating\":17000}],\"pagination\":{\"totalPages\":1,\"totalSize\":1}},\"metadata\":\"" + new string('x', 300000) + "\"}";
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(projectionJson)))
        {
            var projected = BoardProjection.Read(input);
            Check(projected.leaderboard.rows[0].accountid == "a\"{x}\\b", "stream projection respects escaped quotes and braces");
        }
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"extra\":[{\"x\":\"}\\\"\"}],\"leaderboard\":{\"rows\":[],\"pagination\":{\"totalPages\":0,\"totalSize\":0}},\"seasonId\":19}")))
            Check(BoardProjection.Read(input).seasonId == 19, "projection tolerates root key order and unknown fields");
        bool wrongSeason = false;
        try { BoardApi.FromGlobal(new GlobalWire { seasonId = 18, leaderboard = new GlobalBoard { rows = new List<GlobalRow>(), pagination = new Pagination() } }, 1, 19); }
        catch (InvalidDataException) { wrongSeason = true; }
        Check(wrongSeason, "wrong season response rejected");
        var china = BoardApi.FromChina(Parse<ChinaWire>("{\"code\":0,\"data\":{\"list\":[{\"position\":26,\"battle_tag\":\"测试玩家\",\"score\":19730}],\"total\":500}}"), 2, 19);
        Check(china.TotalPages == 20 && china.Rows[0].Page == 2 && china.Rows[0].Score == 19730, "China independent schema, pagination and score");
        var c = new Config { Player = "shenshui", HintRank = 62 };
        var fake = new FakeApi { Handler = (config, page, token) => Page(page, page == 1 ? rows[0] : page == 3 ? rows[1] : Entry(30, "other", 2)) };
        var all = new RankSearch(fake).All(c, s => { }, CancellationToken.None);
        Check(all.Status == RankStatus.Ambiguous && all.Candidates.Count == 2 && all.Complete, "full scan finds duplicate across distant pages");
        fake.Handler = (config, page, token) => { if (page == 2) throw new IOException("network"); return Page(page, Entry(page, "other", page)); };
        var incomplete = new RankSearch(fake).All(c, s => { }, CancellationToken.None);
        Check(!incomplete.Complete && incomplete.Status == RankStatus.Searching && incomplete.Error.Length > 0, "failed pages cannot produce false not-found");
        var previous = Identity.Resolve("shenshui", new[] { Entry(25, "shenshui", 1) }, true, 156, 75); previous.TotalPages = 3;
        int requests = 0;
        fake.Handler = (config, page, token) => { Interlocked.Increment(ref requests); return Page(page, Entry(26, page == 2 ? "shenshui" : "other", page)); };
        var moved = new RankSearch(fake).Nearby(c, previous, CancellationToken.None);
        Check(moved.Candidates.Single().Rank == 26 && moved.Complete, "player crossing a page boundary is relocated");
        requests = 0; previous.Candidates[0] = Entry(26, "shenshui", 2);
        var stable = new RankSearch(fake).Nearby(c, previous, CancellationToken.None);
        Check(requests == 1 && stable.Candidates.Count == 1, "stable player uses one request per refresh");
        using (var cancel = new CancellationTokenSource())
        {
            fake.Handler = (config, page, token) => { cancel.Cancel(); return Page(page); };
            bool cancelled = false;
            try { new RankSearch(fake).All(c, s => { }, cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "cancel stops a full scan");
        }
        var cn = new Config { Region = "CN", Mode = "battlegroundsduo" };
        Check(BoardApi.PageUrl(cn, 2, 19).StartsWith("https://webapi.blizzard.cn/") && BoardApi.PageUrl(cn, 2, 19).Contains("mode_name=battlegroundsduo"), "China never falls back to international region CN");
        Check(!BoardApi.PageUrl(new Config(), 1, 0).Contains("seasonId="), "current global season selected by official server");
        Check(Parse<ChinaModeWire>("{\"code\":0,\"data\":{\"season_map\":{\"battlegrounds\":[{\"season_id\":19,\"season\":\"第14赛季\"}]}}}").data.season_map.battlegrounds[0].season_id == 19, "China season display number is distinct from API ID");
        using (var ready = new ManualResetEvent(false))
        using (var paused = new ManualResetEvent(false))
        {
            int outsideCachedPage = 0;
            fake.Handler = (config, page, token) => {
                if (page > 1) Interlocked.Increment(ref outsideCachedPage);
                return Page(page, Entry(10, page == 1 ? "shenshui" : "other", page));
            };
            using (var monitor = new RankMonitor(fake))
            {
                monitor.Changed += snapshot => { if (snapshot.Status == RankStatus.Ready) ready.Set(); if (snapshot.Status == RankStatus.Paused) paused.Set(); };
                monitor.Apply(c);
                Check(ready.WaitOne(3000), "monitor completes discovery");
                var off = c.Copy(); off.Monitoring = false; monitor.Apply(off);
                Check(paused.WaitOne(3000), "pause cancels polling and publishes paused state");
                int beforeResume = outsideCachedPage;
                ready.Reset(); monitor.Apply(c);
                Check(ready.WaitOne(3000) && outsideCachedPage == beforeResume, "resume reuses verified page cache");
            }
        }
    }
    static void Online()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var api = new BoardApi();
        foreach (string region in new[] { "AP", "US", "EU", "CN" })
        foreach (string mode in new[] { "standard", "wild", "battlegrounds", "battlegroundsduo" })
        {
            var c = new Config { Region = region, Mode = mode };
            int season = region == "CN" ? api.LatestChinaSeason(mode, CancellationToken.None) : 0;
            var page = api.Fetch(c, 1, season, CancellationToken.None);
            Check(page.Rows.Count > 0 && page.Season > 0 && page.Total > 0, "live " + region + " " + mode + " season=" + page.Season + " total=" + page.Total);
        }
    }
    static void Benchmark()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.DefaultConnectionLimit = 4;
        var watch = Stopwatch.StartNew();
        var result = new RankSearch(new BoardApi()).All(new Config { Player = "shenshui", HintRank = 5200 }, s => { }, CancellationToken.None);
        GC.Collect(2, GCCollectionMode.Forced);
        var process = Process.GetCurrentProcess(); process.Refresh();
        Console.WriteLine("BENCH complete=" + result.Complete + " status=" + result.Status + " pages=" + result.TotalPages
            + " elapsedSeconds=" + watch.Elapsed.TotalSeconds.ToString("0.0")
            + " privateMB=" + (process.PrivateMemorySize64 / 1048576.0).ToString("0.0")
            + " workingMB=" + (process.WorkingSet64 / 1048576.0).ToString("0.0")
            + " managedMB=" + (GC.GetTotalMemory(false) / 1048576.0).ToString("0.0"));
    }
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { Offline(); if (args.Contains("--online")) Online(); if (args.Contains("--benchmark")) Benchmark(); Console.WriteLine("Passed " + passed + " checks."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
