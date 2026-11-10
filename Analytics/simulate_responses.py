#!/usr/bin/env python3
"""Writes sample_responses.csv in exactly the format the game sends, so the dashboard can be tested
before real playtest data exists. Every value is made up."""

import csv
import random

SONGS = [("pink-panther", 0.45, 80), ("cheat-codes", 0.6, 85), ("flames", 0.75, 90), ("far-out", 0.9, 90), ("boogie-shoes", 1.05, 95)]
SPOTS = ["Center", "Left", "Bottom", "Right"]
rand = random.Random(526)
rows = []


def add(player, session, song, difficulty, attempt, event, t, d1="-", d2="-", d3="-"):
    rows.append(["2026-11-11 12:00:00", f"{player}-{session}", event, f"{song}|{difficulty}|{attempt}", f"{t:.2f}", d1, d2, d3])


def play(player, session, song, density, length, level_index, attempt, skill):
    difficulty = "Medium"
    add(player, session, song, difficulty, attempt, "level_start", 0.0, "0", "1.00", "seen")
    spot, lives, t, stars = "Bottom", 4, 2.0, []
    next_star, next_dodge, next_big = 15.0, rand.uniform(4, 9), 22.0
    bias = rand.gauss(35, 15) * (0.6 if attempt > 1 else 1)
    spread = (95 - skill * 40) * (0.75 if attempt > 1 else 1)
    count = 0
    while t < length and lives > 0:
        spike = 1.9 if abs(t - length * 0.55) < 4 else 1.0
        busy = density * (0.7 + 0.6 * (t / length))
        t += rand.expovariate(busy) if busy else 1
        if t >= length:
            break
        while next_dodge < t:
            new = rand.choice([s for s in SPOTS if s != spot])
            add(player, session, song, difficulty, attempt, "dodge", next_dodge, spot, new)
            spot, next_dodge = new, next_dodge + rand.uniform(4, 11)
        while next_big < t:
            new = rand.choice([s for s in SPOTS if s != spot])
            moved = rand.random() < 0.45 + skill * 0.5
            if moved:
                add(player, session, song, difficulty, attempt, "dodge", next_big - 1, spot, new)
                spot = new
            else:
                lives -= 1
            add(player, session, song, difficulty, attempt, "asteroid", next_big, "big|" + ("dodged" if moved else "hit"), spot, "-|0|0")
            next_big += 23
        while next_star < t:
            kind = rand.choice(["Shield", "SlowMo", "Bomb"])
            got = rand.random() < 0.35 + skill * 0.4
            stars.append((next_star, got))
            add(player, session, song, difficulty, attempt, "star", next_star, kind, rand.choice(SPOTS), "collected" if got else "expired")
            next_star += 20
        count += 1
        kind = "turbo" if count % 8 == 0 else "normal"
        recent_star = any(got and 0 < t - s <= 10 for s, got in stars)
        hit_chance = min(0.7, (0.06 + 0.035 * level_index + busy * 0.06) * spike * (1.3 - skill * 0.7) * (0.6 if recent_star else 1))
        offset = "-"
        if rand.random() < hit_chance:
            outcome = "hit"
            lives -= 1
        else:
            roll = rand.gauss(bias, spread)
            offset = str(round(roll))
            outcome = "perfect" if abs(roll) <= 120 else "good" if abs(roll) <= 250 else "dodged"
            if outcome == "perfect":
                lives = min(5, lives + (rand.random() < 0.3))
        add(player, session, song, difficulty, attempt, "asteroid", t, f"{kind}|{outcome}", spot, f"{offset}|{0 if offset == '-' else 1}|0")
    won = lives > 0
    add(player, session, song, difficulty, attempt, "level_end", length if won else t, "win" if won else "loss", str(attempt), str(rand.randint(0, 14)))


for p in range(26):
    player, session, skill = f"p{p:07d}", f"s{p:07d}", rand.random()
    for index, (song, density, length) in enumerate(SONGS):
        if index and rand.random() > 0.82:
            break
        for attempt in range(1, 2 + (rand.random() < 0.55) + (rand.random() < 0.2)):
            play(player, session, song, density, length, index, attempt, skill)

with open("sample_responses.csv", "w", newline="") as f:
    writer = csv.writer(f)
    writer.writerow(["Timestamp", "Session", "Event", "Level", "Level Time", "Data 1", "Data 2", "Data 3"])
    writer.writerows(rows)
print(len(rows), "rows")
