import sqlite3
import re
import os
import json
from datetime import datetime, timedelta
from flask import Flask, request, jsonify, send_file

app = Flask(__name__)
DB_PATH = os.environ.get("DB_PATH", "guessr_scores.db")


def get_db():
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    return conn


def init_db():
    conn = get_db()
    conn.executescript("""
        CREATE TABLE IF NOT EXISTS scores (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL,
            game TEXT NOT NULL,
            game_number TEXT,
            score_value REAL NOT NULL,
            raw_text TEXT,
            play_date TEXT NOT NULL,
            created_at TEXT DEFAULT (datetime('now')),
            UNIQUE(username, game, play_date)
        );
        CREATE INDEX IF NOT EXISTS idx_scores_date ON scores(play_date);
        CREATE INDEX IF NOT EXISTS idx_scores_user ON scores(username);
        CREATE INDEX IF NOT EXISTS idx_scores_game ON scores(game);
    """)
    conn.commit()
    conn.close()


# ── Parsing logic ──

def parse_travle(text):
    """Travle: score is the +N number (antal fel). -1 for perfect."""
    m = re.search(r'#travle\s+#(\d+)\s+([+-]?\d+)', text, re.IGNORECASE)
    if not m:
        # Check for perfect
        m2 = re.search(r'#travle\s+#(\d+)\s+\(Perfect\)', text, re.IGNORECASE)
        if m2:
            return {"game": "Travle", "number": m2.group(1), "score": -1}
        return None
    return {"game": "Travle", "number": m.group(1), "score": int(m.group(2))}


def parse_connections(text):
    """Connections: count number of non-correct (non-first-try) rows. 
    Each row of 4 emojis that isn't all same color = 1 mistake.
    Actually: count total guesses beyond 4 (the minimum). Max 4 extra = fail."""
    m = re.search(r'Connections\s+Puzzle\s+#(\d+)', text, re.IGNORECASE)
    if not m:
        return None
    number = m.group(1)
    
    # Find emoji section - stop at next game header or double newline
    emoji_section = text[m.end():]
    # Cut off at the next game marker
    next_game = re.search(r'\n\s*\n', emoji_section)
    if next_game:
        emoji_section = emoji_section[:next_game.start()]
    
    # Extract colored squares (only connections colors)
    colors = re.findall(r'[🟪🟩🟨🟦]', emoji_section)
    
    if not colors:
        return None
    
    # Group into rows of 4
    rows = [colors[i:i+4] for i in range(0, len(colors), 4)]
    
    # Count wrong guesses: rows where not all 4 are the same color
    mistakes = 0
    for row in rows:
        if len(row) == 4 and len(set(row)) != 1:
            mistakes += 1
    
    # If more than 4+4=8 rows attempted or didn't solve, cap at 4
    if len(rows) > 7:
        mistakes = 4
    
    return {"game": "Connections", "number": number, "score": mistakes}


def parse_wordle(text):
    """Wordle: score is the number of guesses. X/6 = 7."""
    m = re.search(r'Wordle\s+[\d,]+\s+([X\d])/6', text, re.IGNORECASE)
    if not m:
        return None
    num_m = re.search(r'Wordle\s+([\d,]+)', text, re.IGNORECASE)
    number = num_m.group(1).replace(",", "") if num_m else ""
    score_str = m.group(1)
    score = 7 if score_str.upper() == 'X' else int(score_str)
    return {"game": "Wordle", "number": number, "score": score}


def parse_guess_the_movie(text):
    """GuessTheMovie: count guesses until 🟩. All red = 7."""
    m = re.search(r'#GuessTheMovie\s+#(\d+)', text, re.IGNORECASE)
    if not m:
        return None
    number = m.group(1)
    emoji_section = text[m.end():]
    squares = re.findall(r'[🟥🟩⬜]', emoji_section)
    
    if '🟩' in squares:
        score = squares.index('🟩') + 1
    else:
        score = 7
    return {"game": "GuessTheMovie", "number": number, "score": score}


