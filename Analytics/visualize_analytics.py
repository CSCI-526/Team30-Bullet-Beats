#!/usr/bin/env python3
"""Turn the Bullet Beats analytics responses into a chart report.

The game's Analytics script posts one Google Form response per event. The form's linked
Google Sheet has one row per event:

    Timestamp | session | event | level | level time | data 1 | data 2 | data 3

    event        data 1              data 2          data 3
    shot         Perfect/Good/Miss   -               -
    dodge        from spot           to spot         -
    death        cause               -               -
    level_end    win / loss          attempt number  -

This script reads that sheet as CSV (a downloaded file or the sheet's CSV export link) and
writes an HTML report with one chart per GDD metric:

    1. Win / loss ratio      wins vs losses, and attempts until each session's first win
    2. Death spots           when in the level players die (histogram of level time)
    3. Shot accuracy         Perfect / Good / Miss share, overall and per session
    4. Dodging               dodges per session, and when in the level they happen

Only the Python standard library is used, so it runs with a plain `python3`.

Usage:
    python3 visualize_analytics.py responses.csv
    python3 visualize_analytics.py "https://docs.google.com/spreadsheets/d/<id>/export?format=csv&gid=<gid>"
    python3 visualize_analytics.py responses.csv -o report.html --bin 5
"""

import argparse
import csv
import html
import io
import sys
import urllib.request
from collections import Counter, defaultdict
from datetime import datetime

FIELDS = ["timestamp", "session", "event", "level", "level_time", "d1", "d2", "d3"]

# Header words that identify each column; columns that don't match fall back to form order.
HEADER_HINTS = {
    "timestamp": ["timestamp"],
    "session": ["session"],
    "event": ["event"],
    "level": ["level name", "level"],
    "level_time": ["level time", "leveltime", "time"],
    "d1": ["data 1", "data1", "d1"],
    "d2": ["data 2", "data2", "d2"],
    "d3": ["data 3", "data3", "d3"],
}

JUDGMENTS = ["Perfect", "Good", "Miss"]
COLORS = {
    "Perfect": "#e8b923",
    "Good": "#3fb5d9",
    "Miss": "#e0605e",
    "win": "#4caf6e",
    "loss": "#e0605e",
    "bar": "#5b7fd6",
}


# ---------------------------------------------------------------- loading

def read_text(source):
    if source.startswith("http://") or source.startswith("https://"):
        with urllib.request.urlopen(source) as response:
            return response.read().decode("utf-8-sig")
    with open(source, encoding="utf-8-sig") as f:
        return f.read()


def map_columns(header):
    """Returns {field: column index}, using header names first and form order as a fallback."""
    lowered = [h.strip().lower() for h in header]
    mapping = {}
    # Most specific hints first, so "level time" is not taken by "level".
    for field in ["timestamp", "session", "event", "level_time", "d1", "d2", "d3", "level"]:
        for hint in HEADER_HINTS[field]:
            for i, name in enumerate(lowered):
                if i in mapping.values():
                    continue
                if name == hint or (hint in name and len(hint) > 4):
                    mapping[field] = i
                    break
            if field in mapping:
                break
    for i, field in enumerate(FIELDS):
        if field not in mapping and i < len(header) and i not in mapping.values():
            mapping[field] = i
    return mapping


def load_events(source):
    rows = list(csv.reader(io.StringIO(read_text(source))))
    if not rows:
        return []
    mapping = map_columns(rows[0])
    events = []
    for row in rows[1:]:
        if not any(cell.strip() for cell in row):
            continue
        get = lambda f: row[mapping[f]].strip() if f in mapping and mapping[f] < len(row) else ""
        try:
            level_time = float(get("level_time"))
        except ValueError:
            level_time = None
        events.append({
            "timestamp": get("timestamp"),
            "session": get("session") or "unknown",
            "event": get("event").lower(),
            "level": get("level"),
            "level_time": level_time,
            "d1": get("d1"),
            "d2": get("d2"),
            "d3": get("d3"),
        })
    return events


# ---------------------------------------------------------------- metrics

def win_loss(events):
    ends = [e for e in events if e["event"] == "level_end"]
    counts = Counter(e["d1"].lower() for e in ends if e["d1"].lower() in ("win", "loss"))
    # Attempts until the first win, per session (sessions that never won are counted separately).
    attempts_to_win = []
    never_won = 0
    by_session = defaultdict(list)
    for e in ends:
        by_session[e["session"]].append(e)
    for session_ends in by_session.values():
        tries = 0
        won = False
        for e in session_ends:
            tries += 1
            if e["d1"].lower() == "win":
                attempts_to_win.append(tries)
                won = True
                break
        if not won:
            never_won += 1
    return counts, Counter(attempts_to_win), never_won


