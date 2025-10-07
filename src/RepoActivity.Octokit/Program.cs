using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Octokit;
using DotNetEnv;

public class ContributorActivity
{
    public string Username { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Email { get; set; }
    public int Commits { get; set; }
    public int PullRequests { get; set; }
    public int Issues { get; set; }
    public int TotalActivity => Commits + PullRequests + Issues;
    
    // Bot detection fields
    public bool IsBot { get; set; }
    public List<string> BotIndicators { get; set; } = new List<string>();
    public List<string> CommitMessages { get; set; } = new List<string>();
    public double MessageSimilarityScore { get; set; }
}

public class BotDetector
{
    // Measure 1: Username pattern analysis
    private static readonly string[] BotPatterns = new[]
    {
        "bot", "Bot", "[bot]", "-bot", "BOT",
        "ci", "CI", "ci-", "-ci",
        "automated", "automation", "auto-",
        "dependabot", "renovate", "greenkeeper",
        "github-actions", "azure-pipelines", "travis",
        "deploy", "deployer", "builder"
    };

    public static bool IsLikelyBotByUsername(string username)
    {
        return BotPatterns.Any(pattern => username.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    public static List<string> GetUsernameIndicators(string username)
    {
        var indicators = new List<string>();
        foreach (var pattern in BotPatterns)
        {
            if (username.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                indicators.Add($"Username contains '{pattern}'");
            }
        }
        return indicators;
    }

    // Measure 2: Commit message uniformity analysis
    public static (bool isBot, double similarityScore, List<string> indicators) AnalyzeCommitMessages(List<string> messages)
    {
        var indicators = new List<string>();
        
        if (messages.Count < 5)
        {
            return (false, 0.0, indicators);
        }

        // Check for repetitive messages
        var messageGroups = messages.GroupBy(m => NormalizeMessage(m)).ToList();
        var mostCommonMessage = messageGroups.OrderByDescending(g => g.Count()).First();
        var similarityScore = (double)mostCommonMessage.Count() / messages.Count;

        // Check for automation keywords
        var automationKeywords = new[] 
        { 
            "automated", "auto-generated", "automatic",
            "bump version", "update dependencies", "merge branch",
            "[skip ci]", "[ci skip]", "version bump",
            "update package", "dependency update"
        };

        var automationCount = messages.Count(msg => 
            automationKeywords.Any(keyword => msg.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        );
        var automationRatio = (double)automationCount / messages.Count;

        // Determine if bot
        bool isBot = false;
        if (similarityScore > 0.8)
        {
            isBot = true;
            indicators.Add($"High message similarity: {similarityScore:P0} identical");
        }
        
        if (automationRatio > 0.7)
        {
            isBot = true;
            indicators.Add($"Automation keywords: {automationRatio:P0} of messages");
        }

        if (messages.All(m => m.Length < 30))
        {
            indicators.Add("All messages very short (< 30 chars)");
        }

        return (isBot, similarityScore, indicators);
    }

    private static string NormalizeMessage(string message)
    {
        // Remove version numbers, dates, and other variables to compare structure
        var normalized = Regex.Replace(message, @"\d+\.\d+\.\d+", "X.Y.Z"); // versions
        normalized = Regex.Replace(normalized, @"\d{4}-\d{2}-\d{2}", "DATE"); // dates
        normalized = Regex.Replace(normalized, @"#\d+", "#NUM"); // issue numbers
        normalized = Regex.Replace(normalized, @"\b[0-9a-f]{7,40}\b", "HASH"); // commit hashes
        return normalized.ToLowerInvariant().Trim();
    }

    // Bonus Measure 3: Activity pattern analysis
    public static List<string> AnalyzeActivityPatterns(List<DateTime> commitTimes)
    {
        var indicators = new List<string>();
        
        if (commitTimes.Count < 10)
        {
            return indicators;
        }

        // Check for extremely high frequency
        var timeSpan = commitTimes.Max() - commitTimes.Min();
        var commitsPerDay = commitTimes.Count / Math.Max(1, timeSpan.TotalDays);
        
        if (commitsPerDay > 50)
        {
            indicators.Add($"Very high frequency: {commitsPerDay:F1} commits/day");
        }

        // Check for perfect regularity (commits at exact intervals)
        var intervals = commitTimes
            .OrderBy(t => t)
            .Zip(commitTimes.OrderBy(t => t).Skip(1), (a, b) => (b - a).TotalMinutes)
            .ToList();

        if (intervals.Count > 5)
        {
            var avgInterval = intervals.Average();
            var variance = intervals.Average(i => Math.Pow(i - avgInterval, 2));
            var stdDev = Math.Sqrt(variance);
            
            // Very low variance suggests automated timing
            if (stdDev < 1.0 && avgInterval < 60) // Less than 1 minute variance
            {
                indicators.Add($"Highly regular commit timing: {avgInterval:F1}min intervals");
            }
        }

        return indicators;
    }
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
        var fromUtc = new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var client = new GitHubClient(new Octokit.ProductHeaderValue("RepoActivity"))
        {
            Credentials = new Credentials(token)
        };

        Console.WriteLine($"Analyzing top contributors for {Owner}/{Repo}...");
        Console.WriteLine($"Window: {fromUtc:yyyy-MM-dd} .. {toUtc:yyyy-MM-dd}\n");

        // Collect contributor data
        var contributors = await CollectContributorDataAsync(client, Owner, Repo, fromUtc, toUtc);

        // Apply bot detection
        Console.WriteLine("\nApplying bot detection measures...");
        await ApplyBotDetectionAsync(contributors);

        // Separate bots from humans
        var bots = contributors.Where(c => c.IsBot).ToList();
        var humans = contributors.Where(c => !c.IsBot).ToList();

        Console.WriteLine($"\nBot Detection Results:");
        Console.WriteLine($"  Total contributors: {contributors.Count}");
        Console.WriteLine($"  Identified bots: {bots.Count} ({(double)bots.Count / contributors.Count:P1})");
        Console.WriteLine($"  Human contributors: {humans.Count}");
        Console.WriteLine($"  Bot commits: {bots.Sum(b => b.Commits)} ({(double)bots.Sum(b => b.Commits) / contributors.Sum(c => c.Commits):P1})");

        // Show identified bots
        if (bots.Any())
        {
            Console.WriteLine("\n=== IDENTIFIED BOTS ===");
            foreach (var bot in bots.OrderByDescending(b => b.Commits).Take(10))
            {
                Console.WriteLine($"  {bot.Username}: {bot.Commits} commits");
                foreach (var indicator in bot.BotIndicators)
                {
                    Console.WriteLine($"    - {indicator}");
                }
            }
        }

        // Get top 10 HUMAN contributors by total activity
        var top10 = humans
            .OrderByDescending(c => c.TotalActivity)
            .Take(10)
            .ToList();

        // Display results (humans only)
        Console.WriteLine("\n=== TOP 10 HUMAN CONTRIBUTORS BY TOTAL ACTIVITY ===");
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

        // Additional breakdowns (humans only)
        var topByCommits = humans.OrderByDescending(c => c.Commits).Take(5).ToList();
        var topByPRs = humans.OrderByDescending(c => c.PullRequests).Take(5).ToList();
        var topByIssues = humans.OrderByDescending(c => c.Issues).Take(5).ToList();

        Console.WriteLine("\n=== TOP 5 HUMAN CONTRIBUTORS BY INDIVIDUAL METRICS ===");
        
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
        await SaveContributorDataAsync(contributors, humans, bots, top10);
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
                            Username = username,
                            Email = commit.Commit?.Author?.Email
                        };
                    }
                    contributors[username].Commits++;
                    
                    // Collect commit message for bot detection
                    if (commit.Commit?.Message != null && contributors[username].CommitMessages.Count < 50)
                    {
                        contributors[username].CommitMessages.Add(commit.Commit.Message);
                    }
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
        var searchQuery = $"repo:{owner}/{repo} is:pr created:>={from:yyyy-MM-dd} created:<={to:yyyy-MM-dd}";
        var searchReq = new SearchIssuesRequest(searchQuery)
        {
            PerPage = 100,
            SortField = IssueSearchSort.Created,
            Order = SortDirection.Descending
        };

        var page = 1;
        while (page <= 10)
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
                            Name = null
                        };
                    }
                    if (contributors[username].Name == null && !string.IsNullOrEmpty(pr.User.Name))
                    {
                        contributors[username].Name = pr.User.Name;
                    }
                    contributors[username].PullRequests++;
                }
            }

            if (searchResults.Items.Count < 100) break;
            page++;
            await Task.Delay(2000);
        }
    }

    static async Task CollectIssueContributorsAsync(GitHubClient client, string owner, string repo, DateTimeOffset from, DateTimeOffset to, Dictionary<string, ContributorActivity> contributors)
    {
        var searchQuery = $"repo:{owner}/{repo} is:issue created:>={from:yyyy-MM-dd} created:<={to:yyyy-MM-dd}";
        var searchReq = new SearchIssuesRequest(searchQuery)
        {
            PerPage = 100,
            SortField = IssueSearchSort.Created,
            Order = SortDirection.Descending
        };

        var page = 1;
        while (page <= 10)
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
                            Name = null
                        };
                    }
                    if (contributors[username].Name == null && !string.IsNullOrEmpty(issue.User.Name))
                    {
                        contributors[username].Name = issue.User.Name;
                    }
                    contributors[username].Issues++;
                }
            }

            if (searchResults.Items.Count < 100) break;
            page++;
            await Task.Delay(2000);
        }
    }

    static async Task ApplyBotDetectionAsync(List<ContributorActivity> contributors)
    {
        foreach (var contributor in contributors)
        {
            // Measure 1: Username pattern analysis
            if (BotDetector.IsLikelyBotByUsername(contributor.Username))
            {
                contributor.IsBot = true;
                contributor.BotIndicators.AddRange(BotDetector.GetUsernameIndicators(contributor.Username));
            }

            // Measure 2: Commit message uniformity (only for contributors with commits)
            if (contributor.CommitMessages.Any())
            {
                var (isBot, score, indicators) = BotDetector.AnalyzeCommitMessages(contributor.CommitMessages);
                contributor.MessageSimilarityScore = score;
                
                if (isBot)
                {
                    contributor.IsBot = true;
                    contributor.BotIndicators.AddRange(indicators);
                }
            }

            // If still not identified as bot but has suspicious characteristics
            if (!contributor.IsBot && contributor.BotIndicators.Any())
            {
                // Require multiple indicators for edge cases
            }
        }

        await Task.CompletedTask;
    }

    static async Task SaveContributorDataAsync(
        List<ContributorActivity> allContributors, 
        List<ContributorActivity> humans,
        List<ContributorActivity> bots,
        List<ContributorActivity> top10)
    {
        var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
        Directory.CreateDirectory(resultsDir);

        var payload = new
        {
            repository = $"{Owner}/{Repo}",
            analysis_date = DateTime.UtcNow.ToString("o"),
            bot_detection = new
            {
                total_contributors = allContributors.Count,
                identified_bots = bots.Count,
                bot_percentage = (double)bots.Count / allContributors.Count,
                human_contributors = humans.Count,
                bot_commits = bots.Sum(b => b.Commits),
                bot_commit_percentage = (double)bots.Sum(b => b.Commits) / allContributors.Sum(c => c.Commits),
                top_bots = bots.OrderByDescending(b => b.Commits).Take(10).Select(b => new
                {
                    username = b.Username,
                    commits = b.Commits,
                    indicators = b.BotIndicators
                })
            },
            top_10_human_contributors = top10.Select(c => new
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
                total_commits_all = allContributors.Sum(c => c.Commits),
                total_commits_human = humans.Sum(c => c.Commits),
                total_prs = humans.Sum(c => c.PullRequests),
                total_issues = humans.Sum(c => c.Issues)
            }
        };

        var jsonPath = Path.Combine(resultsDir, $"contributors_{DateTime.UtcNow:yyyy-MM-dd}.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        // Save human contributors CSV
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

        // Save bots CSV
        var botCsvPath = Path.Combine(resultsDir, $"identified_bots_{DateTime.UtcNow:yyyy-MM-dd}.csv");
        var botCsvLines = new List<string>
        {
            "Username,Commits,PullRequests,Issues,Indicators"
        };

        foreach (var bot in bots.OrderByDescending(b => b.Commits))
        {
            var indicators = string.Join("; ", bot.BotIndicators);
            botCsvLines.Add($"\"{bot.Username}\",{bot.Commits},{bot.PullRequests},{bot.Issues},\"{indicators}\"");
        }

        await File.WriteAllLinesAsync(botCsvPath, botCsvLines);
    }
}