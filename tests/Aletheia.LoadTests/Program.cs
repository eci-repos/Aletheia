using System.Diagnostics;

var apiBase = Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:8080";
var concurrency = int.TryParse(Environment.GetEnvironmentVariable("LOADTEST_CONCURRENCY"), out var c) ? c : 10;
var durationSeconds = int.TryParse(Environment.GetEnvironmentVariable("LOADTEST_DURATION"), out var d) ? d : 30;
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

Console.WriteLine($"Aletheia Load Test");
Console.WriteLine($"Target: {apiBase}");
Console.WriteLine($"Concurrency: {concurrency}");
Console.WriteLine($"Duration: {durationSeconds}s");
Console.WriteLine();

var endpoints = new[]
{
    "/api/files",
    "/api/search",
    "/api/metadata",
    "/api/versions",
    "/api/graph",
    "/api/rags",
    "/api/copilot",
    "/api/graphrag",
    "/api/lazygraphrag",
    "/api/ontology",
    "/api/taxonomy",
    "/api/collaboration",
    "/api/governance"
};

var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
var counter = new Counter();
var tasks = new List<Task>();

var sw = Stopwatch.StartNew();

for (int i = 0; i < concurrency; i++)
{
    tasks.Add(WorkerAsync(http, apiBase, endpoints, counter, cts.Token));
}

await Task.WhenAll(tasks);
sw.Stop();

Console.WriteLine();
Console.WriteLine("=== Results ===");
Console.WriteLine($"Total Requests: {counter.Total}");
Console.WriteLine($"Successful:     {counter.Success}");
Console.WriteLine($"Failed:         {counter.Failed}");
Console.WriteLine($"Duration:       {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"RPS:            {counter.Total / sw.Elapsed.TotalSeconds:F2}");
Console.WriteLine($"P50 Latency:    {counter.GetPercentile(0.50):F0}ms");
Console.WriteLine($"P95 Latency:    {counter.GetPercentile(0.95):F0}ms");
Console.WriteLine($"P99 Latency:    {counter.GetPercentile(0.99):F0}ms");

static async Task WorkerAsync(HttpClient http, string baseUrl, string[] endpoints, Counter counter, CancellationToken ct)
{
    var rand = new Random();
    while (!ct.IsCancellationRequested)
    {
        var endpoint = endpoints[rand.Next(endpoints.Length)];
        var url = baseUrl.TrimEnd('/') + endpoint;
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await http.GetAsync(url, ct);
            sw.Stop();
            counter.Record(response.IsSuccessStatusCode, sw.ElapsedMilliseconds);
        }
        catch
        {
            sw.Stop();
            counter.Record(false, sw.ElapsedMilliseconds);
        }
    }
}

class Counter
{
    private long _total;
    private long _success;
    private long _failed;
    private readonly List<long> _latencies = new();
    private readonly object _lock = new();

    public long Total => Interlocked.Read(ref _total);
    public long Success => Interlocked.Read(ref _success);
    public long Failed => Interlocked.Read(ref _failed);

    public void Record(bool success, long latencyMs)
    {
        Interlocked.Increment(ref _total);
        if (success)
            Interlocked.Increment(ref _success);
        else
            Interlocked.Increment(ref _failed);

        lock (_lock)
        {
            _latencies.Add(latencyMs);
        }
    }

    public double GetPercentile(double p)
    {
        lock (_lock)
        {
            if (_latencies.Count == 0) return 0;
            var sorted = _latencies.OrderBy(x => x).ToList();
            int index = (int)Math.Ceiling(sorted.Count * p) - 1;
            if (index < 0) index = 0;
            return sorted[index];
        }
    }
}