def death_times(events):
    return [e["level_time"] for e in events if e["event"] == "death" and e["level_time"] is not None]


def shot_accuracy(events):
    overall = Counter()
    per_session = defaultdict(Counter)
    for e in events:
        if e["event"] != "shot":
            continue
        judgment = e["d1"].capitalize()
        if judgment not in JUDGMENTS:
            continue
        overall[judgment] += 1
        per_session[e["session"]][judgment] += 1
    return overall, per_session


def dodges(events):
    per_session = Counter(e["session"] for e in events if e["event"] == "dodge")
    times = [e["level_time"] for e in events if e["event"] == "dodge" and e["level_time"] is not None]
    return per_session, times


def histogram(values, bin_size):
    if not values:
        return []
    top = max(values)
    bins = int(top // bin_size) + 1
    counts = [0] * bins
    for v in values:
        counts[min(int(v // bin_size), bins - 1)] += 1
    return [(f"{i * bin_size:g}-{(i + 1) * bin_size:g}s", c) for i, c in enumerate(counts)]


# ---------------------------------------------------------------- charts (inline SVG)

def bar_chart(items, title, x_label="", y_label="Count", width=480, height=280, color=COLORS["bar"]):
    """items: list of (label, value), (label, value, color) or (label, value, color, tooltip name)."""
    if not items:
        return empty_chart(title)
    pad_l, pad_r, pad_t, pad_b = 48, 12, 32, 56
    plot_w, plot_h = width - pad_l - pad_r, height - pad_t - pad_b
    top = nice_top(max(v for _, v, *_ in items))
    slot = plot_w / len(items)
    bar_w = max(4, slot * 0.7)
    parts = [svg_open(width, height), svg_title(title, width)]
    parts += axis(pad_l, pad_t, plot_w, plot_h, top, y_label)
    rotate = len(items) > 8
    for i, (label, value, *rest) in enumerate(items):
        h = plot_h * value / top
        x = pad_l + i * slot + (slot - bar_w) / 2
        y = pad_t + plot_h - h
        fill = rest[0] if rest else color
        name = rest[1] if len(rest) > 1 else label
        parts.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{bar_w:.1f}" height="{h:.1f}" fill="{fill}" rx="2">'
                     f'<title>{html.escape(str(name))}: {value}</title></rect>')
        if value:
            parts.append(f'<text x="{x + bar_w / 2:.1f}" y="{y - 4:.1f}" class="val">{value}</text>')
        lx, ly = x + bar_w / 2, pad_t + plot_h + 14
        if rotate:
            parts.append(f'<text x="{lx:.1f}" y="{ly:.1f}" class="lbl" transform="rotate(35 {lx:.1f} {ly:.1f})" '
                         f'text-anchor="start">{html.escape(str(label))}</text>')
        else:
            parts.append(f'<text x="{lx:.1f}" y="{ly:.1f}" class="lbl">{html.escape(str(label))}</text>')
    if x_label:
        parts.append(f'<text x="{pad_l + plot_w / 2:.1f}" y="{height - 6}" class="axis-title">{html.escape(x_label)}</text>')
    parts.append("</svg>")
    return "".join(parts)


def stacked_chart(rows, keys, title, width=480, height=300):
    """rows: list of (label, Counter, tooltip name); bars show each key's share of the row total."""
    if not rows:
        return empty_chart(title)
    pad_l, pad_r, pad_t, pad_b = 48, 12, 32, 48
    plot_w, plot_h = width - pad_l - pad_r, height - pad_t - pad_b
    slot = plot_w / len(rows)
    bar_w = max(4, slot * 0.7)
    parts = [svg_open(width, height), svg_title(title, width)]
    parts += axis(pad_l, pad_t, plot_w, plot_h, 100, "% of shots", percent=True)
    for i, (label, counts, name) in enumerate(rows):
        total = sum(counts[k] for k in keys) or 1
        x = pad_l + i * slot + (slot - bar_w) / 2
        y = pad_t + plot_h
        for k in keys:
            h = plot_h * counts[k] / total
            y -= h
            parts.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{bar_w:.1f}" height="{h:.1f}" fill="{COLORS[k]}">'
                         f'<title>{html.escape(name)} {k}: {counts[k]} ({100 * counts[k] / total:.0f}%)</title></rect>')
        parts.append(f'<text x="{x + bar_w / 2:.1f}" y="{pad_t + plot_h + 14}" class="lbl">{html.escape(label)}</text>')
    parts.append(legend(keys, pad_l, height - 8))
    parts.append("</svg>")
    return "".join(parts)


def nice_top(value, ticks=4):
    """Rounds the axis top up so each tick step is a whole 1, 2, 3, 4, 5, 6 or 8 x 10^n."""
    raw = max(value, 1) / ticks
    if raw <= 1:
        return ticks
    magnitude = 10 ** (len(str(int(raw))) - 1)
    for m in (1, 2, 3, 4, 5, 6, 8, 10):
        if raw <= m * magnitude:
            return m * magnitude * ticks
    return 10 * magnitude * ticks


def axis(x0, y0, w, h, top, y_label, percent=False):
    out = [f'<line x1="{x0}" y1="{y0 + h}" x2="{x0 + w}" y2="{y0 + h}" class="ax"/>',
           f'<line x1="{x0}" y1="{y0}" x2="{x0}" y2="{y0 + h}" class="ax"/>']
    for step in range(5):
        frac = step / 4
        y = y0 + h - h * frac
        value = top * frac
        text = f"{value:.0f}%" if percent else f"{value:.0f}"
        out.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x0 + w}" y2="{y:.1f}" class="grid"/>')
        out.append(f'<text x="{x0 - 6}" y="{y + 4:.1f}" class="tick">{text}</text>')
    out.append(f'<text x="12" y="{y0 + h / 2:.1f}" class="axis-title" transform="rotate(-90 12 {y0 + h / 2:.1f})">'
               f'{html.escape(y_label)}</text>')
    return out


def legend(keys, x, y):
    parts = []
    for k in keys:
        parts.append(f'<rect x="{x}" y="{y - 10}" width="10" height="10" fill="{COLORS[k]}"/>'
                     f'<text x="{x + 14}" y="{y}" class="leg">{k}</text>')
        x += 80
    return "".join(parts)


def svg_open(width, height):
    return f'<svg viewBox="0 0 {width} {height}" width="100%" role="img" xmlns="http://www.w3.org/2000/svg">'


def svg_title(title, width):
    return f'<text x="{width / 2}" y="18" class="title">{html.escape(title)}</text>'


def empty_chart(title):
    return f'<div class="empty"><b>{html.escape(title)}</b><br>No data for this metric yet.</div>'


# ---------------------------------------------------------------- report

def pct(part, total):
    return f"{100 * part / total:.0f}%" if total else "-"


def build_report(events, source, bin_size):
    sessions = sorted({e["session"] for e in events})
    # Short labels for the charts; the full session id stays in each bar's tooltip.
    short = {sid: f"S{i + 1}" for i, sid in enumerate(sessions)}
    counts, attempts_to_win, never_won = win_loss(events)
    deaths = death_times(events)
    overall, per_session = shot_accuracy(events)
    dodge_counts, dodge_times = dodges(events)

    wins, losses = counts.get("win", 0), counts.get("loss", 0)
    shots = sum(overall.values())
    on_beat = overall["Perfect"] + overall["Good"]

    sections = []

    sections.append(section(
        "1. Win / loss ratio",
        f"{wins} wins, {losses} losses (win rate {pct(wins, wins + losses)}). "
        f"{sum(attempts_to_win.values())} sessions won at least once; {never_won} never won.",
        bar_chart([("Wins", wins, COLORS["win"]), ("Losses", losses, COLORS["loss"])], "Level results"),
        bar_chart([(str(k), attempts_to_win[k]) for k in sorted(attempts_to_win)],
                  "Attempts until first win", x_label="Attempt number of the first win", y_label="Sessions"),
    ))

    sections.append(section(
        "2. Death spots",
        f"{len(deaths)} deaths recorded" + (f", most common window: {max(histogram(deaths, bin_size), key=lambda b: b[1])[0]}." if deaths else "."),
        bar_chart(histogram(deaths, bin_size), f"Deaths by level time ({bin_size:g}s bins)",
                  x_label="Level time when the player died", y_label="Deaths", color=COLORS["loss"]),
    ))

    sections.append(section(
        "3. Shot accuracy",
        f"{shots} shots: Perfect {pct(overall['Perfect'], shots)}, Good {pct(overall['Good'], shots)}, "
        f"Miss {pct(overall['Miss'], shots)}. On-beat (Perfect + Good) {pct(on_beat, shots)}.",
        bar_chart([(k, overall[k], COLORS[k]) for k in JUDGMENTS], "All shots by judgment", y_label="Shots"),
        stacked_chart([(short[s], per_session[s], s) for s in sessions if per_session[s]], JUDGMENTS,
                      "Judgment share per session"),
    ))

    sections.append(section(
        "4. Dodging",
        f"{sum(dodge_counts.values())} dodges across {len(dodge_counts)} sessions"
        + (f" (average {sum(dodge_counts.values()) / len(dodge_counts):.1f} per session)." if dodge_counts else "."),
        bar_chart([(short[s], dodge_counts[s], COLORS["bar"], s) for s in sessions if dodge_counts[s]],
                  "Dodges per session", x_label="Session", y_label="Dodges"),
        bar_chart(histogram(dodge_times, bin_size), f"Dodges by level time ({bin_size:g}s bins)",
                  x_label="Level time of the dodge", y_label="Dodges"),
    ))

    generated = datetime.now().strftime("%Y-%m-%d %H:%M")
    return PAGE.format(
        generated=generated,
        source=html.escape(source),
        events=len(events),
        sessions=len(sessions),
        sections="".join(sections),
    )


def section(title, summary, *charts):
    body = "".join(f'<div class="chart">{c}</div>' for c in charts)
    return f'<section><h2>{html.escape(title)}</h2><p>{html.escape(summary)}</p><div class="charts">{body}</div></section>'


PAGE = """<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Bullet Beats Analytics</title>
<style>
  :root {{ --bg:#ffffff; --fg:#1d2230; --muted:#5d6475; --line:#d9dde6; --card:#f5f7fb; }}
  @media (prefers-color-scheme: dark) {{
    :root {{ --bg:#11141b; --fg:#e7eaf1; --muted:#9aa2b4; --line:#2c3240; --card:#1a1f2a; }}
  }}
  body {{ margin:0; padding:24px 16px; background:var(--bg); color:var(--fg);
         font:15px/1.5 system-ui, -apple-system, sans-serif; }}
  main {{ max-width:1040px; margin:0 auto; }}
  h1 {{ margin:0 0 4px; font-size:24px; }}
  .meta {{ color:var(--muted); margin:0 0 24px; font-size:13px; }}
  section {{ background:var(--card); border:1px solid var(--line); border-radius:10px; padding:16px; margin:0 0 16px; }}
  h2 {{ margin:0 0 4px; font-size:18px; }}
  section > p {{ margin:0 0 12px; color:var(--muted); }}
  .charts {{ display:grid; grid-template-columns:repeat(auto-fill, minmax(min(100%, 420px), 1fr)); gap:12px; }}
  .chart {{ min-width:0; }}
  svg text {{ fill:var(--fg); font-size:11px; }}
  .title {{ text-anchor:middle; font-size:13px; font-weight:600; }}
  .val {{ text-anchor:middle; font-size:10px; }}
  .lbl {{ text-anchor:middle; fill:var(--muted); }}
  .tick {{ text-anchor:end; fill:var(--muted); font-size:10px; }}
  .axis-title {{ text-anchor:middle; fill:var(--muted); }}
  .leg {{ font-size:11px; }}
  .ax {{ stroke:var(--muted); }}
  .grid {{ stroke:var(--line); stroke-dasharray:2 3; }}
  .empty {{ border:1px dashed var(--line); border-radius:8px; padding:24px; color:var(--muted); text-align:center; }}
</style></head>
<body><main>
<h1>Bullet Beats Analytics</h1>
<p class="meta">Generated {generated} from {source} &middot; {events} events &middot; {sessions} sessions</p>
{sections}
</main></body></html>
"""


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", help="Responses CSV file, or the Google Sheet's CSV export URL")
    parser.add_argument("-o", "--output", default="analytics_report.html", help="HTML report to write")
    parser.add_argument("--bin", type=float, default=5.0, help="Seconds per bar in the time histograms")
    args = parser.parse_args(argv)

    events = load_events(args.source)
    if not events:
        print("No responses found in", args.source, file=sys.stderr)
    report = build_report(events, args.source, args.bin)
    with open(args.output, "w", encoding="utf-8") as f:
        f.write(report)
    print(f"Wrote {args.output}: {len(events)} events")


if __name__ == "__main__":
    main()
