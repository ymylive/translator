"""
Post-processor for translation results.

Inspired by LunaTranslator's transoptimi/transerrorfix.py.
Provides text replacement rules to fix common translation errors.
"""

import re
from pathlib import Path
from typing import List, Dict, Optional, Callable
from dataclasses import dataclass, field

from config_utils import safe_save_json, safe_load_json


@dataclass
class PostProcessRule:
    """A single post-processing rule."""
    pattern: str
    replacement: str
    is_regex: bool = False
    enabled: bool = True
    description: str = ""

    def to_dict(self) -> Dict:
        return {
            "pattern": self.pattern,
            "replacement": self.replacement,
            "is_regex": self.is_regex,
            "enabled": self.enabled,
            "description": self.description,
        }

    @classmethod
    def from_dict(cls, data: Dict) -> "PostProcessRule":
        return cls(
            pattern=data.get("pattern", ""),
            replacement=data.get("replacement", ""),
            is_regex=data.get("is_regex", False),
            enabled=data.get("enabled", True),
            description=data.get("description", ""),
        )


class PostProcessor:
    """
    Translation result post-processor.

    Features:
    - Plain text and regex replacement rules
    - Rule enable/disable
    - Rule ordering
    - Persistent storage
    """

    def __init__(self):
        self.rules: List[PostProcessRule] = []
        self._enabled = True

    @property
    def enabled(self) -> bool:
        return self._enabled

    @enabled.setter
    def enabled(self, value: bool):
        self._enabled = value

    def add_rule(
        self,
        pattern: str,
        replacement: str,
        is_regex: bool = False,
        description: str = ""
    ) -> PostProcessRule:
        """Add a replacement rule."""
        rule = PostProcessRule(
            pattern=pattern,
            replacement=replacement,
            is_regex=is_regex,
            description=description,
        )
        self.rules.append(rule)
        return rule

    def remove_rule(self, index: int) -> bool:
        """Remove a rule by index."""
        if 0 <= index < len(self.rules):
            self.rules.pop(index)
            return True
        return False

    def move_rule(self, from_index: int, to_index: int) -> bool:
        """Move a rule to a different position."""
        if 0 <= from_index < len(self.rules) and 0 <= to_index < len(self.rules):
            rule = self.rules.pop(from_index)
            self.rules.insert(to_index, rule)
            return True
        return False

    def process(self, text: str) -> str:
        """
        Apply all enabled rules to text.

        Args:
            text: Input text to process

        Returns:
            Processed text
        """
        if not self._enabled:
            return text

        result = text
        for rule in self.rules:
            if not rule.enabled:
                continue

            try:
                if rule.is_regex:
                    result = re.sub(rule.pattern, rule.replacement, result)
                else:
                    result = result.replace(rule.pattern, rule.replacement)
            except re.error:
                # Skip invalid regex patterns
                continue

        return result

    def process_batch(self, texts: List[str]) -> List[str]:
        """Process multiple texts."""
        return [self.process(text) for text in texts]

    def test_rule(self, rule_index: int, test_text: str) -> Optional[str]:
        """Test a single rule on sample text."""
        if not 0 <= rule_index < len(self.rules):
            return None

        rule = self.rules[rule_index]
        try:
            if rule.is_regex:
                return re.sub(rule.pattern, rule.replacement, test_text)
            else:
                return test_text.replace(rule.pattern, rule.replacement)
        except re.error:
            return None

    def load_from_file(self, path: Path) -> int:
        """
        Load rules from JSON file.

        Returns:
            Number of rules loaded
        """
        data = safe_load_json(path, {"rules": [], "enabled": True})
        self._enabled = data.get("enabled", True)
        self.rules = [
            PostProcessRule.from_dict(r)
            for r in data.get("rules", [])
        ]
        return len(self.rules)

    def save_to_file(self, path: Path) -> bool:
        """Save rules to JSON file."""
        data = {
            "version": "1.0",
            "enabled": self._enabled,
            "rules": [rule.to_dict() for rule in self.rules],
        }
        return safe_save_json(path, data)

    def clear(self):
        """Clear all rules."""
        self.rules.clear()

    def __len__(self) -> int:
        return len(self.rules)

    def __iter__(self):
        return iter(self.rules)


# Common post-processing rules for game translation
DEFAULT_RULES = [
    PostProcessRule(
        pattern=r"\s+",
        replacement=" ",
        is_regex=True,
        description="Normalize whitespace",
    ),
    PostProcessRule(
        pattern="...",
        replacement="……",
        is_regex=False,
        description="Convert ellipsis to Chinese style",
    ),
    PostProcessRule(
        pattern=r"「([^」]+)」",
        replacement=r"「\1」",
        is_regex=True,
        description="Preserve Japanese quotes",
    ),
]


def create_default_processor() -> PostProcessor:
    """Create a post-processor with common rules."""
    processor = PostProcessor()
    for rule in DEFAULT_RULES:
        processor.rules.append(rule)
    return processor
