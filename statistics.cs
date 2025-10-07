using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Octokit;
using DotNetEnv; // Add this using statement

class Program
{
    const string Owner = "microsoft";
    const string Repo  = "vscode";

    static async Task Main()
    {
        // Load environment variables from .env file
        Env.Load();
        
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Set GITHUB_TOKEN in .env file first.");
            Console.Error.WriteLine("Create a .env file in the project root with: GITHUB_TOKEN=your_token_here");
            return;
        }

        var toUtc   = DateTimeOffset.UtcNow;
        var fromUtc = toUtc.AddYears(-1);

        var client = new GitHubClient(new Octokit.ProductHeaderValue("RepoActivity"))
        {
            Credentials = new Credentials(token)
        };

        // 1) Commits created in last year (paginated)
        var commitCount = await CountCommitsAsync(client, Owner, Repo, fromUtc, toUtc);

        // 2) PRs created in last year (chunked by month to avoid 1000 Search cap)
        var prCount = await CountIssuesSearchChunkedAsync(client, $"repo:{Owner}/{Repo} is:pr", fromUtc, toUtc);

        // 3) Issues created in last year (excluding PRs, chunked by month)
        var issueCount = await CountIssuesSearchChunkedAsync(client, $"repo:{Owner}/{Repo} is:issue", fromUtc, toUtc);

        // 4) Actions workflow runs created in last year (uses REST total_count)
        var actionsCount = await CountWorkflowRunsAsync(token, Owner, Repo, fromUtc, toUtc);

        Console.WriteLine($"Repository: {Owner}/{Repo}");
        Console.WriteLine($"Window: {fromUtc:yyyy-MM-dd} .. {toUtc:yyyy-MM-dd}\n");
        Console.WriteLine($"Commits (created):        {commitCount}");
        Console.WriteLine($"Pull requests (created):  {prCount}");
        Console.WriteLine($"Issues (created):         {issueCount}");
        Console.WriteLine($"Actions workflow runs:    {actionsCount}");

        // Save results to JSON for your report
        var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
        Directory.CreateDirectory(resultsDir);
        var outPath = Path.Combine(resultsDir, $"activity_{DateTime.UtcNow:yyyy-MM-dd}.json");

        var payload = new
        {
            repository = $"{Owner}/{Repo}",
            window = new { from = fromUtc.ToString("o"), to = toUtc.ToString("o") },
            counts = new
            {
                commits = commitCount,
                pull_requests = prCount,
                issues = issueCount,
                actions_runs = actionsCount
            },
            generated_at_utc = DateTime.UtcNow.ToString("o")
        };

        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nSaved results → {outPath}");
    }

    static async Task<int> CountCommitsAsync(GitHubClient client, string owner, string repo, DateTimeOffset since, DateTimeOffset until)
    {
        var total = 0;
        var page = 1;
        const int perPage = 100;

        var req = new CommitRequest { Since = since, Until = until };

        while (true)
        {
            var options = new ApiOptions { PageCount = 1, PageSize = perPage, StartPage = page };
            var batch = await client.Repository.Commit.GetAll(owner, repo, req, options);
            if (batch.Count == 0) break;
            total += batch.Count;
            if (batch.Count < perPage) break;
            page++;
        }
        return total;
    }

    // Chunk by month to avoid Search API 1000-result cap; sums TotalCount over months.
    static async Task<int> CountIssuesSearchChunkedAsync(GitHubClient client, string baseQuery, DateTimeOffset from, DateTimeOffset to)
    {
        var monthStart = new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(to.Year, to.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        end = end.AddMonths(1); // exclusive upper bound

        var total = 0;
        for (var cursor = monthStart; cursor < end; cursor = cursor.AddMonths(1))
        {
            var monthFrom = cursor;
            var monthTo = cursor.AddMonths(1).AddTicks(-1); // inclusive-ish

            var q = $"{baseQuery} created:{monthFrom:yyyy-MM-dd}..{monthTo:yyyy-MM-dd}";
            var req = new SearchIssuesRequest(q) { PerPage = 1 };
            var res = await client.Search.SearchIssues(req);
            total += res.TotalCount;
        }
        return total;
    }

    // GET /repos/{owner}/{repo}/actions/runs?per_page=1&created=from..to → reads total_count
    static async Task<int> CountWorkflowRunsAsync(string token, string owner, string repo, DateTimeOffset from, DateTimeOffset to)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/actions/runs?per_page=1&created={from:yyyy-MM-dd}..{to:yyyy-MM-dd}";
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RepoActivity", "1.0"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var resp = await http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        return doc.RootElement.TryGetProperty("total_count", out var total)
            ? total.GetInt32()
            : 0;
    }
}