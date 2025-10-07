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

public class ContributorActivity
{
    public string Username { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int Commits { get; set; }
    public int PullRequests { get; set; }
    public int Issues { get; set; }
    public int TotalActivity => Commits + PullRequests + Issues;
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
            return;
        }

        // Get all-time data by setting a very early start date
        var toUtc = DateTimeOffset.UtcNow;
        var fromUtc = new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero); // VSCode started around 2015

        var client = new GitHubClient(new Octokit.ProductHeaderValue("RepoActivity"))
        {
            Credentials = new Credentials(token)
        };

        Console.WriteLine($"Analyzing top contributors for {Owner}/{Repo}...");
        Console.WriteLine($"Window: {fromUtc:yyyy-MM-dd} .. {toUtc:yyyy-MM-dd}\n");

        // Collect contributor data
        var contributors = await CollectContributorDataAsync(client, Owner, Repo, fromUtc, toUtc);

        // Get top 10 by total activity
        var top10 = contributors
            .OrderByDescending(c => c.TotalActivity)
            .Take(10)
            .ToList();

        // Display results
        Console.WriteLine("=== TOP 10 CONTRIBUTORS BY TOTAL ACTIVITY ===");
        Console.WriteLine($"{"Rank",-4} {"Username",-20} {"Name",-25} {"Commits",-8} {"PRs",-6} {"Issues",-7} {"Total",-6}");
        Console.WriteLine($"{new string('-', 4)} {new string('-', 20)} {new string('-', 25)} {new string('-', 8)} {new string('-', 6)} {new string('-', 7)} {new string('-', 6)}");

        for (int i = 0; i < top10.Count; i++)
        {
            var contributor = top10[i];
            var rank = (i + 1).ToString();
            var displayName = string.IsNullOrWhiteSpace(contributor.Name) ? contributor.Username : contributor.Name;
            if (displayName.Length > 24) displayName = displayName.Substring(0, 21) + "...";
            
            Console.WriteLine($"{rank,-4} {contributor.Username,-20} {displayName,-25} {contributor.Commits,-8} {contributor.PullRequests,-6} {contributor.Issues,-7} {contributor.TotalActivity,-6}");
        }

        // Additional breakdowns
        var topByCommits = contributors.OrderByDescending(c => c.Commits).Take(5).ToList();
        var topByPRs = contributors.OrderByDescending(c => c.PullRequests).Take(5).ToList();
        var topByIssues = contributors.OrderByDescending(c => c.Issues).Take(5).ToList();

        Console.WriteLine("\n=== TOP 5 BY INDIVIDUAL METRICS ===");
        
        Console.WriteLine("\nTop 5 Committers:");
        foreach (var contributor in topByCommits)
        {
            Console.WriteLine($"  {contributor.Username}: {contributor.Commits} commits");
        }

        Console.WriteLine("\nTop 5 PR Contributors:");
        foreach (var contributor in topByPRs)
        {
            Console.WriteLine($"  {contributor.Username}: {contributor.PullRequests} PRs");
        }

        Console.WriteLine("\nTop 5 Issue Contributors:");
        foreach (var contributor in topByIssues)
        {
            Console.WriteLine($"  {contributor.Username}: {contributor.Issues} issues");
        }

        // Save results
        await SaveContributorDataAsync(contributors, top10);
        Console.WriteLine($"\nSaved contributor analysis to results/contributors_{DateTime.UtcNow:yyyy-MM-dd}.json");
    }

    static async Task<List<ContributorActivity>> CollectContributorDataAsync(GitHubClient client, string owner, string repo, DateTimeOffset from, DateTimeOffset to)
    {
        var contributors = new Dictionary<string, ContributorActivity>();

        Console.WriteLine("Collecting commit data...");
        await CollectCommitContributorsAsync(client, owner, repo, from, to, contributors);

        Console.WriteLine("Collecting PR data...");
        await CollectPRContributorsAsync(client, owner, repo, from, to, contributors);

        Console.WriteLine("Collecting issue data...");
        await CollectIssueContributorsAsync(client, owner, repo, from, to, contributors);

        return contributors.Values.ToList();
    }

    static async Task CollectCommitContributorsAsync(GitHubClient client, string owner, string repo, DateTimeOffset from, DateTimeOffset to, Dictionary<string, ContributorActivity> contributors)
    {
        var page = 1;
        const int perPage = 100;
        var req = new CommitRequest { Since = from, Until = to };

        while (true)
        {
            var options = new ApiOptions { PageCount = 1, PageSize = perPage, StartPage = page };
            var batch = await client.Repository.Commit.GetAll(owner, repo, req, options);
            
            if (batch.Count == 0) break;

            foreach (var commit in batch)
            {
                if (commit.Author?.Login != null)
                {
                    var username = commit.Author.Login;
                    if (!contributors.ContainsKey(username))
                    {
                        contributors[username] = new ContributorActivity 
                        { 
                            Username = username
                        };
                    }
                    contributors[username].Commits++;
                }
            }

            if (batch.Count < perPage) break;
            page++;

            // Rate limiting
            if (page % 10 == 0)
            {
                Console.WriteLine($"  Processed {page * perPage} commits...");
                await Task.Delay(1000);
            }
        }
    }

    static async Task CollectPRContributorsAsync(GitHubClient client, string owner, string repo, DateTimeOffset from, DateTimeOffset to, Dictionary<string, ContributorActivity> contributors)
    {
        // Use search API for PRs created in date range
        var searchQuery = $"repo:{owner}/{repo} is:pr created:>={from:yyyy-MM-dd} created:<={to:yyyy-MM-dd}";
        var searchReq = new SearchIssuesRequest(searchQuery)
        {
            PerPage = 100,
            SortField = IssueSearchSort.Created,
            Order = SortDirection.Descending
        };

        var page = 1;
        while (page <= 10) // Limit to avoid hitting search API limits
        {
            searchReq.Page = page;
            var searchResults = await client.Search.SearchIssues(searchReq);
            
            if (searchResults.Items.Count == 0) break;

            foreach (var pr in searchResults.Items)
            {
                if (pr.User?.Login != null)
                {
                    var username = pr.User.Login;
                    if (!contributors.ContainsKey(username))
                    {
                        contributors[username] = new ContributorActivity 
                        { 
                            Username = username,
                            Name = null // Will be populated from PR/Issue data if available
                        };
                    }
                    // Update the name if we don't have it yet and this user object has it
                    if (contributors[username].Name == null && !string.IsNullOrEmpty(pr.User.Name))
                    {
                        contributors[username].Name = pr.User.Name;
                    }
                    contributors[username].PullRequests++;
                }
            }

            if (searchResults.Items.Count < 100) break;
            page++;
            await Task.Delay(2000); // Longer delay for search API
        }
    }

    static async Task CollectIssueContributorsAsync(GitHubClient client, string owner, string repo, DateTimeOffset from, DateTimeOffset to, Dictionary<string, ContributorActivity> contributors)
    {
        // Use search API for issues created in date range (excluding PRs)
        var searchQuery = $"repo:{owner}/{repo} is:issue created:>={from:yyyy-MM-dd} created:<={to:yyyy-MM-dd}";
        var searchReq = new SearchIssuesRequest(searchQuery)
        {
            PerPage = 100,
            SortField = IssueSearchSort.Created,
            Order = SortDirection.Descending
        };

        var page = 1;
        while (page <= 10) // Limit to avoid hitting search API limits
        {
            searchReq.Page = page;
            var searchResults = await client.Search.SearchIssues(searchReq);
            
            if (searchResults.Items.Count == 0) break;

            foreach (var issue in searchResults.Items)
            {
                if (issue.User?.Login != null)
                {
                    var username = issue.User.Login;
                    if (!contributors.ContainsKey(username))
                    {
                        contributors[username] = new ContributorActivity 
                        { 
                            Username = username,
                            Name = null // Will be populated from PR/Issue data if available
                        };
                    }
                    // Update the name if we don't have it yet and this user object has it
                    if (contributors[username].Name == null && !string.IsNullOrEmpty(issue.User.Name))
                    {
                        contributors[username].Name = issue.User.Name;
                    }
                    contributors[username].Issues++;
                }
            }

            if (searchResults.Items.Count < 100) break;
            page++;
            await Task.Delay(2000); // Longer delay for search API
        }
    }

    static async Task SaveContributorDataAsync(List<ContributorActivity> allContributors, List<ContributorActivity> top10)
    {
        var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
        Directory.CreateDirectory(resultsDir);

        var payload = new
        {
            repository = $"{Owner}/{Repo}",
            analysis_date = DateTime.UtcNow.ToString("o"),
            total_contributors = allContributors.Count,
            top_10_contributors = top10.Select(c => new
            {
                rank = top10.IndexOf(c) + 1,
                username = c.Username,
                name = c.Name,
                commits = c.Commits,
                pull_requests = c.PullRequests,
                issues = c.Issues,
                total_activity = c.TotalActivity
            }),
            summary = new
            {
                total_commits = allContributors.Sum(c => c.Commits),
                total_prs = allContributors.Sum(c => c.PullRequests),
                total_issues = allContributors.Sum(c => c.Issues)
            }
        };

        var jsonPath = Path.Combine(resultsDir, $"contributors_{DateTime.UtcNow:yyyy-MM-dd}.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        // Also save as CSV
        var csvPath = Path.Combine(resultsDir, $"top_contributors_{DateTime.UtcNow:yyyy-MM-dd}.csv");
        var csvLines = new List<string>
        {
            "Rank,Username,Name,Commits,PullRequests,Issues,TotalActivity"
        };

        for (int i = 0; i < top10.Count; i++)
        {
            var c = top10[i];
            csvLines.Add($"{i + 1},\"{c.Username}\",\"{c.Name ?? ""}\",{c.Commits},{c.PullRequests},{c.Issues},{c.TotalActivity}");
        }

        await File.WriteAllLinesAsync(csvPath, csvLines);
    }
}