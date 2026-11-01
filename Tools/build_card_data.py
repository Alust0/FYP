"""Builds Assets/StreamingAssets/Deck/, the deck builder's card details, from the scraper's
per-set JSON files. Only the D and DZ series are kept.

    python Tools/build_card_data.py "C:/Users/AMD/Downloads/New folder (3)/sets"

Output: one <SET>.json per set, {"D-BT01-001": {fields}, ...}, keyed and named like
CardFirestoreDatabase's cache (CardKey, camelCase field names), plus index.json with every set
code and nation.
"""
import glob
import json
import os
import sys

SERIES = ("D-", "DZ-")

# scraper field -> game field
TEXT_FIELDS = {
    "name": "name", "name_en": "nameEn", "number": "number", "type": "type", "type_en": "typeEn",
    "clan": "clan", "clan_en": "clanEn", "nation": "nation", "nation_en": "nationEn", "race": "race",
    "race_en": "raceEn", "rarity": "rarity", "skill": "skill", "skill_en": "skillEn",
    "trigger_type": "triggerType", "trigger_type_en": "triggerTypeEn", "flavor": "flavor",
    "flavor_en": "flavorEn", "effect": "effect", "effect_en": "effectEn", "productName": "productName",
}
NUMBER_FIELDS = ("grade", "power", "shield", "critical")

# nation_en left in Japanese (mostly dual-nation cards), so the deck builder's one-nation rule matches them
NATIONS_EN = {
    "ドラゴンエンパイア": "Dragon Empire", "ダークステイツ": "Dark States", "ブラントゲート": "Brandt Gate",
    "ケテルサンクチュアリ": "Keter Sanctuary", "ストイケイア": "Stoicheia", "リリカルモナステリオ": "Lyrical Monasterio",
    "ダークゾーン": "Dark Zone", "スターゲート": "Star Gate", "メガラニカ": "Magallanica",
    "コロコロ": "CoroCoro", "バディファイト": "buddy fight", "ぶいすぽっ！": "VSPO!",
    "ペルソナシリーズ": "Persona", "イナズマイレブン": "Inazuma Eleven",
    "クレイエレメンタル": "",  # Cray Elementals have no nation, so they fit any deck
    # the scraper's own machine-translated nation_en spellings
    "Blunt Gate": "Brandt Gate", "Ketel Sanctuary": "Keter Sanctuary", "megalanica": "Magallanica",
    "Stoikeia": "Stoicheia", "inazuma eleven": "Inazuma Eleven", "persona series": "Persona",
    "Brandgate": "Brandt Gate", "Brantgate": "Brandt Gate", "Megalanica": "Magallanica",
    "Stargate": "Star Gate", "Buspo!": "VSPO!", "clay elemental": "",
}
# The same misspellings in names and card text
TEXT_NATION_FIXES = {
    "Blunt Gate": "Brandt Gate", "Brandgate": "Brandt Gate", "Brantgate": "Brandt Gate",
    "Ketel Sanctuary": "Keter Sanctuary", "megalanica": "Magallanica", "Megalanica": "Magallanica",
    "Stoikeia": "Stoicheia", "Stargate": "Star Gate", "Buspo!": "VSPO!", "clay elemental": "Cray Elemental",
}


def card_key(number):
    key = number.strip().upper().replace("/", "-")
    for ch in ".#$[]":
        key = key.replace(ch, "_")
    return key


def english_nation(value):
    parts = [NATIONS_EN.get(part.strip(), part.strip()) for part in value.split("/")]
    return " / ".join(part for part in parts if part)


def to_fields(card):
    fields = {}
    for src, dst in TEXT_FIELDS.items():
        value = card.get(src)
        if isinstance(value, str):
            fields[dst] = value
    # Both, as the game falls back to nation when nationEn is empty
    for name in ("nation", "nationEn"):
        if name in fields:
            fields[name] = english_nation(fields[name])
    for name in ("nameEn", "skillEn", "effectEn", "flavorEn"):
        for wrong, right in TEXT_NATION_FIXES.items():
            if name in fields:
                fields[name] = fields[name].replace(wrong, right)
    for name in NUMBER_FIELDS:
        value = card.get(name)
        fields[name] = int(value) if isinstance(value, (int, float)) else 0
    return fields


def completeness(fields):
    return sum(len(v) for v in fields.values() if isinstance(v, str))


def main():
    source = sys.argv[1]
    out_dir = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "Assets", "StreamingAssets", "Deck"))
    os.makedirs(out_dir, exist_ok=True)

    cards = {}
    set_of = {}
    repeats = 0
    for path in sorted(glob.glob(os.path.join(source, "*.json"))):
        with open(path, encoding="utf-8") as f:
            for card in json.load(f):
                number = card.get("number") or ""
                if not number.startswith(SERIES):
                    continue
                key = card_key(number)
                fields = to_fields(card)
                if key in cards:
                    repeats += 1
                    if completeness(fields) <= completeness(cards[key]):
                        continue
                cards[key] = fields
                set_of[key] = number.split("/")[0].strip().upper()

    by_set = {}
    for key, fields in cards.items():
        by_set.setdefault(set_of[key], {})[key] = fields
    for set_code, set_cards in by_set.items():
        with open(os.path.join(out_dir, set_code + ".json"), "w", encoding="utf-8") as f:
            json.dump(set_cards, f, ensure_ascii=False, separators=(",", ":"), sort_keys=True)

    nations = sorted({
        part.strip()
        for fields in cards.values()
        for part in (fields.get("nationEn") or fields.get("nation") or "").split("/")
        if part.strip() not in ("", "-")
    }, key=str.lower)
    rarities = sorted({
        (fields.get("rarity") or "").strip()
        for fields in cards.values()
        if (fields.get("rarity") or "").strip() not in ("", "-")
    }, key=str.lower)
    with open(os.path.join(out_dir, "index.json"), "w", encoding="utf-8") as f:
        json.dump({"sets": sorted(by_set), "nations": nations, "rarities": rarities}, f, ensure_ascii=False, indent=1)

    print(f"{len(cards)} cards ({repeats} repeats merged) in {len(by_set)} sets, {len(nations)} nations, "
          f"{len(rarities)} rarities -> {out_dir}")


if __name__ == "__main__":
    main()
