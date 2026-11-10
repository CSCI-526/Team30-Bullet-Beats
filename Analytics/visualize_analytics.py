#!/usr/bin/env python3
"""Builds the Bullet Beats analytics dashboard from the Google Form responses.

    python3 visualize_analytics.py responses.csv -o dashboard.html
    python3 visualize_analytics.py "https://docs.google.com/spreadsheets/d/<id>/export?format=csv&gid=<gid>"
    python3 visualize_analytics.py -o dashboard.html        (no source: writes the "No playtest data yet" page)

Only needs Python 3. Rows from before the Beta logging (level = "MainLevel") are ignored, so old
test data doesn't need clearing. The page only ever draws real responses. With no source, or no Beta
rows yet, it says "No playtest data yet" and shows no numbers. (--simulated is for testing the script
on made-up data on your own computer. The published workflow never uses it.)

The form's columns are: Timestamp, session, event, level, level time, data 1, data 2, data 3.
    session = player-session        level = song|difficulty|attempt
    level_start   data: holdToFire, aimAssist, tutorial seen/new
    asteroid      time = its beat; data: type|outcome, ship spot, offsetMs|tries|slowMo
    dodge         data: from, to
    star          data: kind, spot, collected/expired
    level_end     data: win/loss, attempt, empty clicks
"""

import argparse
import csv
import html
import io
import math
import statistics
import sys
import urllib.request
from collections import defaultdict
from datetime import datetime, timezone

BIN = 5                # seconds per column on the song-time charts
MIN_RUNS = 5           # columns with fewer runs than this are drawn grey
# Song ids in level order. Fill in the final order here; any other song is listed after these.
LEVEL_ORDER = ["pink-panther", "cheat-codes", "flames", "far-out", "boogie-shoes"]
# Timing windows (ms) used when the data doesn't say which ones the game had. Matches MainLevel.unity (BeatClock).
# Newer builds log their windows in each level_start row, and the page uses those instead.
DEFAULT_WINDOWS_MS = (70, 150)
# Optional planned difficulty score per song id (doc 3). Empty = column hidden.
PLANNED_SCORE = {}

SPOTS = ["Center", "Left", "Bottom", "Right"]
KEY = {"Center": "W", "Left": "A", "Bottom": "S", "Right": "D"}
DESTROYED, AVOIDED, HIT = "Destroyed on the beat", "Avoided", "Hit the ship"
GROUP = {"perfect": DESTROYED, "good": DESTROYED, "dodged": AVOIDED, "shielded": AVOIDED, "bombed": AVOIDED, "hit": HIT}
ESC = html.escape


# ---------------------------------------------------------------- loading

def read_text(source):
    if source.startswith(("http://", "https://")):
        with urllib.request.urlopen(source, timeout=60) as response:
            return response.read().decode("utf-8-sig")
    with open(source, encoding="utf-8-sig") as f:
        return f.read()


def load_rows(source):
    """Rows as dicts, by form order (Timestamp, session, event, level, time, d1, d2, d3)."""
    rows = list(csv.reader(io.StringIO(read_text(source))))
    out = []
    for row in rows[1:]:
        if len(row) < 5:
            continue
        row = [c.strip() for c in row] + [""] * 8
        try:
            t = float(row[4])
        except ValueError:
            continue
        out.append({"session": row[1], "event": row[2].lower(), "level": row[3], "t": t,
                    "d1": row[5], "d2": row[6], "d3": row[7]})
    return out


def new_run(player, song, difficulty, attempt):
    return {"windows": None, "player": player, "song": song, "difficulty": difficulty, "attempt": attempt, "hold": False,
            "result": None, "end": 0.0, "asteroids": [], "dodges": [], "stars": [], "empty": 0}


