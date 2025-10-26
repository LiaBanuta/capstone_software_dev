from github import Github, Auth
from datetime import datetime, timezone
import statistics
import os

# Your GitHub token
TOKEN = "your_token_here"
REPO_NAME = "microsoft/vscode"

auth = Auth.Token(TOKEN)
g = Github(auth=auth)
repo = g.get_repo("microsoft/vscode")

print("Fast Metrics Collection")
print("=" * 50)
    
# Use search API with date filters (MUCH FASTER)
since = "2024-10-25"
until = "2025-10-25"

# ============================================
# METRIC 2: PR Cycle Time (Sample-based)
# ============================================
print("\n1. PR Cycle Time (sampling 500 merged PRs)...")
query = f"repo:microsoft/vscode is:pr is:merged merged:{since}..{until}"
prs = g.search_issues(query, sort="created", order="desc")

cycle_times = []
count = 0
max_sample = 500  # Sample 500 PRs instead of all

for pr in prs:
    if count >= max_sample:
        break
    
    # Get the actual PR object to access merge time
    pr_obj = repo.get_pull(pr.number)
    if pr_obj.merged_at:
        cycle_time = (pr_obj.merged_at - pr_obj.created_at).days
        cycle_times.append(cycle_time)
        count += 1
    
    if count % 50 == 0:
        print(f"   Processed {count} PRs...")

if cycle_times:
    print(f"\n   ✓ Median PR Cycle Time: {statistics.median(cycle_times):.1f} days")
    print(f"   ✓ Mean PR Cycle Time: {statistics.mean(cycle_times):.1f} days")
    print(f"   ✓ Sample size: {len(cycle_times)} PRs")

# ============================================
# METRIC 3: PR Response Time (Sample-based)
# ============================================
print("\n2. PR Response Time (sampling 300 PRs)...")
query = f"repo:microsoft/vscode is:pr created:{since}..{until}"
prs = g.search_issues(query, sort="created", order="desc")

response_times = []
count = 0
max_sample = 300

for pr in prs:
    if count >= max_sample:
        break
    
    pr_obj = repo.get_pull(pr.number)
    first_response = None
    
    # Check comments
    comments = list(pr_obj.get_issue_comments())
    if comments:
        first_response = comments[0].created_at
    
    # Check reviews
    reviews = list(pr_obj.get_reviews())
    if reviews:
        first_review = reviews[0].submitted_at
        if not first_response or first_review < first_response:
            first_response = first_review
    
    if first_response and first_response > pr_obj.created_at:
        response_time = (first_response - pr_obj.created_at).total_seconds() / 3600
        response_times.append(response_time)
    
    count += 1
    if count % 50 == 0:
        print(f"   Processed {count} PRs...")

if response_times:
    print(f"\n   ✓ Median PR Response Time: {statistics.median(response_times) / 24:.1f} days")
    print(f"   ✓ Mean PR Response Time: {statistics.mean(response_times) / 24:.1f} days")

# ============================================
# METRIC 4: PR Iteration Count (Sample-based)
# ============================================
print("\n3. PR Iteration Count (sampling 300 merged PRs)...")
query = f"repo:microsoft/vscode is:pr is:merged merged:{since}..{until}"
prs = g.search_issues(query, sort="created", order="desc")

iteration_counts = []
count = 0
max_sample = 300

for pr in prs:
    if count >= max_sample:
        break
    
    pr_obj = repo.get_pull(pr.number)
    iteration_counts.append(pr_obj.commits)
    count += 1
    
    if count % 50 == 0:
        print(f"   Processed {count} PRs...")

if iteration_counts:
    print(f"\n   ✓ Average PR Iteration Count: {statistics.mean(iteration_counts):.1f} commits")
    print(f"   ✓ Median PR Iteration Count: {statistics.median(iteration_counts):.0f} commits")

# ============================================
# METRIC 5: Review Coverage (Sample-based)
# ============================================
print("\n4. Review Coverage (sampling 300 merged PRs)...")
query = f"repo:microsoft/vscode is:pr is:merged merged:{since}..{until}"
prs = g.search_issues(query, sort="created", order="desc")

multiple_reviewer_count = 0
total_sampled = 0
max_sample = 300

for pr in prs:
    if total_sampled >= max_sample:
        break
    
    pr_obj = repo.get_pull(pr.number)
    reviewers = set()
    for review in pr_obj.get_reviews():
        if review.user:
            reviewers.add(review.user.login)
    
    if len(reviewers) >= 2:
        multiple_reviewer_count += 1
    
    total_sampled += 1
    if total_sampled % 50 == 0:
        print(f"   Processed {total_sampled} PRs...")

review_coverage = (multiple_reviewer_count / total_sampled) * 100
print(f"\n   ✓ Review Coverage: {review_coverage:.1f}%")
print(f"   ✓ PRs with 2+ reviewers: {multiple_reviewer_count}/{total_sampled}")

# ============================================
# METRIC 6: Review Comment Depth (Sample-based)
# ============================================
print("\n5. Review Comment Depth (sampling 200 PRs)...")
query = f"repo:microsoft/vscode is:pr created:{since}..{until}"
prs = g.search_issues(query, sort="created", order="desc")

total_comments = 0
total_prs = 0
max_sample = 200

for pr in prs:
    if total_prs >= max_sample:
        break
    
    pr_obj = repo.get_pull(pr.number)
    total_comments += pr_obj.comments + pr_obj.review_comments
    total_prs += 1
    
    if total_prs % 50 == 0:
        print(f"   Processed {total_prs} PRs...")

avg_comments = total_comments / total_prs
print(f"\n   ✓ Average Review Comments per PR: {avg_comments:.1f}")
print(f"   ✓ Sample size: {total_prs} PRs")

print("\n" + "=" * 50)
print("Collection complete! (Took ~10-15 minutes)")