#!/usr/bin/env python3
"""Refresh the figures in index.html from the GitHub API.

Two kinds of element are updated, both marked in the HTML so this script never
has to guess:

    <span class="lib-stars" data-repo="BUTR/Bannerlord.Module.Template">★ 82</span>
    <dd data-stat="repos">61</dd>

`repos` counts repositories BUTR actually authored. Forks are excluded — a large
share of the org's public repos are forks of other people's projects, and
counting them overstates what the team builds.

Exits 0 whether or not anything changed; the workflow decides what to commit.
"""

from __future__ import annotations

from datetime import datetime, timezone
import json
import os
import pathlib
import re
import sys
import urllib.error
import urllib.request

ORG = "BUTR"
HTML = pathlib.Path(__file__).resolve().parent.parent / "index.html"
API = "https://api.github.com"


def fetch_org_repos(org: str) -> list[dict]:
    """Every public repo in the org, following pagination."""
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": f"{org}-site-stats",
        "X-GitHub-Api-Version": "2022-11-28",
    }
    # GITHUB_TOKEN in Actions lifts the rate limit from 60/hr to 1000/hr.
    token = os.environ.get("GITHUB_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    repos: list[dict] = []
    for page in range(1, 21):  # 2000 repos is far beyond any plausible size
        url = f"{API}/orgs/{org}/repos?type=public&per_page=100&page={page}"
        req = urllib.request.Request(url, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                batch = json.load(resp)
        except urllib.error.HTTPError as exc:
            sys.exit(f"GitHub API returned {exc.code} for {url}: {exc.read()[:200]!r}")
        except urllib.error.URLError as exc:
            sys.exit(f"Could not reach the GitHub API: {exc.reason}")
        if not batch:
            break
        repos.extend(batch)
        if len(batch) < 100:
            break
    return repos



def update_versions(html: str) -> str:
    """Update both tracked releases atomically; keep the last snapshot if unavailable."""
    versions = {
        "stable": os.environ.get("GAME_VERSION_STABLE", "").strip(),
        "beta": os.environ.get("GAME_VERSION_BETA", "").strip(),
    }
    if not all(versions.values()):
        print("Version variables unavailable; keeping the previous versions and check date.")
        return html
    # Accept Bannerlord version strings, never arbitrary markup or multiline input.
    for channel, value in versions.items():
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.+_-]{0,63}", value):
            raise ValueError(f"Invalid {channel} game version")
    versions["checked"] = datetime.now(timezone.utc).strftime("%Y-%m-%d UTC")
    for key, value in versions.items():
        pattern = rf'(?P<open><(?:dd|span) data-version="{key}">)[^<]*(?P<close></(?:dd|span)>)'
        html, count = re.subn(pattern, lambda m: m["open"] + value + m["close"], html)
        if count != 1:
            raise ValueError(f"Expected exactly one {key} version marker")
    return html


def main() -> int:
    repos = fetch_org_repos(ORG)
    if not repos:
        sys.exit("GitHub returned no repositories — refusing to rewrite the page.")

    stars = {r["full_name"]: r["stargazers_count"] for r in repos}
    authored = sum(1 for r in repos if not r["fork"])

    html = original = HTML.read_text(encoding="utf-8")
    changes: list[str] = []

    def replace_stars(match: re.Match[str]) -> str:
        repo = match.group("repo")
        if repo not in stars:
            print(f"  ! {repo} not found in the org listing — left untouched")
            return match.group(0)
        new = f"★ {stars[repo]}"
        if match.group("value") != new:
            changes.append(f"{repo}: {match.group('value')} -> {new}")
        return f'{match.group("open")}{new}</span>'

    html = re.sub(
        r'(?P<open><span class="lib-stars" data-repo="(?P<repo>[^"]+)">)(?P<value>[^<]*)</span>',
        replace_stars,
        html,
    )

    def replace_repo_count(match: re.Match[str]) -> str:
        if match.group("value") != str(authored):
            changes.append(f"repositories: {match.group('value')} -> {authored}")
        return f'{match.group("open")}{authored}</dd>'

    html = re.sub(
        r'(?P<open><dd data-stat="repos">)(?P<value>[^<]*)</dd>',
        replace_repo_count,
        html,
    )

    version_html = update_versions(html)
    if version_html != html:
        changes.append("Refreshed tracked game versions and check date")
    html = version_html

    if html == original:
        print("Figures already current — nothing to do.")
        return 0

    HTML.write_text(html, encoding="utf-8")
    print("Updated:")
    for line in changes:
        print(f"  {line}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
