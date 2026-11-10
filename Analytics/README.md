# Analytics dashboard

The game's `Analytics` script posts one Google Form response per event from the WebGL build. The form
saves them to its linked Google Sheet. `visualize_analytics.py` turns the sheet into one HTML dashboard
with four graphs: Ship Position Heat Map, Asteroid Outcome Timeline, Timing Profile and Power-up Value.

The live page is <https://csci-526.github.io/Team30-Bullet-Beats/analytics/>. It's rebuilt every 30
minutes by `.github/workflows/analytics-dashboard.yml`.

## What the game sends

The form's columns don't change. New values are packed with `|`.

| Column | Value |
| --- | --- |
| session | `player-session`. The player id is kept across page reloads. |
| event | `level_start`, `asteroid`, `dodge`, `star`, `level_end`, `tutorial` |
| level | `song\|difficulty\|attempt` |
| level time | Song seconds. For `asteroid`, the asteroid's beat. |

| Event | Data 1 | Data 2 | Data 3 |
| --- | --- | --- | --- |
| `level_start` | Hold to Fire 1/0 | aim assist | tutorial `seen`/`new` |
| `asteroid` | `type\|outcome` | ship spot | `offsetMs\|tries\|slowMo` |
| `dodge` | from | to | |
| `star` | kind | spot | `collected`/`expired` |
| `level_end` | `win`/`loss` | attempt | clicks at nothing |

Older rows (level = `MainLevel`) are ignored, so the sheet doesn't need clearing.

## Connect the sheet

1. Share the responses sheet as "Anyone with the link can view".
2. Build the link: `https://docs.google.com/spreadsheets/d/<sheet id>/export?format=csv&gid=<tab id>`
3. Save it as the repository secret `ANALYTICS_CSV_URL`.

The page only ever shows real responses. Until the link is saved, or while the sheet has no Beta rows, it says "No playtest data yet" and shows no numbers. If the link stops working, the workflow fails and the last good page stays up.

## Run it locally

```bash
python3 Analytics/visualize_analytics.py responses.csv -o dashboard.html
```

`simulate_responses.py` writes `sample_responses.csv` (made-up rows, never committed) so you can try the script on your own computer with `--simulated`, which adds a "Simulated data" banner. The published workflow never uses it. Set the level order in `LEVEL_ORDER` at the top of
`visualize_analytics.py`.
