#!/usr/bin/env python3
"""Writes src/KnowBall.Infrastructure/Data/nfl-honors.json: the AP All-Pro teams and the Pro Bowl rosters of
every NFL season since 1999, read from Wikipedia's yearly pages (text under CC BY-SA 4.0). No open data set
has these by season, so the file is kept in the repository and this script is re-run once a year, after the
All-Pro team is named in January and the Pro Bowl rosters in December:

    python3 scripts/nfl-honors.py            # every season through the last one played
    python3 scripts/nfl-honors.py --check    # print the counts without writing

The importer matches each name to a player by name and the team he was on that season (NflHonors.cs).
An entry is [name, franchise code] (the keys of nfl-teams.json, so "LV" for the Oakland Raiders); the team
settles players who share a name. A name the importer cannot place is skipped, never guessed.
"""
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import date
from pathlib import Path

FIRST_SEASON = 1999
DATA = Path(__file__).resolve().parent.parent / "src/KnowBall.Infrastructure/Data"
OUT = DATA / "nfl-honors.json"
TEAMS = DATA / "nfl-teams.json"
SOURCE = "Wikipedia yearly All-Pro and Pro Bowl pages (CC BY-SA 4.0)"
AGENT = "KnowBall/1.0 (https://github.com/natezimm/know-ball; yearly honors list)"


def page(title: str) -> str | None:
    url = "https://en.wikipedia.org/w/index.php?" + urllib.parse.urlencode({"title": title, "action": "raw"})
    request = urllib.request.Request(url, headers={"User-Agent": AGENT})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            wiki = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise
    finally:
        time.sleep(0.5)  # be gentle with Wikipedia
    redirect = re.match(r"#redirect\s*\[\[([^\]|#]+)", wiki, re.I)
    return page(redirect.group(1)) if redirect else wiki


LINK = re.compile(r"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]")


def text(wiki: str) -> str:
    """Wiki markup to plain text: links to their shown text, templates and tags dropped."""
    wiki = re.sub(r"\{\{[Rr]ef label[^}]*\}\}|\{\{[Ss]mall\|[^}]*\}\}|<ref[^>]*/>|<ref[^>]*>.*?</ref>", "", wiki)
    wiki = LINK.sub(lambda m: m.group(2) or m.group(1), wiki)
    wiki = re.sub(r"\{\{[^}]*\}\}|'''?|<[^>]+>", "", wiki)
    return re.sub(r"\s+", " ", wiki.replace("\u2019", "'")).strip()


def _team_names():
    """Nickname and city -> franchise code, from the importer's own team list, plus Washington's old names."""
    teams = json.loads(TEAMS.read_text(encoding="utf-8"))
    names, cities = {"Redskins": "WAS", "Team": "WAS"}, {}
    for t in teams["current"]:
        names[t["name"]] = t["key"]
        cities.setdefault(t["city"], set()).add(t["key"])
    for t in teams["former"]:
        cities.setdefault(t["city"], set()).add(t["franchise"])
    # A city two teams share ("New York") settles nothing.
    return names, {city: codes.pop() for city, codes in cities.items() if len(codes) == 1}


NAMES, CITIES = _team_names()


def team_code(team_wiki: str) -> str:
    """'[[2023 Baltimore Ravens season|Baltimore]]', 'Oakland Raiders' or 'Cleveland' -> 'BAL' / 'LV' / 'CLE'; '' if unknown."""
    m = LINK.search(team_wiki)
    name = m.group(1) if m else team_wiki
    name = re.sub(r"^\d{4} | season$", "", name.strip())
    name = re.sub(r"\(.*?\)|[^\w .']", "", name).strip()
    if not name:
        return ""
    last = name.split(" ")[-1]
    for nick, code in NAMES.items():
        if last == nick or (len(last) >= 4 and nick.startswith(last)):  # 'Colt', a typo on one page
            return code
    for city, code in CITIES.items():
        if name == city or city.endswith(" " + name) or name.startswith(city):
            return code
    return ""


def entries(cell: str):
    """One table cell of a roster or All-Pro table: 'Name, Team (tags)' pieces split on <br>."""
    for piece in re.split(r"<br\s*/?>", cell, flags=re.I):
        piece = piece.strip().lstrip("|").strip()
        if not piece or "," not in piece:
            continue
        # The first comma outside a link: '[[Chris Jones (defensive tackle, born 1994)|Chris Jones]], ...'
        depth, cut = 0, -1
        for i, ch in enumerate(piece):
            depth += piece.startswith("[[", i) - piece.startswith("]]", i)
            if ch == "," and depth == 0:
                cut = i
                break
        if cut < 0:
            continue
        name_wiki, rest = piece[:cut], piece[cut + 1:]
        name = text(name_wiki)
        team = team_code(rest.split("(")[0])
        tags = re.search(r"\(([^)]*)\)[\s;,.]*$", text(rest))
        if " " in name and team:
            yield name, team, tags.group(1) if tags else ""


