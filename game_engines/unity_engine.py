import json
from pathlib import Path
from typing import Dict, List
from game_engines.base import GameEngineBase


class UnityEngine(GameEngineBase):
    def __init__(self, on_log=None):
        self.on_log = on_log

    def extract_strings(self, game_root: Path) -> List[str]:
        """Extract translatable strings from Unity game files"""
        strings = set()

        # Look for common Unity text asset locations
        data_dirs = [
            game_root / "Data" / "StreamingAssets",
            game_root / "StreamingAssets",
            game_root / "Resources"
        ]

        for data_dir in data_dirs:
            if not data_dir.exists():
                continue

            # Extract from JSON files
            for json_file in data_dir.rglob("*.json"):
                try:
                    with json_file.open("r", encoding="utf-8") as f:
                        data = json.load(f)
                        self._extract_text_recursive(data, strings)
                except Exception:
                    pass

            # Extract from text files
            for txt_file in data_dir.rglob("*.txt"):
                try:
                    with txt_file.open("r", encoding="utf-8") as f:
                        for line in f:
                            line = line.strip()
                            if line and not line.startswith("#"):
                                strings.add(line)
                except Exception:
                    pass

        return sorted(strings)

    def _extract_text_recursive(self, obj, strings: set):
        """Recursively extract text from JSON objects"""
        if isinstance(obj, dict):
            for key, value in obj.items():
                if isinstance(value, str) and value.strip() and len(value) > 3:
                    strings.add(value.strip())
                else:
                    self._extract_text_recursive(value, strings)
        elif isinstance(obj, list):
            for item in obj:
                self._extract_text_recursive(item, strings)

    def write_translations(self, game_root: Path, language: str, translations: Dict[str, str]) -> None:
        """Write translations for Unity game"""
        trans_dir = game_root / f"translations_{language}"
        trans_dir.mkdir(exist_ok=True)

        trans_file = trans_dir / "translations.json"
        with trans_file.open("w", encoding="utf-8") as f:
            json.dump(translations, f, ensure_ascii=False, indent=2)

        if self.on_log:
            self.on_log(f"Translations written to: {trans_file}")

    def get_name(self) -> str:
        return "Unity"

    def validate_game_root(self, game_root: Path) -> bool:
        """Check if this is a valid Unity game directory"""
        return (game_root / "UnityPlayer.dll").exists() or (game_root / "Data").exists()