def parse_guess_the_game(text):
    """GuessTheGame: count guesses until 🟩. All wrong = 7."""
    m = re.search(r'#GuessTheGame\s+#(\d+)', text, re.IGNORECASE)
    if not m:
        return None
    number = m.group(1)
    emoji_section = text[m.end():]
    squares = re.findall(r'[🟥🟨🟩⬜]', emoji_section)
    
    if '🟩' in squares:
        score = squares.index('🟩') + 1
    else:
        score = 7
    return {"game": "GuessTheGame", "number": number, "score": score}


def parse_foodguessr(text):
    """FoodGuessr: extract total score (higher is better)."""
    m = re.search(r'I got ([\d,]+) on the FoodGuessr', text, re.IGNORECASE)
    if not m:
        return None
    score = int(m.group(1).replace(",", ""))
    return {"game": "FoodGuessr", "number": "", "score": score}


def parse_timeguessr(text):
    """TimeGuessr: extract score like 44,237/50,000 (higher is better)."""
    m = re.search(r'TimeGuessr\s+#(\d+)\s+([\d,]+)/([\d,]+)', text, re.IGNORECASE)
    if not m:
        return None
    number = m.group(1)
    score = int(m.group(2).replace(",", ""))
    return {"game": "TimeGuessr", "number": number, "score": score}


def parse_date_from_text(text):
    """Try to extract a date from the text."""
    # Try "Wednesday, Feb 18, 2026" style
    m = re.search(r'(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday),?\s+(\w+)\s+(\d+),?\s+(\d{4})', text)
    if m:
        try:
            date_str = f"{m.group(1)} {m.group(2)} {m.group(3)}"
            dt = datetime.strptime(date_str, "%b %d %Y")
            return dt.strftime("%Y-%m-%d")
        except ValueError:
            pass
    return None


PARSERS = [
    parse_travle,
    parse_connections,
    parse_wordle,
    parse_guess_the_movie,
    parse_guess_the_game,
    parse_foodguessr,
    parse_timeguessr,
]

# Games where higher is better
HIGHER_IS_BETTER = {"FoodGuessr", "TimeGuessr"}
# Games where lower is better
LOWER_IS_BETTER = {"Travle", "Connections", "Wordle", "GuessTheMovie", "GuessTheGame"}

# Score ranges for normalization (for leaderboard)
SCORE_RANGES = {
    "Travle": {"min": -1, "max": 20, "lower_better": True},
    "Connections": {"min": 0, "max": 4, "lower_better": True},
    "Wordle": {"min": 1, "max": 7, "lower_better": True},
    "GuessTheMovie": {"min": 1, "max": 7, "lower_better": True},
    "GuessTheGame": {"min": 1, "max": 7, "lower_better": True},
    "FoodGuessr": {"min": 0, "max": 15000, "lower_better": False},
    "TimeGuessr": {"min": 0, "max": 50000, "lower_better": False},
}


def normalize_score(game, score):
    """Normalize score to 0-100 where 100 is best."""
    r = SCORE_RANGES.get(game)
    if not r:
        return 50
    mn, mx = r["min"], r["max"]
    clamped = max(mn, min(mx, score))
    if r["lower_better"]:
        return 100 * (mx - clamped) / (mx - mn) if mx != mn else 100
    else:
        return 100 * (clamped - mn) / (mx - mn) if mx != mn else 100


# ── Routes ──

@app.route("/")
def index():
    return send_file("index.html")


