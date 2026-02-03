import json
import re
from pathlib import Path
from typing import Dict, List
from game_engines.base import GameEngineBase


class RpgmakerEngine(GameEngineBase):
    def __init__(self, on_log=None):
        self.on_log = on_log

    def extract_strings(self, game_root: Path) -> List[str]:
        """Extract translatable strings from RPG Maker game files"""
        strings = set()
        data_dir = game_root / "www" / "data"

        if not data_dir.exists():
            data_dir = game_root / "data"

        if not data_dir.exists():
            return []

        # Extract from Map files
        for map_file in data_dir.glob("Map*.json"):
            try:
                with map_file.open("r", encoding="utf-8") as f:
                    data = json.load(f)
                    if isinstance(data, dict) and "events" in data:
                        for event in data.get("events", []):
                            if event and "pages" in event:
                                for page in event["pages"]:
                                    if "list" in page:
                                        for cmd in page["list"]:
                                            if cmd.get("code") in [401, 405]:  # Text commands
                                                text = cmd.get("parameters", [None])[0]
                                                if text and isinstance(text, str) and text.strip():
                                                    strings.add(text.strip())
            except Exception:
                pass

        # Extract from other data files
        for data_file in ["CommonEvents.json", "Troops.json", "Items.json", "Weapons.json", "Armors.json", "Skills.json"]:
            try:
                with (data_dir / data_file).open("r", encoding="utf-8") as f:
                    data = json.load(f)
                    self._extract_text_recursive(data, strings)
            except Exception:
                pass

        return sorted(strings)

    def _extract_text_recursive(self, obj, strings: set):
        """Recursively extract text from JSON objects"""
        if isinstance(obj, dict):
            for key, value in obj.items():
                if key in ["name", "description", "message", "note"] and isinstance(value, str) and value.strip():
                    strings.add(value.strip())
                else:
                    self._extract_text_recursive(value, strings)
        elif isinstance(obj, list):
            for item in obj:
                self._extract_text_recursive(item, strings)

    def write_translations(self, game_root: Path, language: str, translations: Dict[str, str]) -> None:
        """Write translations back to RPG Maker game files"""
        data_dir = game_root / "www" / "data"
        if not data_dir.exists():
            data_dir = game_root / "data"

        trans_dir = game_root / f"translations_{language}"
        trans_dir.mkdir(exist_ok=True)

        # Write translation mapping
        trans_file = trans_dir / "translations.json"
        with trans_file.open("w", encoding="utf-8") as f:
            json.dump(translations, f, ensure_ascii=False, indent=2)

        if self.on_log:
            self.on_log(f"Translations written to: {trans_file}")

    def get_name(self) -> str:
        return "RPG Maker"

    def validate_game_root(self, game_root: Path) -> bool:
        """Check if this is a valid RPG Maker game directory"""
        www_data = game_root / "www" / "data"
        data = game_root / "data"
        return (www_data / "System.json").exists() or (data / "System.json").exists()
