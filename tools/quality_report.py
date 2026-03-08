import argparse
import csv
import datetime as _dt
import os
import subprocess
import sys
from pathlib import Path


def _run(cmd: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, capture_output=True, text=True, check=False)


def _git_ls_files() -> list[str]:
    p = _run(["git", "ls-files"])
    if p.returncode != 0:
        return []
    return [line.strip() for line in p.stdout.splitlines() if line.strip()]


def _count_lines(path: Path) -> int:
    try:
        with path.open("rb") as f:
            return sum(1 for _ in f)
    except Exception:
        return 0


def _safe_relpath(p: str) -> str:
    return p.replace("\\", "/")


def _ensure_parent(out: Path) -> None:
    out.parent.mkdir(parents=True, exist_ok=True)


def _run_lizard_csv(paths: list[str], csv_out: Path) -> tuple[list[dict], str | None]:
    # Use module invocation so we don't depend on PATH.
    cmd = [
        sys.executable,
        "-m",
        "lizard",
        "-l",
        "csharp",
        "--csv",
        "-x",
        "*/bin/*",
        "-x",
        "*/obj/*",
    ] + paths

    p = _run(cmd)
    if p.returncode != 0:
        return [], (p.stderr.strip() or "lizard failed")

    _ensure_parent(csv_out)
    csv_out.write_text(p.stdout, encoding="utf-8")

    rows: list[dict] = []
    r = csv.reader(p.stdout.splitlines())
    for line in r:
        if not line:
            continue
        # Format (no header):
        # nloc, ccn, token_count, parameter_count, length, location, file, function, long_name, start_line, end_line
        if len(line) < 11:
            continue
        try:
            rows.append(
                {
                    "nloc": int(line[0]),
                    "ccn": int(line[1]),
                    "token_count": int(line[2]),
                    "parameter_count": int(line[3]),
                    "length": int(line[4]),
                    "location": line[5],
                    "file": _safe_relpath(line[6]),
                    "function": line[7],
                    "long_name": line[8],
                    "start_line": int(line[9]),
                    "end_line": int(line[10]),
                }
            )
        except Exception:
            continue

    return rows, None


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--md", default="_logs/quality-report.md")
    ap.add_argument("--lizard-csv", default="_logs/lizard.csv")
    ap.add_argument("--top", type=int, default=20)
    args = ap.parse_args()

    md_out = Path(args.md)
    csv_out = Path(args.lizard_csv)
    top_n = max(5, args.top)

    tracked = _git_ls_files()
    tracked_cs = [p for p in tracked if p.endswith(".cs") and "/bin/" not in p and "/obj/" not in p]

    loc_rows: list[tuple[int, str]] = []
    total_loc = 0
    for rel in tracked_cs:
        n = _count_lines(Path(rel))
        total_loc += n
        loc_rows.append((n, _safe_relpath(rel)))

    loc_rows.sort(reverse=True)

    loc_by_area = []
    areas = [
        ("src/WinPanX2/Core", "src/WinPanX2/Core/"),
        ("src/WinPanX2/Windowing", "src/WinPanX2/Windowing/"),
        ("src/WinPanX2/Audio", "src/WinPanX2/Audio/"),
        ("src/WinPanX2/Tray", "src/WinPanX2/Tray/"),
        ("tests", "tests/"),
    ]
    for name, prefix in areas:
        files = [p for p in tracked_cs if p.startswith(prefix)]
        loc = sum(_count_lines(Path(p)) for p in files)
        loc_by_area.append((loc, name, len(files)))

    lizard_rows, lizard_err = _run_lizard_csv(["src", "tests"], csv_out)

    now = _dt.datetime.now(_dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    lines: list[str] = []
    lines.append(f"# Quality Report ({now})")
    lines.append("")
    lines.append("## Size")
    lines.append(f"- Tracked C# files: {len(tracked_cs)}")
    lines.append(f"- Total LOC (tracked .cs): {total_loc}")
    lines.append("")
    lines.append("LOC by area (tracked .cs):")
    lines.append("")
    lines.append("| LOC | Area | Files |")
    lines.append("|---:|---|---:|")
    for loc, name, count in sorted(loc_by_area, reverse=True):
        lines.append(f"| {loc} | {name} | {count} |")

    lines.append("")
    lines.append(f"Top {min(top_n, len(loc_rows))} largest files (LOC):")
    lines.append("")
    lines.append("| LOC | File |")
    lines.append("|---:|---|")
    for loc, path in loc_rows[:top_n]:
        lines.append(f"| {loc} | `{path}` |")

    lines.append("")
    lines.append("## Complexity (lizard)")
    if lizard_err:
        lines.append(f"- lizard: not available ({lizard_err})")
    else:
        lines.append(f"- lizard CSV: `{_safe_relpath(str(csv_out))}`")
        lines.append("")

        by_ccn = sorted(lizard_rows, key=lambda r: (r["ccn"], r["nloc"], r["length"]), reverse=True)
        lines.append(f"Top {min(top_n, len(by_ccn))} functions by CCN:")
        lines.append("")
        lines.append("| CCN | NLOC | Len | Params | Function | File |")
        lines.append("|---:|---:|---:|---:|---|---|")
        for r in by_ccn[:top_n]:
            fn = r["function"]
            file = r["file"]
            lines.append(
                f"| {r['ccn']} | {r['nloc']} | {r['length']} | {r['parameter_count']} | `{fn}` | `{file}` |")

        by_len = sorted(lizard_rows, key=lambda r: (r["length"], r["ccn"], r["nloc"]), reverse=True)
        lines.append("")
        lines.append(f"Top {min(top_n, len(by_len))} functions by length:")
        lines.append("")
        lines.append("| Len | CCN | NLOC | Params | Function | File |")
        lines.append("|---:|---:|---:|---:|---|---|")
        for r in by_len[:top_n]:
            fn = r["function"]
            file = r["file"]
            lines.append(
                f"| {r['length']} | {r['ccn']} | {r['nloc']} | {r['parameter_count']} | `{fn}` | `{file}` |")

    lines.append("")
    lines.append("## Hygiene")
    # Light TODO scan (tracked only)
    todo_hits = 0
    for rel in tracked:
        if not rel.endswith((".cs", ".md", ".ps1", ".yml", ".yaml", ".iss")):
            continue
        try:
            text = Path(rel).read_text(encoding="utf-8", errors="replace")
        except Exception:
            continue
        if "TODO" in text or "HACK" in text or "FIXME" in text:
            todo_hits += 1
    lines.append(f"- Files containing TODO/HACK/FIXME tokens: {todo_hits}")

    _ensure_parent(md_out)
    md_out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(str(md_out))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