@app.route("/api/parse", methods=["POST"])
def api_parse():
    data = request.json
    text = data.get("text", "")
    username = data.get("username", "").strip()
    
    if not username:
        return jsonify({"error": "Username required"}), 400
    if not text:
        return jsonify({"error": "No text provided"}), 400
    
    # Try to extract date
    play_date = parse_date_from_text(text) or datetime.now().strftime("%Y-%m-%d")
    
    results = []
    for parser in PARSERS:
        try:
            result = parser(text)
            if result:
                results.append(result)
        except Exception as e:
            pass
    
    if not results:
        return jsonify({"error": "Could not parse any game scores from the text"}), 400
    
    conn = get_db()
    saved = []
    errors = []
    
    for r in results:
        try:
            conn.execute(
                "INSERT INTO scores (username, game, game_number, score_value, raw_text, play_date) VALUES (?, ?, ?, ?, ?, ?)",
                (username.lower(), r["game"], r.get("number", ""), r["score"], text, play_date)
            )
            saved.append({"game": r["game"], "number": r.get("number"), "score": r["score"], "date": play_date})
        except sqlite3.IntegrityError:
            errors.append(f"{r['game']}: already submitted for {play_date}")
    
    conn.commit()
    conn.close()
    
    return jsonify({"saved": saved, "errors": errors, "date": play_date})


@app.route("/api/scores")
def api_scores():
    date = request.args.get("date", datetime.now().strftime("%Y-%m-%d"))
    conn = get_db()
    rows = conn.execute(
        "SELECT username, game, game_number, score_value, play_date FROM scores WHERE play_date = ? ORDER BY game, username",
        (date,)
    ).fetchall()
    conn.close()
    return jsonify([dict(r) for r in rows])


@app.route("/api/leaderboard")
def api_leaderboard():
    """Weekly leaderboard: normalized scores averaged per user."""
    # Get the current week (Mon-Sun)
    today = datetime.now()
    week_offset = int(request.args.get("week_offset", 0))
    
    # Calculate start of week
    start_of_week = today - timedelta(days=today.weekday()) - timedelta(weeks=-week_offset)
    start_of_week = start_of_week.replace(hour=0, minute=0, second=0, microsecond=0)
    end_of_week = start_of_week + timedelta(days=6)
    
    start_str = start_of_week.strftime("%Y-%m-%d")
    end_str = end_of_week.strftime("%Y-%m-%d")
    
    conn = get_db()
    rows = conn.execute(
        "SELECT username, game, score_value, play_date FROM scores WHERE play_date BETWEEN ? AND ? ORDER BY username, play_date",
        (start_str, end_str)
    ).fetchall()
    conn.close()
    
    # Build leaderboard
    user_scores = {}
    user_game_details = {}
    
    for row in rows:
        u = row["username"]
        g = row["game"]
        s = row["score_value"]
        d = row["play_date"]
        
        if u not in user_scores:
            user_scores[u] = []
            user_game_details[u] = {}
        
        norm = normalize_score(g, s)
        user_scores[u].append(norm)
        
        if g not in user_game_details[u]:
            user_game_details[u][g] = []
        user_game_details[u][g].append({"score": s, "normalized": round(norm, 1), "date": d})
    
    leaderboard = []
    for u, scores in user_scores.items():
        avg = sum(scores) / len(scores) if scores else 0
        leaderboard.append({
            "username": u,
            "average_score": round(avg, 1),
            "games_played": len(scores),
            "details": user_game_details[u]
        })
    
    leaderboard.sort(key=lambda x: x["average_score"], reverse=True)
    
    return jsonify({
        "week_start": start_str,
        "week_end": end_str,
        "leaderboard": leaderboard
    })


@app.route("/api/history")
def api_history():
    username = request.args.get("username", "").lower()
    if not username:
        return jsonify([])
    conn = get_db()
    rows = conn.execute(
        "SELECT game, game_number, score_value, play_date FROM scores WHERE username = ? ORDER BY play_date DESC, game",
        (username,)
    ).fetchall()
    conn.close()
    return jsonify([dict(r) for r in rows])


@app.route("/api/delete", methods=["POST"])
def api_delete():
    data = request.json
    username = data.get("username", "").strip().lower()
    game = data.get("game", "")
    date = data.get("date", "")
    
    if not all([username, game, date]):
        return jsonify({"error": "Missing fields"}), 400
    
    conn = get_db()
    conn.execute("DELETE FROM scores WHERE username = ? AND game = ? AND play_date = ?", (username, game, date))
    conn.commit()
    conn.close()
    return jsonify({"ok": True})


# Initialize DB on module load (works with both gunicorn and direct run)
init_db()

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000, debug=True)
