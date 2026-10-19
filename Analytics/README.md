# Analytics visualization

The game's `Analytics` script (`BulletBeats/Assets/Scripts/Analytics/Analytics.cs`) posts one Google
Form response per event (`shot`, `dodge`, `death`, `level_end`) from the WebGL build. The form saves
the responses to its linked Google Sheet. `visualize_analytics.py` turns that sheet into
`analytics_report.html`, with one section per GDD metric:

1. **Win / loss ratio**: wins vs losses, and attempts until each session's first win
2. **Death spots**: histogram of the level time when players die
3. **Shot accuracy**: Perfect / Good / Miss share, overall and per session
4. **Dodging**: dodges per session, and histogram of when in the level they happen

It only needs Python 3 (no extra packages).

## Run it

1. Get the responses as CSV, either way:
   - **File:** open the responses sheet → File → Download → Comma-separated values (.csv)
   - **Link:** share the sheet as "Anyone with the link can view" and use
     `https://docs.google.com/spreadsheets/d/<sheet id>/export?format=csv&gid=<tab id>`
2. Run:

   ```bash
   python3 Analytics/visualize_analytics.py responses.csv
   ```

3. Open `analytics_report.html` in a browser.

Options: `-o report.html` picks the output file, and `--bin 10` sets the seconds per bar in the time
histograms (default 5).

Columns are matched by their header names (session, event, level time, data 1–3). If the headers
don't match, the form's question order is used: Timestamp, session, event, level, level time,
data 1, data 2, data 3.
