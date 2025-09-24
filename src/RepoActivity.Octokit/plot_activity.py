import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.dates as mdates
from datetime import datetime
import numpy as np

def load_and_plot_activity(csv_file):
    # Load the data
    df = pd.read_csv(csv_file)
    df['Month'] = pd.to_datetime(df['Month'])
    df = df.sort_values('Month')
    
    # Create figure with subplots
    fig, ((ax1, ax2), (ax3, ax4)) = plt.subplots(2, 2, figsize=(15, 12))
    fig.suptitle('GitHub Repository Activity Over Time\n(Microsoft/VSCode)', fontsize=16, fontweight='bold')
    
    # Plot 1: Commits
    ax1.plot(df['Month'], df['Commits'], marker='o', linewidth=2, markersize=6, color='#2E8B57')
    ax1.set_title('Commits Created Per Month', fontweight='bold')
    ax1.set_ylabel('Number of Commits')
    ax1.grid(True, alpha=0.3)
    ax1.tick_params(axis='x', rotation=45)
    
    # Plot 2: Pull Requests
    ax2.plot(df['Month'], df['PullRequests'], marker='s', linewidth=2, markersize=6, color='#4169E1')
    ax2.set_title('Pull Requests Created Per Month', fontweight='bold')
    ax2.set_ylabel('Number of Pull Requests')
    ax2.grid(True, alpha=0.3)
    ax2.tick_params(axis='x', rotation=45)
    
    # Plot 3: Issues
    ax3.plot(df['Month'], df['Issues'], marker='^', linewidth=2, markersize=6, color='#DC143C')
    ax3.set_title('Issues Created Per Month', fontweight='bold')
    ax3.set_ylabel('Number of Issues')
    ax3.grid(True, alpha=0.3)
    ax3.tick_params(axis='x', rotation=45)
    
    # Plot 4: Actions Runs
    ax4.plot(df['Month'], df['ActionsRuns'], marker='D', linewidth=2, markersize=6, color='#FF8C00')
    ax4.set_title('GitHub Actions Runs Per Month', fontweight='bold')
    ax4.set_ylabel('Number of Actions Runs')
    ax4.grid(True, alpha=0.3)
    ax4.tick_params(axis='x', rotation=45)
    
    # Format x-axis for all subplots
    for ax in [ax1, ax2, ax3, ax4]:
        ax.xaxis.set_major_formatter(mdates.DateFormatter('%Y-%m'))
        ax.xaxis.set_major_locator(mdates.MonthLocator(interval=2))
    
    plt.tight_layout()
    plt.savefig('github_activity_breakdown.png', dpi=300, bbox_inches='tight')
    plt.show()
    
    # Create combined plot
    fig2, ax = plt.subplots(1, 1, figsize=(14, 8))
    
    # Normalize the data to show relative patterns (since Actions runs are much higher)
    ax.plot(df['Month'], df['Commits'], marker='o', linewidth=2, label='Commits', color='#2E8B57')
    ax.plot(df['Month'], df['PullRequests'], marker='s', linewidth=2, label='Pull Requests', color='#4169E1')
    ax.plot(df['Month'], df['Issues'], marker='^', linewidth=2, label='Issues', color='#DC143C')
    
    # Plot Actions runs on secondary y-axis due to scale difference
    ax2 = ax.twinx()
    ax2.plot(df['Month'], df['ActionsRuns'], marker='D', linewidth=2, label='Actions Runs', color='#FF8C00')
    ax2.set_ylabel('GitHub Actions Runs', color='#FF8C00', fontweight='bold')
    ax2.tick_params(axis='y', labelcolor='#FF8C00')
    
    ax.set_title('GitHub Repository Activity Over Time - Combined View\n(Microsoft/VSCode)', fontsize=14, fontweight='bold')
    ax.set_xlabel('Month', fontweight='bold')
    ax.set_ylabel('Number of Activities', fontweight='bold')
    ax.grid(True, alpha=0.3)
    
    # Format x-axis
    ax.xaxis.set_major_formatter(mdates.DateFormatter('%Y-%m'))
    ax.xaxis.set_major_locator(mdates.MonthLocator(interval=2))
    ax.tick_params(axis='x', rotation=45)
    
    # Legends
    ax.legend(loc='upper left')
    ax2.legend(loc='upper right')
    
    plt.tight_layout()
    plt.savefig('github_activity_combined.png', dpi=300, bbox_inches='tight')
    plt.show()
    
    # Print analysis
    print("\n=== ACTIVITY ANALYSIS ===")
    print(f"Data period: {df['Month'].min().strftime('%Y-%m')} to {df['Month'].max().strftime('%Y-%m')}")
    
    totals = {
        'Commits': df['Commits'].sum(),
        'Pull Requests': df['PullRequests'].sum(),
        'Issues': df['Issues'].sum(),
        'Actions Runs': df['ActionsRuns'].sum()
    }
    
    print(f"\nTotal Activities:")
    for activity, total in totals.items():
        print(f"  {activity}: {total:,}")
    
    print(f"\nMonthly Averages:")
    for activity, total in totals.items():
        avg = total / len(df)
        print(f"  {activity}: {avg:.1f}")
    
    # Calculate trends
    print(f"\nActivity Balance:")
    total_dev_activities = totals['Commits'] + totals['Pull Requests']
    total_project_mgmt = totals['Issues']
    total_automation = totals['Actions Runs']
    
    print(f"  Development (Commits + PRs): {total_dev_activities:,} ({total_dev_activities/(sum(totals.values())-total_automation)*100:.1f}% of non-automation)")
    print(f"  Project Management (Issues): {total_project_mgmt:,} ({total_project_mgmt/(sum(totals.values())-total_automation)*100:.1f}% of non-automation)")
    print(f"  Automation (Actions): {total_automation:,} ({total_automation/sum(totals.values())*100:.1f}% of all activities)")
    
    # Peak months
    peak_commits_month = df.loc[df['Commits'].idxmax(), 'Month'].strftime('%Y-%m')
    peak_prs_month = df.loc[df['PullRequests'].idxmax(), 'Month'].strftime('%Y-%m')
    peak_issues_month = df.loc[df['Issues'].idxmax(), 'Month'].strftime('%Y-%m')
    peak_actions_month = df.loc[df['ActionsRuns'].idxmax(), 'Month'].strftime('%Y-%m')
    
    print(f"\nPeak Activity Months:")
    print(f"  Commits: {peak_commits_month} ({df['Commits'].max()} commits)")
    print(f"  Pull Requests: {peak_prs_month} ({df['PullRequests'].max()} PRs)")
    print(f"  Issues: {peak_issues_month} ({df['Issues'].max()} issues)")
    print(f"  Actions: {peak_actions_month} ({df['ActionsRuns'].max()} runs)")

if __name__ == "__main__":
    # Update this path to match your CSV file
    csv_file = "results/monthly_activity_2025-09-17.csv"  # Adjust date as needed
    
    try:
        load_and_plot_activity(csv_file)
    except FileNotFoundError:
        print(f"CSV file not found: {csv_file}")
        print("Please run the C# program first to generate the monthly data.")
    except Exception as e:
        print(f"Error: {e}")