def all_pro(season: int):
    wiki = page(f"{season} All-Pro Team")
    if wiki is None:
        return None
    # Only the teams themselves: a later "By NFL team" table repeats the names with positions for teams.
    wiki = re.split(r"\n==\s*(?:Key|Position|By |Notes|References|See also)", wiki)[0]
    first, second = [], []
    for line in wiki.splitlines():
        if not line.startswith("|") or "(" not in line or "," not in line:
            continue
        for name, team, tags in entries(line):
            parts = [t.strip().rstrip("*") for t in tags.split(",")]  # '*' marks a different position
            if any(re.fullmatch(r"AP(-t)?", t) for t in parts):
                bucket = first
            elif any(re.fullmatch(r"AP-2t?", t) for t in parts):
                bucket = second
            else:
                continue
            if [name, team] not in bucket:
                bucket.append([name, team])
    return first, second


SMALL = re.compile(r"\{\{[Ss]mall\|[^}]*\}\}\s*(?:''+)?\s*(\[\[[^\]]+\]\])\s*(?:''+)?\s*,\s*(\[\[[^\]]+\]\])")
NFLPLAYER = re.compile(r"\{\{NFLplayer\|[^|}]*\|\s*([^|}]+?)\s*\|[^{}]*?\(\s*(\[\[[^\]]+\]\])\s*\)", re.I)


def pro_bowl(season: int):
    """The rosters of the game played after the season: '2001 Pro Bowl' for 2000, '2024 Pro Bowl Games' for 2023."""
    year = season + 1
    for title in (f"{year} Pro Bowl Games", f"{year} Pro Bowl"):
        wiki = page(title)
        if wiki:
            break
    else:
        return None
    rosters = re.split(r"\n==+[^=\n]*[Rr]oster[^=\n]*==+\n", wiki, maxsplit=1)
    if len(rosters) < 2:
        return []
    body = re.split(r"\n==\s*(?:[Nn]otes|[Rr]eferences|[Nn]umber of selections|[Ee]xternal)", rosters[1])[0]
    seen, players = set(), []
    # A player line starts with his number, in {{Small|..}} before the name or as the first value of
    # {{NFLplayer|number|name|..|([[team]])}} (the draft years, 2014-16); coaches and headings have neither.
    lines = [(m.group(1), m.group(2)) for m in SMALL.finditer(body)]
    lines += [(m.group(1), m.group(2)) for m in NFLPLAYER.finditer(body)]
    for name_wiki, team_wiki in lines:
        name, team = text(name_wiki), team_code(team_wiki)
        if " " in name and team and (name, team) not in seen:
            seen.add((name, team))
            players.append([name, team])
    return players


def main() -> None:
    check = "--check" in sys.argv
    today = date.today()
    last = today.year - 1 if today.month < 9 else today.year
    honors = {}
    for season in range(FIRST_SEASON, last + 1):
        ap = all_pro(season)
        pb = pro_bowl(season)
        row = {}
        if ap:
            row["allPro1"], row["allPro2"] = ap
        if pb:
            row["proBowl"] = pb
        print(f"{season}: All-Pro {len(row.get('allPro1', []))}/{len(row.get('allPro2', []))}, Pro Bowl {len(row.get('proBowl', []))}")
        if row:
            honors[str(season)] = row
    if not check:
        OUT.write_text(render(honors), encoding="utf-8")
        print(f"Wrote {OUT}")


def render(honors: dict) -> str:
    """JSON with one player per line, so a yearly re-run shows up in a diff as the lines it changed."""
    dump = lambda value: json.dumps(value, ensure_ascii=False)
    out = ["{", f'"source": {dump(SOURCE)},', '"seasons": {']
    for i, (season, row) in enumerate(honors.items()):
        out.append(f"{dump(season)}: {{")
        for j, (key, players) in enumerate(row.items()):
            out.append(f"{dump(key)}: [")
            out += [dump(p) + ("," if k < len(players) - 1 else "") for k, p in enumerate(players)]
            out.append("]" + ("," if j < len(row) - 1 else ""))
        out.append("}" + ("," if i < len(honors) - 1 else ""))
    out += ["}", "}"]
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    main()
