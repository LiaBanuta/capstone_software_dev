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
using DotNetEnv;

public class ActivityData
{
    public DateTime Month { get; set; }
    public int Commits { get; set; }
    public int PullRequests { get; set; }
    public int Issues { get; set; }
    public int ActionsRuns { get; set; }
}

class Program
{
    const string Owner = "microsoft";
    const string Repo = "vscode";

    static async Task Main()
    {
        Env.Load();
        
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Set GITHUB_TOKEN in .env file first.");
            Console.Error.WriteLine("Create a .env file in the project root with: GITHUB_TOKEN=your_token_here");
            return;
        }

        var toUtc = DateTimeOffset.UtcNow;
        var fromUtc = toUtc.AddYears(-1);

        var client = new GitHubClient(new Octokit.ProductHeaderValue("RepoActivity"))
        {
            Credentials = new Credentials(token)
        };

        Console.WriteLine($"Collecting monthly data for {Owner}/{Repo}...");
        Console.WriteLine($"Window: {fromUtc:yyyy-MM-dd} .. {toUtc:yyyy-MM-dd}\n");

        // Collect monthly data
        var monthlyData = await CollectMonthlyDataAsync(client, token, Owner, Repo, fromUtc, toUtc);

        // Display totals
        var totalCommits = monthlyData.Sum(x => x.Commits);
        var totalPRs = monthlyData.Sum(x => x.PullRequests);
        var totalIssues = monthlyData.Sum(x => x.Issues);
        var totalActions = monthlyData.Sum(x => x.ActionsRuns);

        Console.WriteLine("=== TOTALS ===");
        Console.WriteLine($"Commits (created):        {totalCommits}");
        Console.WriteLine($"Pull requests (created):  {totalPRs}");
        Console.WriteLine($"Issues (created):         {totalIssues}");
        Console.WriteLine($"Actions workflow runs:    {totalActions}");

        // Display monthly breakdown
        Console.WriteLine("\n=== MONTHLY BREAKDOWN ===");
        Console.WriteLine("Month\t\tCommits\tPRs\tIssues\tActions");
        Console.WriteLine("------\t\t-------\t---\t------\t-------");
        foreach (var data in monthlyData.OrderBy(x => x.Month))
        {
            Console.WriteLine($"{data.Month:yyyy-MM}\t\t{data.Commits}\t{data.PullRequests}\t{data.Issues}\t{data.ActionsRuns}");
        }

        // Save detailed results to JSON
        var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
        Directory.CreateDirectory(resultsDir);
        var outPath = Path.Combine(resultsDir, $"monthly_activity_{DateTime.UtcNow:yyyy-MM-dd}.json");

        var payload = new
        {
            repository = $"{Owner}/{Repo}",
            window = new { from = fromUtc.ToString("o"), to = toUtc.ToString("o") },
            totals = new
            {
                commits = totalCommits,
                pull_requests = totalPRs,
                issues = totalIssues,
                actions_runs = totalActions
            },
            monthly_data = monthlyData.OrderBy(x => x.Month).Select(x => new
            {
                month = x.Month.ToString("yyyy-MM"),
                commits = x.Commits,
                pull_requests = x.PullRequests,
                issues = x.Issues,
                actions_runs = x.ActionsRuns
            }),
            generated_at_utc = DateTime.UtcNow.ToString("o")
        };

        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nSaved detailed results → {outPath}");

        // Also save CSV for easy plotting
        var csvPath = Path.Combine(resultsDir, $"monthly_activity_{DateTime.UtcNow:yyyy-MM-dd}.csv");
        await SaveToCsvAsync(csvPath, monthlyData);
        Console.WriteLine($"Saved CSV for plotting → {csvPath}");
    }

    static async Task<List<ActivityData>> CollectMonthlyDataAsync(GitHubClient client, string token, string owner, string repo, DateTimeOffset from, DateTimeOffset to)
    {
        var monthlyData = new List<ActivityData>();
        
        var monthStart = new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(to.Year, to.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        end = end.AddMonths(1); // exclusive upper bound

        for (var cursor = monthStart; cursor < end; cursor = cursor.AddMonths(1))
        {
            var monthFrom = cursor;
            var monthTo = cursor.AddMonths(1).AddTicks(-1);

            Console.WriteLine($"Processing {monthFrom:yyyy-MM}...");

            var data = new ActivityData { Month = monthFrom };

            // Get commits for this month
            data.Commits = await CountCommitsForMonthAsync(client, owner, repo, monthFrom, monthTo);

            // Get PRs for this month
            var prQuery = $"repo:{owner}/{repo} is:pr created:{monthFrom:yyyy-MM-dd}..{monthTo:yyyy-MM-dd}";
            var prReq = new SearchIssuesRequest(prQuery) { PerPage = 1 };
            var prRes = await client.Search.SearchIssues(prReq);
            data.PullRequests = prRes.TotalCount;

            // Get Issues for this month (excluding PRs)
            var issueQuery = $"repo:{owner}/{repo} is:issue created:{monthFrom:yyyy-MM-dd}..{monthTo:yyyy-MM-dd}";
            var issueReq = new SearchIssuesRequest(issueQuery) { PerPage = 1 };
            var issueRes = await client.Search.SearchIssues(issueReq);
            data.Issues = issueRes.TotalCount;

            // Get Actions runs for this month
            data.ActionsRuns = await CountWorkflowRunsForMonthAsync(token, owner, repo, monthFrom, monthTo);

            monthlyData.Add(data);
            
            // Small delay to be nice to the API
            await Task.Delay(1000);
        }

        return monthlyData;
    }

    static async Task<int> CountCommitsForMonthAsync(GitHubClient client, string owner, string repo, DateTime since, DateTime until)
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

    static async Task<int> CountWorkflowRunsForMonthAsync(string token, string owner, string repo, DateTime from, DateTime to)
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

    static async Task SaveToCsvAsync(string filePath, List<ActivityData> data)
    {
        var lines = new List<string>
        {
            "Month,Commits,PullRequests,Issues,ActionsRuns"
        };

        foreach (var item in data.OrderBy(x => x.Month))
        {
            lines.Add($"{item.Month:yyyy-MM},{item.Commits},{item.PullRequests},{item.Issues},{item.ActionsRuns}");
        }

        await File.WriteAllLinesAsync(filePath, lines);
    }
}