"""
Glossary Manager for consistent translation of game-specific terms.

Manages translation dictionaries/glossaries to ensure consistent
translation of character names, items, locations, and other game terms.
Inspired by LunaTranslator's GptDict feature.
"""

import json
import re
from pathlib import Path
from typing import Dict, List, Optional, Tuple
from dataclasses import dataclass, field


@dataclass
class GlossaryEntry:
    """A single glossary entry."""
    source: str
    target: str
    context: str = ""
    category: str = "general"
    case_sensitive: bool = True
    regex: bool = False

    def to_dict(self) -> Dict[str, str]:
        return {
            "src": self.source,
            "dst": self.target,
            "info": self.context,
            "category": self.category,
        }

    @classmethod
    def from_dict(cls, data: Dict) -> "GlossaryEntry":
        return cls(
            source=data.get("src", data.get("source", "")),
            target=data.get("dst", data.get("target", "")),
            context=data.get("info", data.get("context", "")),
            category=data.get("category", "general"),
            case_sensitive=data.get("case_sensitive", True),
            regex=data.get("regex", False),
        )


class GlossaryManager:
    """
    Manages translation glossaries for consistent term translation.

    Features:
    - Multiple glossary support (per-game, global)
    - Category-based organization
    - Pre/post translation term replacement
    - Export to LLM-friendly format
    """

    def __init__(self):
        self.entries: List[GlossaryEntry] = []
        self._by_category: Dict[str, List[GlossaryEntry]] = {}
        self._source_index: Dict[str, GlossaryEntry] = {}

    def add_entry(self, entry: GlossaryEntry):
        """Add a glossary entry."""
        self.entries.append(entry)

        # Index by category
        if entry.category not in self._by_category:
            self._by_category[entry.category] = []
        self._by_category[entry.category].append(entry)

        # Index by source (for quick lookup)
        key = entry.source if entry.case_sensitive else entry.source.lower()
        self._source_index[key] = entry

    def add_entries(self, entries: List[GlossaryEntry]):
        """Add multiple glossary entries."""
        for entry in entries:
            self.add_entry(entry)

    def remove_entry(self, source: str):
        """Remove an entry by source term."""
        key = source.lower()
        if key in self._source_index:
            entry = self._source_index[key]
            self.entries.remove(entry)
            self._by_category[entry.category].remove(entry)
            del self._source_index[key]

    def get_entry(self, source: str) -> Optional[GlossaryEntry]:
        """Get entry by source term."""
        # Try exact match first
        if source in self._source_index:
            return self._source_index[source]
        # Try case-insensitive
        return self._source_index.get(source.lower())

    def get_by_category(self, category: str) -> List[GlossaryEntry]:
        """Get all entries in a category."""
        return self._by_category.get(category, [])

    @property
    def categories(self) -> List[str]:
        """Get all categories."""
        return list(self._by_category.keys())

    def apply_pre_translation(self, text: str) -> Tuple[str, Dict[str, str]]:
        """
        Apply glossary before translation by replacing terms with placeholders.
        Uses LunaTranslator-style placeholder format (ZX{letter}Z).

        Returns:
            Tuple of (modified_text, {placeholder: original_term})
        """
        placeholders: Dict[str, str] = {}
        result = text

        for i, entry in enumerate(self.entries):
            if entry.regex:
                pattern = entry.source
            else:
                pattern = re.escape(entry.source)

            flags = 0 if entry.case_sensitive else re.IGNORECASE
            # LunaTranslator style placeholder: ZX{B+i}Z
            placeholder = f"ZX{chr(ord('B') + (i % 24))}Z"
            if i >= 24:
                placeholder = f"ZX{chr(ord('B') + (i % 24))}{i // 24}Z"

            matches = list(re.finditer(pattern, result, flags))
            for match in reversed(matches):
                placeholders[placeholder] = match.group(0)
                result = result[:match.start()] + placeholder + result[match.end():]

        return result, placeholders

    def apply_post_translation(
        self,
        text: str,
        placeholders: Dict[str, str]
    ) -> str:
        """
        Apply glossary after translation by replacing placeholders with translations.
        Handles case-insensitive placeholder matching.

        Args:
            text: Translated text with placeholders
            placeholders: Mapping from placeholder to original term

        Returns:
            Text with glossary terms properly translated
        """
        result = text

        for placeholder, original in placeholders.items():
            # Find the glossary entry for this term
            entry = self.get_entry(original)
            if entry:
                # Case-insensitive replacement for placeholder
                pattern = re.escape(placeholder)
                result = re.sub(pattern, entry.target, result, flags=re.IGNORECASE)
            else:
                # No entry found, restore original
                result = re.sub(re.escape(placeholder), original, result, flags=re.IGNORECASE)

        return result

    def process_before(self, text: str) -> Tuple[str, Dict]:
        """
        LunaTranslator-compatible pre-processing.
        Returns processed text and context dict for post-processing.
        """
        processed, placeholders = self.apply_pre_translation(text)
        return processed, {"placeholders": placeholders, "original": text}

    def process_after(self, text: str, context: Dict) -> str:
        """
        LunaTranslator-compatible post-processing.
        Restores placeholders to target translations.
        """
        placeholders = context.get("placeholders", {})
        return self.apply_post_translation(text, placeholders)

    def to_prompt_format(self, categories: Optional[List[str]] = None) -> str:
        """
        Export glossary to LLM prompt format.

        Args:
            categories: Optional list of categories to include

        Returns:
            Formatted string for LLM prompt
        """
        entries = self.entries
        if categories:
            entries = [e for e in entries if e.category in categories]

        if not entries:
            return ""

        lines = ["Translation glossary (use these exact translations):"]
        for entry in entries:
            line = f"- {entry.source} → {entry.target}"
            if entry.context:
                line += f" ({entry.context})"
            lines.append(line)

        return "\n".join(lines)

    def to_gpt_dict_format(self) -> List[Dict[str, str]]:
        """
        Export to GPT dictionary format (LunaTranslator compatible).

        Returns:
            List of dictionaries with src, dst, info keys
        """
        return [entry.to_dict() for entry in self.entries]

    def save(self, path: Path):
        """Save glossary to JSON file."""
        data = {
            "version": "1.0",
            "entries": [
                {
                    "source": e.source,
                    "target": e.target,
                    "context": e.context,
                    "category": e.category,
                    "case_sensitive": e.case_sensitive,
                    "regex": e.regex,
                }
                for e in self.entries
            ]
        }
        with path.open('w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)

    def load(self, path: Path):
        """Load glossary from JSON file."""
        if not path.exists():
            return

        with path.open('r', encoding='utf-8') as f:
            data = json.load(f)

        for entry_data in data.get("entries", []):
            entry = GlossaryEntry(
                source=entry_data.get("source", ""),
                target=entry_data.get("target", ""),
                context=entry_data.get("context", ""),
                category=entry_data.get("category", "general"),
                case_sensitive=entry_data.get("case_sensitive", True),
                regex=entry_data.get("regex", False),
            )
            self.add_entry(entry)

    def load_from_csv(self, path: Path, delimiter: str = ","):
        """
        Load glossary from CSV file.

        Expected format: source,target,context,category
        """
        if not path.exists():
            return

        with path.open('r', encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith('#'):
                    continue

                parts = line.split(delimiter)
                if len(parts) >= 2:
                    entry = GlossaryEntry(
                        source=parts[0].strip(),
                        target=parts[1].strip(),
                        context=parts[2].strip() if len(parts) > 2 else "",
                        category=parts[3].strip() if len(parts) > 3 else "general",
                    )
                    self.add_entry(entry)

    def clear(self):
        """Clear all entries."""
        self.entries.clear()
        self._by_category.clear()
        self._source_index.clear()

    def __len__(self) -> int:
        return len(self.entries)

    def __iter__(self):
        return iter(self.entries)

    def __repr__(self) -> str:
        return f"GlossaryManager({len(self.entries)} entries, categories={self.categories})"


# Default categories for game translation
DEFAULT_CATEGORIES = [
    "character",    # Character names
    "location",     # Place names
    "item",         # Items, weapons, etc.
    "skill",        # Skills, abilities
    "ui",           # UI elements
    "system",       # System messages
    "general",      # General terms
]


def create_default_glossary() -> GlossaryManager:
    """Create a glossary manager with common game translation terms."""
    manager = GlossaryManager()

    # Add some common placeholder entries
    common_entries = [
        GlossaryEntry("[player]", "[player]", "Player name variable", "system"),
        GlossaryEntry("[name]", "[name]", "Character name variable", "system"),
        GlossaryEntry("{w}", "{w}", "Wait tag", "system"),
        GlossaryEntry("{p}", "{p}", "Pause tag", "system"),
        GlossaryEntry("{nw}", "{nw}", "No-wait tag", "system"),
        GlossaryEntry("{fast}", "{fast}", "Fast display tag", "system"),
    ]

    manager.add_entries(common_entries)
    return manager