def build_runs(rows):
    """Groups Beta-format rows into one run per player, song and attempt."""
    runs = {}
    for r in rows:
        parts = r["level"].split("|")
        if len(parts) != 3 or not parts[2].isdigit():
            continue
        player = r["session"].split("-")[0]
        key = (player, parts[0], int(parts[2]))
        run = runs.setdefault(key, new_run(player, parts[0], parts[1], int(parts[2])))
        run["end"] = max(run["end"], r["t"])
        e = r["event"]
        if e == "level_start":
            run["hold"] = r["d1"] == "1"
            w = r["d3"].split("|")
            if len(w) == 3 and w[1].isdigit() and w[2].isdigit() and int(w[1]) > 0:
                run["windows"] = (int(w[1]), int(w[2]))
        elif e == "asteroid":
            kind = r["d1"].split("|") + [""]
            shot = r["d3"].split("|") + ["", ""]
            try:
                offset = int(shot[0])
            except ValueError:
                offset = None
            run["asteroids"].append({"t": r["t"], "type": kind[0], "outcome": kind[1], "spot": r["d2"],
                                     "offset": offset, "slow": shot[2] == "1"})
        elif e == "dodge":
            run["dodges"].append((r["t"], r["d2"]))
        elif e == "star":
            run["stars"].append({"t": r["t"], "kind": r["d1"], "spot": r["d2"], "collected": r["d3"] == "collected"})
        elif e == "level_end":
            run["result"], run["empty"] = r["d1"], int(r["d3"]) if r["d3"].isdigit() else 0
    for run in runs.values():
        run["dodges"].sort()
    return list(runs.values())


def level_ids(runs):
    """The songs the game shows, in level order. Songs that have been hidden are left out."""
    seen = {r["song"] for r in runs}
    return [s for s in LEVEL_ORDER if s in seen]


def pct(a, b):
    return 100.0 * a / b if b else 0.0


def median(values):
    return statistics.median(values) if values else None


def iqr(values):
    if len(values) < 4:
        return None
    q = statistics.quantiles(values, n=4)
    return q[2] - q[0]


# ---------------------------------------------------------------- calculations

def bins_for(runs):
    return max(1, math.ceil(max(r["end"] for r in runs) / BIN))


