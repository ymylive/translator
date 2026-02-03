import os
import re
import shutil
import time
from pathlib import Path
from typing import Dict, Iterable, List

import unrpyc

from .base import GameEngineBase


def normalize_text(text: str) -> str:
    text = text.strip()
    if len(text) >= 2 and text[0] == text[-1] and text[0] in ("\"", "'"):
        return text[1:-1]
    return text


def iter_rpyc_files(root: Path) -> Iterable[Path]:
    for path in root.rglob("*.rpyc"):
        if "\\tl\\" in str(path).lower():
            continue
        yield path


def walk_ast(node, visited: set, out: List[str]) -> None:
    if isinstance(node, (list, tuple)):
        for n in node:
            walk_ast(n, visited, out)
        return

    obj_id = id(node)
    if obj_id in visited:
        return
    visited.add(obj_id)

    name = getattr(node.__class__, "__name__", "")

    if name == "Say":
        what = getattr(node, "what", None)
        if isinstance(what, str) and what.strip():
            out.append(normalize_text(what))

    if name == "Menu":
        items = getattr(node, "items", None)
        if isinstance(items, list):
            for item in items:
                if isinstance(item, (list, tuple)) and item:
                    text = item[0]
                    if isinstance(text, str) and text.strip():
                        out.append(normalize_text(text))
                    if len(item) > 2:
                        walk_ast(item[2], visited, out)

    for v in getattr(node, "__dict__", {}).values():
        if isinstance(v, (list, tuple)):
            walk_ast(v, visited, out)
        else:
            if hasattr(v, "__dict__") and v.__class__.__module__.startswith("renpy.ast"):
                walk_ast(v, visited, out)


class RenPyEngine(GameEngineBase):
    """Ren'Py game engine handler"""

    def __init__(self, on_log=None):
        self.on_log = on_log

    def get_name(self) -> str:
        return "RenPy"

    def validate_game_root(self, game_root: Path) -> bool:
        game_dir = game_root / "game"
        return game_dir.exists() and game_dir.is_dir()

    def extract_strings(self, game_root: Path) -> List[str]:
        game_dir = game_root / "game"
        if not game_dir.exists():
            raise RuntimeError(f"game directory not found: {game_dir}")

        strings: List[str] = []
        for rpyc in iter_rpyc_files(game_dir):
            try:
                ctx = unrpyc.Context()
                ast = unrpyc.get_ast(rpyc, try_harder=False, context=ctx)
                walk_ast(ast, set(), strings)
            except Exception as exc:
                if self.on_log:
                    self.on_log(f"skip {rpyc.name}: {exc}")

        seen = set()
        unique_strings = []
        for s in strings:
            if s not in seen:
                seen.add(s)
                unique_strings.append(s)

        return unique_strings

    def write_translations(self, game_root: Path, language: str, translations: Dict[str, str]) -> None:
        game_dir = game_root / "game"
        output_path = game_dir / "tl" / language / "zz_auto_strings.rpy"

        # Backup existing .rpyc files
        backup_dir = self._backup_translation_rpyc(output_path.parent, {"zz_auto_strings.rpyc"})
        if backup_dir and self.on_log:
            self.on_log(f"Backed up existing translation rpyc files: {backup_dir}")

        # Write translation file
        output_path.parent.mkdir(parents=True, exist_ok=True)
        lines = [f"translate {language} strings:", ""]
        for old, new in translations.items():
            old_esc = old.replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", "\\n")
            new_esc = new.replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", "\\n")
            lines.append(f'    old "{old_esc}"')
            lines.append(f'    new "{new_esc}"')
            lines.append("")
        output_path.write_text("\n".join(lines), encoding="utf-8")

        # Remove stale compiled file
        compiled = output_path.with_suffix(output_path.suffix + "c")
        if compiled.exists():
            compiled.unlink()

        if self.on_log:
            self.on_log(f"Wrote translations: {output_path}")

    def _backup_translation_rpyc(self, lang_dir: Path, keep_names: set) -> Path:
        rpyc_files = [p for p in lang_dir.rglob("*.rpyc") if p.name not in keep_names]
        if not rpyc_files:
            return None
        stamp = time.strftime("%Y%m%d_%H%M%S")
        backup_dir = lang_dir.parents[2] / f"_translation_rpyc_backup_{stamp}" / lang_dir.name
        backup_dir.mkdir(parents=True, exist_ok=True)
        for path in rpyc_files:
            dest = backup_dir / path.name
            shutil.move(str(path), dest)
        return backup_dir
