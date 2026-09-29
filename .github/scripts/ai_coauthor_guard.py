#!/usr/bin/env python3
"""AI co-author guard for PRs

Inspects a PRs commits (author, committer, message trailers and body) and the title/body
for attribution to AI coding agents. When attribution is found, the script exits 1
and produces a PR comment explaining the detection and to remove it for PR acceptance.

.github/workflows/no-ai-coauthors.yml fetches the PR data through the GitHub API and runs
this script - it never executes repository or PR code.

Usage:
    python3 ai_coauthor_guard.py --self-test
    python3 ai_coauthor_guard.py --repo owner/name --pr pr.json --commits commits.jsonl \
        --failure-payload failure.json --resolved-payload resolved.json --summary summary.md

Exit codes: 0 clean, 1 findings, 2 usage or input error.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path

MARKER = "<!-- ai-coauthor-guard -->"

# ----------------
# Detection tables
# ----------------

# Company or tool email domains
AI_EMAIL_DOMAINS = {
    "anthropic.com",
    "cursor.com",
    "openai.com",
    "devin.ai",
    "cognition.ai",
    "aider.chat",
    "codeium.com",
    "windsurf.com",
    "all-hands.dev",
    "factory.ai",
    "augmentcode.com",
    "zencoder.ai",
    "qodo.ai",
    "tabnine.com",
    "magic.dev",
    "poolside.ai",
    "mistral.ai",
    "x.ai",
    "deepseek.com",
    "moonshot.ai",
}

# Email regex
AI_EMAIL_PATTERNS = [
    r"^\d+\+copilot@users\.noreply\.github\.com$",
    r"^\d+\+copilot-swe-agent\[bot\]@users\.noreply\.github\.com$",
    r"^.*devin-ai-integration\[bot\]@users\.noreply\.github\.com$",
    r"^.*google-labs-jules\[bot\]@users\.noreply\.github\.com$",
    r"^.*openhands[-a-z]*\[bot\]@users\.noreply\.github\.com$",
    r"^.*codegen-sh\[bot\]@users\.noreply\.github\.com$",
    r"^.*sweep-ai\[bot\]@users\.noreply\.github\.com$",
    r"^.*swe-agent\[bot\]@users\.noreply\.github\.com$",
    r"^.*factory-droid\[bot\]@users\.noreply\.github\.com$",
    r"^.*cursoragent@.*$",
    r"^.*traeagent@.*$",
    r"^.*aider@.*$",
]

# Unambiguous agent names
STRONG_AGENT_NAMES = {
    "claude code",
    "claude-code",
    "anthropic",
    "traeagent",
    "trae agent",
    "cursor agent",
    "cursoragent",
    "github copilot",
    "copilot coding agent",
    "copilot workspace",
    "openai",
    "openai codex",
    "chatgpt",
    "devin ai",
    "openhands",
    "openhands agent",
    "swe agent",
    "sweagent",
    "swe-agent",
    "google jules",
    "gemini code assist",
    "amazon q",
    "amazon q developer",
    "kiro",
    "junie",
    "jetbrains ai",
    "factory droid",
    "zencoder",
    "replit agent",
    "windsurf",
    "codeium",
    "cline",
    "roo code",
    "roocode",
    "qwen code",
    "deepseek",
    "deepseek ai",
    "grok",
    "xai",
    "kimi",
    "moonshot ai",
    "mistral ai",
    "codestral",
    "sourcegraph cody",
    "augment code",
    "continue dev",
    "tabnine",
    "qodo",
    "qodo gen",
    "codegen",
    "sweep",
    "pythagora",
    "refact",
    "tabby",
    "ona",
    "poolside",
    "magic dev",
    "firebender",
}

# Agent names that are also plausible usernames parts
BARE_AGENT_NAMES = {
    "claude",
    "copilot",
    "cursor",
    "codex",
    "devin",
    "aider",
    "trae",
    "jules",
    "gemini",
    "qwen",
    "grok",
    "kimi",
    "bolt",
    "amp",
    "droid",
}

# Trailer keys that imply authorship or generation
TRAILER_KEYS = {
    "coauthoredby",
    "coauthor",
    "coauthorby",
    "coauthors",
    "codevelopedby",
    "generatedby",
    "generatedwith",
    "generatedusing",
    "assistedby",
    "assistedwith",
    "madewith",
    "madeby",
    "madeusing",
    "writtenby",
    "createdby",
    "contributedby",
    "builtby",
}

# Phrases and signatures anywhere
SIGNATURE_PATTERNS = [
    (r"generated with \[?claude(?: code)?\]?", "Claude Code attribution"),
    (r"claude\.com/claude-code", "Claude Code link"),
    (r"made with \[?cursor\]?", "Cursor attribution"),
    (r"generated (?:with|by) \[?trae(?:agent)?\]?", "Trae attribution"),
    (r"generated (?:with|by) \[?(?:windsurf|codeium)\]?", "Windsurf or Codeium attribution"),
    (r"generated (?:with|by) \[?aider\]?", "Aider attribution"),
    (r"^[ \t]*aider:[ \t]", "Aider commit prefix"),
    (r"\U0001F916[^\n]*(?:generated|co-?authored|assisted)", "robot emoji attribution"),
    (r"\bai-generated\b", "AI-generated marker"),
    (r"(?:this )?(?:commit|pr|pull request) was generated", "generated-by statement"),
    (
        r"powered by (?:claude|cursor|copilot|codex|devin|aider|openhands|windsurf|codeium|trae)",
        "powered-by attribution",
    ),
    (
        r"co-?authored[- ]by[: ]+\s*(?:claude|copilot|cursor|codex|devin|aider|traeagent|openhands)\s*(?:<|$)",
        "co-author attribution",
    ),
    (
        r"(?:generated|created|written|produced|assisted) (?:by|with|using) (?:an? )?ai\b",
        "generic AI attribution",
    ),
]

# ---------------------
# Normalisation helpers
# ---------------------

_ZERO_WIDTH = dict.fromkeys(map(ord, "\u200b\u200c\u200d\u200e\u200f\u2060\ufeff"), None)
_TRAILER_RE = re.compile(r"^\s*([A-Za-z][A-Za-z0-9 _-]{0,30})\s*[:\uFF1A]\s*(.+?)\s*$")
_EMAIL_RE = re.compile(r"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")
_IDENTITY_RE = re.compile(r"^(.*?)\s*<([^>]+)>\s*$")
_SIGNATURES = [(re.compile(p, re.IGNORECASE | re.MULTILINE), d) for p, d in SIGNATURE_PATTERNS]
_EMAIL_PATTERNS = [re.compile(p) for p in AI_EMAIL_PATTERNS]


def normalise(text: str) -> str:
    if not text:
        return ""
    text = unicodedata.normalize("NFKC", text)
    text = text.translate(_ZERO_WIDTH)
    return text.replace("\r\n", "\n").replace("\r", "\n")


def fold_name(text: str) -> str:
    text = normalise(text).lower()
    text = re.sub(r"[^a-z0-9]+", " ", text)
    return re.sub(r"\s+", " ", text).strip()


def fold_key(text: str) -> str:
    return re.sub(r"[^a-z0-9]", "", normalise(text).lower())


def split_identity(value: str) -> tuple[str, str]:
    match = _IDENTITY_RE.match(value.strip())
    if match:
        return match.group(1).strip(), match.group(2).strip()
    return value.strip(), ""


def email_is_agent(email: str) -> str | None:
    email = (email or "").strip().lower()
    if not email or "@" not in email:
        return None
    domain = email.rsplit("@", 1)[-1]
    if domain in AI_EMAIL_DOMAINS:
        return f"agent email domain: {domain}"
    for pattern in _EMAIL_PATTERNS:
        if pattern.match(email):
            return f"agent email: {email}"
    return None


def identity_rule(name: str, email: str, allow_bare: bool) -> str | None:
    email_rule = email_is_agent(email)
    if email_rule:
        return email_rule
    folded = fold_name(name)
    if not folded:
        return None
    if folded in STRONG_AGENT_NAMES:
        return f"agent name: {name.strip()}"
    if allow_bare and folded in BARE_AGENT_NAMES:
        return f"agent name: {name.strip()}"
    return None


def excerpt(text: str, start: int, end: int, limit: int = 140) -> str:
    snippet = text[max(0, start - 30) : end + 30].replace("\n", " ").strip()
    snippet = re.sub(r"\s+", " ", snippet)
    if len(snippet) > limit:
        snippet = snippet[: limit - 3] + "..."
    return snippet.replace("|", "\\|")


# ---------
# Detection
# ---------


@dataclass
class Finding:
    source: str  # commit or pull request
    sha: str
    url: str
    role: str  # author, committer, trailer key, message, title, body
    rule: str
    detail: str


def check_text(text: str, source: str, sha: str, url: str, role: str) -> list[Finding]:
    findings: list[Finding] = []
    if not text:
        return findings

    covered_lines: set[int] = set()
    for index, line in enumerate(text.split("\n")):
        match = _TRAILER_RE.match(line)
        if not match:
            continue
        key = fold_key(match.group(1))
        if key not in TRAILER_KEYS:
            continue
        value = match.group(2).strip()
        name, email = split_identity(value)
        rule = identity_rule(name, email, allow_bare=True)
        if rule:
            findings.append(Finding(source, sha, url, f"trailer {match.group(1).strip()}", rule,
                                    excerpt(line, 0, len(line))))
            covered_lines.add(index)
            continue
        for email_match in _EMAIL_RE.finditer(value):
            if email_is_agent(email_match.group(0)):
                findings.append(Finding(source, sha, url, f"trailer {match.group(1).strip()}",
                                        f"agent email: {email_match.group(0).lower()}",
                                        excerpt(line, 0, len(line))))
                covered_lines.add(index)
                break

    for pattern, description in _SIGNATURES:
        match = pattern.search(text)
        if not match:
            continue
        line_number = text.count("\n", 0, match.start())
        if line_number in covered_lines:
            continue
        findings.append(Finding(source, sha, url, role, description,
                                excerpt(text, match.start(), match.end())))
        break

    for email_match in _EMAIL_RE.finditer(text):
        line_number = text.count("\n", 0, email_match.start())
        if line_number in covered_lines:
            continue
        email = email_match.group(0)
        if email_is_agent(email):
            findings.append(Finding(source, sha, url, role, f"agent email: {email.lower()}",
                                    excerpt(text, email_match.start(), email_match.end())))
    return findings


def check_commit(repo: str, commit: dict) -> list[Finding]:
    findings: list[Finding] = []
    sha = commit.get("sha", "")
    url = commit.get("html_url") or (f"https://github.com/{repo}/commit/{sha}" if repo else sha)
    inner = commit.get("commit") or {}
    message = normalise(inner.get("message") or "")

    for role in ("author", "committer"):
        identity = inner.get(role) or {}
        name = identity.get("name") or ""
        email = identity.get("email") or ""
        rule = identity_rule(name, email, allow_bare=False)
        if rule:
            findings.append(Finding("commit", sha, url, role, rule, f"{name} <{email}>".strip()))

    findings.extend(check_text(message, "commit", sha, url, "message"))
    return findings


def check_pull_request(repo: str, pr: dict) -> list[Finding]:
    findings: list[Finding] = []
    url = pr.get("html_url") or ""
    findings.extend(check_text(normalise(pr.get("title") or ""), "pull request", "", url, "title"))
    findings.extend(check_text(normalise(pr.get("body") or ""), "pull request", "", url, "body"))
    return findings


def deduplicate(findings: list[Finding]) -> list[Finding]:
    seen: set[tuple[str, str, str, str]] = set()
    unique: list[Finding] = []
    for finding in findings:
        key = (finding.source, finding.sha, finding.role, finding.rule)
        if key in seen:
            continue
        seen.add(key)
        unique.append(finding)
    return unique


# ---------
# Reporting
# ---------


def short_sha(sha: str) -> str:
    return sha[:10] if sha else ""


def commit_link(repo: str, finding: Finding) -> str:
    sha = short_sha(finding.sha)
    if finding.url:
        return f"[`{sha}`]({finding.url})"
    if repo and finding.sha:
        return f"[`{sha}`](https://github.com/{repo}/commit/{finding.sha})"
    return f"`{sha}`"


def build_summary(findings: list[Finding]) -> str:
    lines = ["## AI co-author guard", ""]
    if not findings:
        lines.append("No AI agent attribution found in the pull request commits or description.")
        lines.append("")
        return "\n".join(lines)

    lines.append(f"Found {len(findings)} AI agent attribution finding(s). The check fails until "
                 "they are removed.")
    lines.append("")
    lines.append("| Location | Match | Rule |")
    lines.append("|---|---|---|")
    for finding in findings:
        if finding.source == "commit":
            location = f"commit {short_sha(finding.sha)} / {finding.role}"
        else:
            location = f"pull request / {finding.role}"
        detail = finding.detail.replace("|", "\\|")
        rule = finding.rule.replace("|", "\\|")
        lines.append(f"| {location} | `{detail}` | {rule} |")
    lines.append("")
    return "\n".join(lines)


def build_failure_payload(repo: str, findings: list[Finding]) -> dict:
    commit_findings = [f for f in findings if f.source == "commit"]
    pr_findings = [f for f in findings if f.source == "pull request"]

    lines = [
        MARKER,
        "## AI co-authoring detected",
        "",
        "This pull request contains attribution to an AI coding agent, which this project does "
        "not accept. The merge check has been marked as failed.",
        "",
    ]

    if commit_findings:
        lines.append("**Commits**")
        lines.append("")
        lines.append("| Commit | Location | Match |")
        lines.append("|---|---|---|")
        for finding in commit_findings:
            location = finding.role.replace("|", "\\|")
            lines.append(f"| {commit_link(repo, finding)} | {location} | `{finding.detail}` |")
        lines.append("")

    if pr_findings:
        lines.append("**Pull request text**")
        lines.append("")
        lines.append("| Location | Match |")
        lines.append("|---|---|")
        for finding in pr_findings:
            lines.append(f"| {finding.role} | `{finding.detail}` |")
        lines.append("")

    lines.extend([
        "**How to fix**",
        "",
        "1. Remove the AI attribution from the affected commits, for example:",
        "   - `git rebase -i <base>` and edit each flagged commit message, or",
        "   - `git commit --amend` when only the latest commit is affected.",
        "2. Force-push the branch (`git push --force-with-lease`).",
        "3. If the attribution was added to the pull request description, edit the description "
        "to remove it.",
        "",
        "The check re-runs automatically on the next push and this comment is updated then.",
        "",
    ])
    return {"body": "\n".join(lines)}


def build_resolved_payload() -> dict:
    body = "\n".join([
        MARKER,
        "## AI co-authoring check: resolved",
        "",
        "The AI attribution flagged earlier has been removed and the check now passes.",
        "",
    ])
    return {"body": body}


# --------------
# Input handling
# --------------


def load_commit_items(paths: list[str]) -> list[dict]:
    items: list[dict] = []
    for path in paths:
        text = Path(path).read_text(encoding="utf-8").strip()
        if not text:
            continue
        if text.startswith("["):
            data = json.loads(text)
            items.extend(data if isinstance(data, list) else [data])
            continue
        if text.startswith("{"):
            try:
                data = json.loads(text)
                items.append(data)
                continue
            except json.JSONDecodeError:
                pass
        for line in text.splitlines():
            line = line.strip()
            if line:
                items.append(json.loads(line))
    return items


def run_check(repo: str, pr_path: str, commit_paths: list[str]) -> list[Finding]:
    pr = json.loads(Path(pr_path).read_text(encoding="utf-8"))
    commits = load_commit_items(commit_paths)
    findings: list[Finding] = []
    findings.extend(check_pull_request(repo, pr))
    for commit in commits:
        findings.extend(check_commit(repo, commit))
    return deduplicate(findings)


# ---------
# Self-test
# ---------


def _commit(message: str = "", name: str = "Dev", email: str = "dev@example.com",
            sha: str = "a" * 40) -> dict:
    return {
        "sha": sha,
        "html_url": f"https://github.com/example/repo/commit/{sha}",
        "commit": {
            "message": message,
            "author": {"name": name, "email": email},
            "committer": {"name": name, "email": email},
        },
    }


def run_self_test() -> int:
    commit_cases: list[tuple[str, dict, bool]] = [
        # Positives
        ("claude trailer", _commit("Fix\n\nCo-Authored-By: Claude <noreply@anthropic.com>"), True),
        ("claude code footer",
         _commit("Fix\n\nGenerated with [Claude Code](https://claude.com/claude-code)"), True),
        ("claude code link", _commit("Fix\n\nSee claude.com/claude-code for details"), True),
        ("cursor trailer",
         _commit("Fix\n\nCo-authored-by: Cursor Agent <cursoragent@cursor.com>"), True),
        ("cursor footer", _commit("Fix\n\nMade with [Cursor](https://cursor.com)"), True),
        ("traeagent trailer",
         _commit("Fix\n\nCo-Authored-By: traeagent <traeagent@users.noreply.github.com>"), True),
        ("trae footer", _commit("Fix\n\nGenerated with [Trae]"), True),
        ("copilot trailer",
         _commit("Fix\n\nCo-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"), True),
        ("copilot agent author", _commit("Fix", name="copilot-swe-agent[bot]",
                                         email="198982749+copilot-swe-agent[bot]@users.noreply.github.com"), True),
        ("devin bot author", _commit("Fix", name="Devin",
                                     email="devin-ai-integration[bot]@users.noreply.github.com"), True),
        ("openhands trailer", _commit("Fix\n\nAssisted-by: OpenHands"), True),
        ("aider prefix", _commit("aider: fix the parser"), True),
        ("robot emoji", _commit("Fix\n\n\U0001F916 Generated with an AI assistant"), True),
        ("full width colon", _commit("Fix\n\nCo-Authored-By\uFF1AClaude <noreply@anthropic.com>"), True),
        ("zero width name", _commit("Fix\n\nCo-Authored-By: Cla\u200bude <noreply@anthropic.com>"), True),
        ("bare claude trailer", _commit("Fix\n\nCo-Authored-By: Claude"), True),
        ("malformed co-author line", _commit("Fix\n\nCo-authored-by Claude"), True),
        ("windsurf trailer", _commit("Fix\n\nGenerated-By: Windsurf"), True),
        ("anthropic committer", _commit("Fix", name="Dev", email="someone@anthropic.com"), True),
        ("jules bot author", _commit("Fix", name="google-labs-jules[bot]",
                                     email="google-labs-jules[bot]@users.noreply.github.com"), True),
        # Negatives
        ("human co-author named Claude",
         _commit("Fix\n\nCo-Authored-By: Claude Martin <claude.martin@example.com>"), False),
        ("human co-author", _commit("Fix\n\nCo-Authored-By: Jane Doe <jane@example.com>"), False),
        ("dco sign-off", _commit("Fix\n\nSigned-off-by: Jane Doe <jane@example.com>"), False),
        ("dependabot", _commit("Bump foo from 1.0 to 1.1", name="dependabot[bot]",
                               email="49699333+dependabot[bot]@users.noreply.github.com"), False),
        ("github actions", _commit("CI", name="github-actions[bot]",
                                   email="41898282+github-actions[bot]@users.noreply.github.com"), False),
        ("cursor in prose", _commit("Fix the cursor position in the editor"), False),
        ("claude in prose", _commit("Update the claude variable name"), False),
        ("plain message", _commit("Fix inventory parsing edge case"), False),
        ("devin human author", _commit("Fix", name="Devin Smith", email="devin@example.com"), False),
    ]

    pr_cases: list[tuple[str, dict, bool]] = [
        ("pr body claude footer",
         {"title": "Fix", "body": "\U0001F916 Generated with [Claude Code](https://claude.com/claude-code)"}, True),
        ("pr title attribution", {"title": "Made with Cursor", "body": ""}, True),
        ("pr body clean", {"title": "Fix parser", "body": "This PR fixes the parser."}, False),
        ("pr body human mention", {"title": "Fix", "body": "Thanks to Claude Martin for the report."}, False),
    ]

    failures = 0
    for name, commit, expected in commit_cases:
        found = bool(check_commit("example/repo", commit))
        if found != expected:
            print(f"SELF-TEST FAIL: commit case '{name}' expected {expected} but got {found}")
            failures += 1
    for name, pr, expected in pr_cases:
        found = bool(check_pull_request("example/repo", pr))
        if found != expected:
            print(f"SELF-TEST FAIL: PR case '{name}' expected {expected} but got {found}")
            failures += 1

    if failures:
        print(f"Self-test failed with {failures} case(s).")
        return 1
    print(f"Self-test passed: {len(commit_cases) + len(pr_cases)} vectors.")
    return 0


# -----------
# Entry point
# -----------


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Check a pull request for AI agent co-authoring.")
    parser.add_argument("--self-test", action="store_true", help="run the built-in test vectors")
    parser.add_argument("--repo", default="", help="owner/name used for commit links")
    parser.add_argument("--pr", help="path to the pull request JSON")
    parser.add_argument("--commits", action="append", default=[],
                        help="path to a commits JSON/JSONL file (repeatable)")
    parser.add_argument("--failure-payload", help="write the failure comment payload here")
    parser.add_argument("--resolved-payload", help="write the resolved comment payload here")
    parser.add_argument("--summary", help="write the markdown summary here")
    args = parser.parse_args(argv)

    if args.self_test:
        return run_self_test()

    if not args.pr or not args.commits:
        parser.error("--pr and at least one --commits file are required")

    try:
        findings = run_check(args.repo, args.pr, args.commits)
    except (OSError, json.JSONDecodeError) as error:
        print(f"AI co-author guard error: {error}", file=sys.stderr)
        return 2

    summary = build_summary(findings)
    print(summary)

    if args.summary:
        Path(args.summary).write_text(summary, encoding="utf-8")
    if args.failure_payload:
        Path(args.failure_payload).write_text(
            json.dumps(build_failure_payload(args.repo, findings)), encoding="utf-8")
    if args.resolved_payload:
        Path(args.resolved_payload).write_text(
            json.dumps(build_resolved_payload()), encoding="utf-8")

    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())