def occupancy(run, bins):
    """Seconds the ship spent at each spot, per column. Rebuilt from dodges; the ship starts at S."""
    grid = [dict.fromkeys(SPOTS, 0.0) for _ in range(bins)]
    points = [(0.0, "Bottom")] + [d for d in run["dodges"] if d[1] in SPOTS] + [(run["end"], None)]
    for (start, spot), (stop, _) in zip(points, points[1:]):
        for b in range(int(start // BIN), min(bins, int(stop // BIN) + 1)):
            overlap = min(stop, (b + 1) * BIN) - max(start, b * BIN)
            if overlap > 0:
                grid[b][spot] += overlap
    return grid


def heat_data(runs):
    bins = bins_for(runs)
    totals = [dict.fromkeys(SPOTS, 0.0) for _ in range(bins)]
    reached = [0] * bins
    bigs, stars = [0] * bins, [0] * bins
    shares = []
    for run in runs:
        grid = occupancy(run, bins)
        whole = dict.fromkeys(SPOTS, 0.0)
        for b, cell in enumerate(grid):
            if sum(cell.values()) > 0:
                reached[b] += 1
            for s in SPOTS:
                totals[b][s] += cell[s]
                whole[s] += cell[s]
        if sum(whole.values()) > 0:
            shares.append(max(whole.values()) / sum(whole.values()) * 100)
        for a in run["asteroids"]:
            if a["type"] == "big" and int(a["t"] // BIN) < bins:
                bigs[int(a["t"] // BIN)] += 1
        for s in run["stars"]:
            if int(s["t"] // BIN) < bins:
                stars[int(s["t"] // BIN)] += 1
    return {"bins": bins, "totals": totals, "n": reached, "bigs": bigs, "stars": stars, "camp": median(shares)}


def timeline_data(runs):
    bins = bins_for(runs)
    counts = [defaultdict(int) for _ in range(bins)]
    reached = [0] * bins
    for run in runs:
        for b in range(min(bins, int(run["end"] // BIN) + 1)):
            reached[b] += 1
        for a in run["asteroids"]:
            b = int(a["t"] // BIN)
            if b < bins and a["outcome"] in GROUP:
                counts[b][GROUP[a["outcome"]]] += 1
    total = [sum(c.values()) for c in counts]
    density = [total[b] / (reached[b] * BIN) if reached[b] else 0 for b in range(bins)]
    avg_hit = pct(sum(c[HIT] for c in counts), sum(total))
    calm = median([d for d in density if d > 0]) or 0
    flagged = [b for b in range(bins) if total[b] >= 10 and density[b] <= calm and pct(counts[b][HIT], total[b]) >= 1.5 * avg_hit]
    return {"bins": bins, "counts": counts, "total": total, "n": reached, "density": density, "avg_hit": avg_hit, "flagged": flagged}


def timing_data(runs):
    first, later = [], []
    by_player = defaultdict(lambda: ([], []))
    for run in runs:
        if run["hold"]:
            continue
        for a in run["asteroids"]:
            if a["offset"] is None or a["slow"] or a["type"] == "big":
                continue
            (first if run["attempt"] == 1 else later).append(a["offset"])
            by_player[(run["player"], run["song"])][0 if run["attempt"] == 1 else 1].append(a["offset"])
    a1, a2 = [], []
    pairs = 0
    for one, two in by_player.values():
        if len(one) >= 10 and len(two) >= 10:
            a1 += one
            a2 += two
            pairs += 1
    used = defaultdict(int)
    for run in runs:
        if not run["hold"] and any(a["offset"] is not None for a in run["asteroids"]):
            used[run["windows"] or DEFAULT_WINDOWS_MS] += 1
    windows = max(used, key=used.get) if used else DEFAULT_WINDOWS_MS
    return {"windows": windows, "mixed": len(used) > 1, "all_windows": dict(used), "first": first, "later": later, "pairs": pairs, "pair_first": iqr(a1), "pair_later": iqr(a2),
            "empty": sum(r["empty"] for r in runs), "runs": len(runs)}


def power_data(runs):
    table = defaultdict(lambda: {"collected": [0, 0], "missed": [0, 0]})
    for run in runs:
        hits = [a["t"] for a in run["asteroids"] if a["outcome"] == "hit"]
        for star in run["stars"]:
            side = table[star["kind"]]["collected" if star["collected"] else "missed"]
            side[0] += 1
            side[1] += any(star["t"] < h <= star["t"] + 10 for h in hits)
    return table


# ---------------------------------------------------------------- drawing

def svg(w, h, label, body):
    # Never draw a chart bigger than its natural size: a run with only a few 5-second columns made a tiny
    # viewBox that the page then stretched to the full width, so everything looked huge.
    style = f"max-width:{w}px;min-width:min({w}px,600px)"
    return f'<div class="scroll"><svg viewBox="0 0 {w} {h}" style="{style}" role="img" aria-label="{ESC(label)}">{body}</svg></div>'


def text(x, y, s, anchor="start", cls=""):
    return f'<text x="{x:.1f}" y="{y:.1f}" text-anchor="{anchor}" class="{cls}">{ESC(str(s))}</text>'


def rect(x, y, w, h, fill, tip="", extra=""):
    title = f"<title>{ESC(tip)}</title>" if tip else ""
    return f'<rect x="{x:.1f}" y="{y:.1f}" width="{max(w, 0):.1f}" height="{max(h, 0):.1f}" rx="2" fill="{fill}" {extra}>{title}</rect>'


def x_labels(bins, left, cw, y):
    step = max(1, round(bins / 8))
    return "".join(text(left + b * cw, y, f"{b * BIN}s", "middle") for b in range(0, bins + 1, step))


def n_row(n, left, cw, y):
    step = max(1, round(len(n) / 8))
    return text(0, y, "runs") + "".join(text(left + b * cw + cw / 2, y, n[b], "middle") for b in range(0, len(n), step))


def heat_svg(d):
    left, top, cw, ch = 48, 28, max(14, min(34, 640 / d["bins"])), 26
    w, h = left + d["bins"] * cw + 58, top + 4 * ch + 42
    out = [text(0, 18, "asked")]
    for b in range(d["bins"]):
        if d["n"][b] >= MIN_RUNS and d["bigs"][b] / d["n"][b] >= 0.4:
            out.append(text(left + b * cw + cw / 2 - 5, 18, "▲", "middle", "on"))
        if d["n"][b] >= MIN_RUNS and d["stars"][b] / d["n"][b] >= 0.2:
            out.append(text(left + b * cw + cw / 2 + 5, 18, "★", "middle", "on"))
    grand = {s: sum(c[s] for c in d["totals"]) for s in SPOTS}
    all_time = sum(grand.values()) or 1
    for y, spot in enumerate(SPOTS):
        out.append(text(left - 12, top + y * ch + 17, KEY[spot], "end", "on"))
        out.append(text(left + d["bins"] * cw + 10, top + y * ch + 17, f"{grand[spot] / all_time * 100:.0f}%", "start", "on"))
        for b in range(d["bins"]):
            cell = d["totals"][b]
            whole = sum(cell.values())
            x, yy = left + b * cw, top + y * ch
            if d["n"][b] < MIN_RUNS or not whole:
                out.append(rect(x, yy, cw - 2, ch - 2, "var(--empty)", f"{b * BIN}s: only {d['n'][b]} runs"))
                continue
            v = cell[spot] / whole
            out.append(rect(x, yy, cw - 2, ch - 2, "var(--seq)", f"{KEY[spot]} at {b * BIN}-{(b + 1) * BIN}s: {v * 100:.0f}% of time, {d['n'][b]} runs", f'fill-opacity="{max(.05, v):.2f}"'))
    out.append(text(left + d["bins"] * cw + 10, top - 8, "total"))
    out.append(x_labels(d["bins"], left, cw, top + 4 * ch + 14))
    out.append(n_row(d["n"], left, cw, top + 4 * ch + 32))
    return svg(w, h, "Ship position by song time", "".join(out))


def timeline_svg(d):
    left, cw, top_h, top, h0 = 52, max(14, min(34, 640 / d["bins"])), 44, 58, 200
    w, h = left + d["bins"] * cw + 14, top + h0 + 42
    peak = max(d["density"]) or 1
    out = [text(0, 34, "per sec")]
    for b, v in enumerate(d["density"]):
        bar = v / peak * (top_h - 6)
        out.append(rect(left + b * cw, top_h - bar, cw - 2, bar, "var(--density)", f"{b * BIN}s: {v:.2f} asteroids per second"))
    for p in (0, 50, 100):
        out.append(text(left - 10, top + h0 - p / 100 * h0 + 4, f"{p}%", "end"))
    colors = [(HIT, "var(--s2)"), (AVOIDED, "var(--s3)"), (DESTROYED, "var(--s1)")]
    for b in range(d["bins"]):
        x = left + b * cw
        if d["n"][b] < MIN_RUNS or not d["total"][b]:
            out.append(rect(x, top, cw - 2, h0, "var(--empty)", f"{b * BIN}s: only {d['n'][b]} runs"))
            continue
        y = top + h0
        for name, color in colors:
            share = pct(d["counts"][b][name], d["total"][b])
            seg = share / 100 * h0
            y -= seg
            out.append(rect(x, y + 1, cw - 2, max(1, seg - 2), color, f"{b * BIN}-{(b + 1) * BIN}s, {name}: {share:.0f}% of {d['total'][b]} asteroids"))
    for b in d["flagged"]:
        out.append(text(left + b * cw + cw / 2, top + h0 + 13, "▲", "middle", "on"))
    out.append(x_labels(d["bins"], left, cw, top + h0 + 26))
    out.append(n_row(d["n"], left, cw, top + h0 + 40))
    return svg(w, h, "Asteroid outcomes by song time", "".join(out))


def timing_svg(d):
    left, right, top, h0, w = 46, 20, 16, 220, 700
    edges = list(range(-300, 301, 20))
    pw, gw = d["windows"]

    def shares(values):
        inside = [v for v in values if -300 <= v < 300]
        counts = [0] * (len(edges) - 1)
        for v in inside:
            counts[(v + 300) // 20] += 1
        return [pct(c, len(values)) for c in counts]

    first, later = shares(d["first"]), shares(d["later"])
    ymax = max(first + later + [5]) * 1.15
    x = lambda ms: left + (ms + 300) / 600 * (w - left - right)
    y = lambda p: top + h0 - p / ymax * h0
    out = [rect(x(-gw), top, x(gw) - x(-gw), h0, "var(--band)", extra='fill-opacity=".45"'),
           rect(x(-pw), top, x(pw) - x(-pw), h0, "var(--band)"),
           text(x(0), top + 12, f"Perfect ±{pw}", "middle", "on"),
           text(x(-(pw + gw) / 2), top + 12, "Good", "middle"), text(x((pw + gw) / 2), top + 12, "Good", "middle")]
    for t in range(0, int(ymax) + 1, 5):
        out.append(f'<line x1="{left}" x2="{w - right}" y1="{y(t):.1f}" y2="{y(t):.1f}" class="grid"/>' + text(left - 8, y(t) + 4, f"{t}%", "end"))
    for m in range(-300, 301, 100):
        out.append(text(x(m), top + h0 + 16, f"+{m}" if m > 0 else m, "middle"))
    out.append(text(left, top + h0 + 34, "early") + text(w - right, top + h0 + 34, "late, milliseconds from the beat", "end"))
    for values, series, color in ((d["first"], first, "var(--s1)"), (d["later"], later, "var(--s2)")):
        if not values:
            continue
        pts = " ".join(f"{x(edges[i] + 10):.1f},{y(p):.1f}" for i, p in enumerate(series))
        out.append(f'<polyline points="{pts}" fill="none" stroke="{color}" stroke-width="2.5" stroke-linejoin="round"/>')
        m = median(values)
        if -300 <= m <= 300:
            out.append(f'<line x1="{x(m):.1f}" x2="{x(m):.1f}" y1="{top + h0}" y2="{top + h0 - 14}" stroke="{color}" stroke-width="3"/>')
    return svg(w, top + h0 + 44, "Timing error distribution", "".join(out))


def power_svg(table):
    kinds = sorted(table)
    left, right, top, rh, w = 190, 60, 8, 60, 700
    h = top + max(1, len(kinds)) * rh + 30
    xs = lambda p: left + p / 100 * (w - left - right)
    out = []
    for p in range(0, 101, 25):
        out.append(f'<line x1="{xs(p):.1f}" x2="{xs(p):.1f}" y1="{top}" y2="{h - 24}" class="grid"/>' + text(xs(p), h - 8, f"{p}%", "middle"))
    for i, kind in enumerate(kinds):
        c, m = table[kind]["collected"], table[kind]["missed"]
        y = top + i * rh + 8
        total = c[0] + m[0]
        out.append(text(0, y + 14, kind, "start", "on") + text(0, y + 31, f"{pct(c[0], total):.0f}% of {total} stars taken"))
        for offset, side, color, name in ((0, c, "var(--s1)", "Collected"), (20, m, "var(--s2)", "Missed")):
            if not side[0]:
                out.append(text(left + 4, y + offset + 13, f"{name}: no stars"))
                continue
            v = pct(side[1], side[0])
            out.append(rect(left, y + offset, xs(v) - left, 17, color, f"{kind}, {name}: {v:.0f}% of {side[0]} stars followed by a hit"))
            out.append(text(xs(v) + 6, y + offset + 13, f"{v:.0f}% of {side[0]}", "start", "on"))
    return svg(w, h, "Ship hits after collected and missed stars", "".join(out))


# ---------------------------------------------------------------- page

def level_table(runs, songs):
    show_score = bool(PLANNED_SCORE)
    head = "<tr><th>Level</th>" + ('<th class="num">Planned score</th>' if show_score else "") + \
        '<th class="num">Players</th><th class="num">Runs</th><th class="num">Completed %</th><th class="num">Asteroids per second</th><th class="num">Hit per 100 asteroids</th></tr>'
    rows = []
    for song in songs:
        group = [r for r in runs if r["song"] == song]
        asteroids = [a for r in group for a in r["asteroids"]]
        seconds = sum(r["end"] for r in group) or 1
        score = f'<td class="num">{PLANNED_SCORE.get(song, "-")}</td>' if show_score else ""
        rows.append(f'<tr><td>{LEVEL_ORDER.index(song) + 1}. {ESC(song)}</td>{score}<td class="num">{len({r["player"] for r in group})}</td><td class="num">{len(group)}</td>'
                    f'<td class="num">{pct(sum(r["result"] == "win" for r in group), len(group)):.0f}</td><td class="num">{len(asteroids) / seconds:.2f}</td>'
                    f'<td class="num">{pct(sum(a["outcome"] == "hit" for a in asteroids), len(asteroids)):.0f}</td></tr>')
    return (f'<div class="scroll"><table>{head}{"".join(rows)}</table></div>'
            '<p class="stat">Completed = the player reached the end of the song. Runs that were lost, quit early or are still being played count as not completed.</p>')


def build_empty_page(message):
    """A page with no numbers at all, for when there are no real responses to draw."""
    css = PAGE.split("<style>")[1].split("</style>")[0].replace("{{", "{").replace("}}", "}")
    updated = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    return (f'<!doctype html>\n<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'
            f'<title>Bullet Beats Analytics</title><style>{css}</style></head><body><main>'
            f'<header><h1>Bullet Beats Analytics</h1><p class="sub">Beta dashboard for CSCI 526 Team 30. Checked {updated}.</p></header>'
            f'<section class="card"><h2>No playtest data yet</h2><p>{ESC(message)}</p>'
            f'<p class="sub">This page only shows real responses from the Google Form. It draws the four graphs as soon as the first Beta runs arrive.</p></section>'
            f'</main></body></html>\n')


def legend(*items):
    return '<div class="legend">' + "".join(f'<span><i style="background:{c}"></i>{ESC(t)}</span>' for c, t in items) + "</div>"


def info(what, why, read, hypothesis):
    return (f'<dl><div><dt>What it is</dt><dd>{what}</dd></div><div><dt>Why we track it</dt><dd>{why}</dd></div>'
            f'<div><dt>How to read it</dt><dd>{read}</dd></div><div><dt>Hypothesis</dt><dd>{hypothesis}</dd></div></dl>')


def fmt_ms(v):
    return "n/a" if v is None else f"{v:+.0f} ms"


def build_page(runs, source_note, simulated):
    songs = level_ids(runs)
    players = len({r["player"] for r in runs})
    options, heat, timeline = [], [], []
    for i, song in enumerate(songs):
        group = [r for r in runs if r["song"] == song]
        options.append(f'<option value="{i}">{LEVEL_ORDER.index(song) + 1}. {ESC(song)}</option>')
        hd, td = heat_data(group), timeline_data(group)
        camp = f"Median run spent {hd['camp']:.0f}% of its time at its most-used spot." if hd["camp"] is not None else ""
        flags = ", ".join(f"{b * BIN}-{(b + 1) * BIN}s" for b in td["flagged"])
        flag_text = f"▲ Hit share at least 1.5× the level average in a quieter stretch: {flags}." if flags else "No quiet stretch has an unusually high hit share."
        heat.append(f'<div data-level="{i}">{heat_svg(hd)}<p class="stat">{camp}</p></div>')
        timeline.append(f'<div data-level="{i}">{timeline_svg(td)}<p class="stat">Level average: {td["avg_hit"]:.0f}% of asteroids hit the ship. {flag_text}</p></div>')
    t = timing_data(runs)
    narrowing = (f'Among {t["pairs"]} player-level pairs who retried, the middle half of shots went from {t["pair_first"]:.0f} ms wide to {t["pair_later"]:.0f} ms wide.'
                 if t["pairs"] and t["pair_first"] and t["pair_later"] else "Not enough retries yet to compare the same players.")
    banner = ('<div class="banner"><strong>Simulated data.</strong> These charts show generated sample responses until Beta playtest data arrives. '
              'They exercise the full pipeline, but no number here is a real result.</div>') if simulated else ""
    updated = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    return PAGE.format(
        banner=banner, players=players, runs=len(runs), asteroids=sum(len(r["asteroids"]) for r in runs), updated=updated, source=ESC(source_note),
        levels=level_table(runs, songs), options="".join(options), heat="".join(heat), timeline="".join(timeline),
        heat_info=info("Where the ship was during the song. Each column is 5 seconds and adds to 100% of the time runs spent in it.",
                       "The ship can only be at W, A, S or D, so this is the true map of the game and entirely the player's choice. Alpha players said dodging felt pointless.",
                       "A row that stays dark means players camp there. Compare with ▲ big asteroids and ★ stars, which show what the level asked for, so the player's choice isn't confused with our design.",
                       "On levels with big asteroids and stars, the median player spends under 60% of the run at any one spot."),
        heat_legend=f'<div class="legend"><span>Share of time<span class="ramp"></span>0 to 100%</span><span>▲ big asteroid</span><span>★ star</span><span><i style="background:var(--empty)"></i>Fewer than {MIN_RUNS} runs</span></div>',
        timeline_info=info("How every asteroid ended, in 5-second columns, under the number of asteroids per second. Each asteroid has exactly one outcome.",
                           "Shows whether players get hit where we meant the song to be hard. It uses hits and not deaths, because healing can make deaths rare.",
                           "Orange at the bottom is the hit share. It should rise and fall with the grey density bars. Orange that grows where the bars are low is a spot to investigate.",
                           "Hit share rises and falls with density, and no quiet stretch is well above the level's average hit share."),
        timeline_legend=legend(("var(--s2)", HIT), ("var(--s3)", AVOIDED + " (dodged, shielded or bombed)"), ("var(--s1)", DESTROYED), ("var(--density)", "Asteroids per second")),
        timing=timing_svg(t),
        timing_info=info("How early or late the first shot at each asteroid was. Leaves out big asteroids, slow-mo shots and Hold to Fire runs.",
                         "The game is usually played muted, so this tests whether players can read the lock-on cue, and whether they improve when they retry.",
                         "One tight peak near zero means the cue is readable. A peak off to one side means players share the wrong moment. A wide, flat curve means guessing.",
                         "The median offset stays within ±40 ms, and the middle half of shots is narrower on later attempts for the same players."),
        timing_legend=legend(("var(--s1)", f"Attempt 1 ({len(t['first'])} shots)"), ("var(--s2)", f"Attempt 2 or later ({len(t['later'])} shots)")),
        timing_stat=f'Median offset: attempt 1 {fmt_ms(median(t["first"]))}, later {fmt_ms(median(t["later"]))}. {narrowing} Clicks at nothing: {t["empty"] / max(1, t["runs"]):.1f} per run. Timing windows in the game: Perfect ±{t["windows"][0]} ms, Good ±{t["windows"][1]} ms{" (runs used different windows, Perfect/Good in ms: " + ", ".join(f"{p}/{g} in {n} runs" for (p, g), n in sorted(t["all_windows"].items(), key=lambda kv: -kv[1])) + "; the shaded one is the most common)" if t["mixed"] else ""}.',
        power=power_svg(power_data(runs)),
        power_info=info("For each power-up: how often its star was taken, and how often a ship hit followed in the next 10 song-seconds, collected vs missed.",
                        "Shows whether power-ups are noticed and whether they change the outcome.",
                        "A blue bar shorter than its orange bar means fewer hits after collecting. Stronger players may also collect more, so check it against the other graphs.",
                        "Each type is collected more than half the time, and collected stars are followed by a hit less often than missed ones."),
        power_legend=legend(("var(--s1)", "Star collected"), ("var(--s2)", "Star missed")) ,
    )


PAGE = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Bullet Beats Analytics</title>
<style>
:root{{color-scheme:light dark;--bg:#f6f7f9;--panel:#fff;--fg:#14171f;--muted:#59606e;--line:#dfe3ea;--s1:#2a78d6;--s2:#eb6834;--s3:#1baf7a;--seq:#184f95;--density:#8a93a3;--empty:#e9ecf1;--band:#dcf0f1;--warn:#fbeecb;--warn-fg:#6a4700}}
@media (prefers-color-scheme:dark){{:root{{--bg:#12151c;--panel:#1b1f29;--fg:#eef0f4;--muted:#a9b0be;--line:#2f3543;--s1:#3987e5;--s2:#d95926;--s3:#199e70;--seq:#6da7ec;--density:#7d8799;--empty:#262b37;--band:#17383d;--warn:#3a2d0e;--warn-fg:#f0c766}}}}
body{{margin:0;background:var(--bg);color:var(--fg);font:16px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}}
main{{max-width:940px;margin:0 auto;padding:32px 16px 64px;display:grid;gap:24px}}
main>*{{min-width:0}}
h1{{margin:0;font-size:2rem}}h2{{margin:0;font-size:1.3rem}}p{{margin:0}}
.sub,.stat,.legend{{color:var(--muted);font-size:.9rem}}
.banner{{background:var(--warn);color:var(--warn-fg);padding:12px 14px;border-radius:6px}}
.card{{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:20px;display:grid;gap:14px}}
.card>*{{min-width:0}}
.facts{{display:flex;flex-wrap:wrap;gap:8px 28px}}.facts b{{display:block;font-size:1.4rem;font-variant-numeric:tabular-nums}}
.scroll{{overflow-x:auto}}svg{{display:block;width:100%;height:auto}}
svg text{{fill:var(--muted);font:11px ui-monospace,Menlo,monospace}}svg text.on{{fill:var(--fg);font-weight:600}}svg .grid{{stroke:var(--line)}}
.legend{{display:flex;flex-wrap:wrap;gap:6px 16px}}.legend i{{display:inline-block;width:12px;height:12px;border-radius:2px;margin-right:6px;vertical-align:-1px}}
.ramp{{display:inline-block;width:100px;height:10px;margin:0 6px;background:linear-gradient(90deg,var(--empty),var(--seq));vertical-align:-1px}}
dl{{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px 20px;margin:0}}dt{{font-weight:600;font-size:.82rem;color:var(--muted)}}dd{{margin:0;font-size:.95rem}}
table{{border-collapse:collapse;width:100%}}th,td{{padding:8px 10px 8px 0;border-bottom:1px solid var(--line);text-align:left;white-space:nowrap}}th{{font-size:.8rem;color:var(--muted)}}.num{{text-align:right;font-variant-numeric:tabular-nums}}
label{{font-weight:600}}select{{font:inherit;padding:4px 8px;margin-left:8px}}
</style></head><body><main>
<header><h1>Bullet Beats Analytics</h1><p class="sub">Beta dashboard for CSCI 526 Team 30. Updated {updated}. Source: {source}.</p></header>
{banner}
<div class="facts"><div><b>{players}</b>players</div><div><b>{runs}</b>runs</div><div><b>{asteroids}</b>asteroids logged</div></div>
<section class="card"><h2>Levels at a glance</h2>{levels}</section>
<div><label for="level">Level shown in the first two graphs<select id="level">{options}</select></label></div>
<section class="card"><h2>1. Ship Position Heat Map</h2>{heat_info}{heat}{heat_legend}</section>
<section class="card"><h2>2. Asteroid Outcome Timeline</h2>{timeline_info}{timeline}{timeline_legend}</section>
<section class="card"><h2>3. Timing Profile</h2>{timing_info}{timing}{timing_legend}<p class="stat">{timing_stat}</p></section>
<section class="card"><h2>4. Power-up Value</h2>{power_info}{power}{power_legend}<p class="stat">All levels combined. Lower is better.</p></section>
<p class="sub">One graph can suggest a cause but can't prove one. Read each graph against the others and the Beta survey.</p>
</main><script>
var s=document.getElementById('level'),b=document.querySelectorAll('[data-level]');
function show(){{b.forEach(function(e){{e.hidden=e.getAttribute('data-level')!==s.value}})}}
s.addEventListener('change',show);show();
</script></body></html>
"""


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", nargs="?", help="responses CSV file or link; leave out to write the empty page")
    parser.add_argument("-o", "--output", default="analytics_report.html")
    parser.add_argument("--simulated", action="store_true", help="local testing only: label the data as simulated")
    args = parser.parse_args(argv)

    runs, hidden = [], 0
    if args.source:
        try:
            runs = build_runs(load_rows(args.source))
        except Exception as error:  # keep the last good page: don't replace it with an empty one
            print(f"Could not read the source: {error}", file=sys.stderr)
            return 1
        # Only the songs the game shows (LEVEL_ORDER). Runs on songs that were hidden later are left out and counted.
        hidden = sum(1 for r in runs if r["song"] not in LEVEL_ORDER)
        runs = [r for r in runs if r["song"] in LEVEL_ORDER]
        note = "no Beta responses on the Beta songs in the sheet yet"
    else:
        note = "the responses sheet is not connected yet"

    if runs:
        source = "simulated sample responses" if args.simulated else "Google Form responses"
        if hidden:
            source += f" ({hidden} runs on songs that are no longer in the game are not shown)"
        page = build_page(runs, source, args.simulated)
        print(f"Wrote {args.output}: {len(runs)} runs{' (simulated)' if args.simulated else ''}")
    else:
        page = build_empty_page(f"No data to show: {note}.")
        print(f"Wrote {args.output}: no real data yet ({note})")
    with open(args.output, "w", encoding="utf-8") as f:
        f.write(page)
    return 0


if __name__ == "__main__":
    sys.exit(main())
