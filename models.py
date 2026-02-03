"""
Data models for the translation pipeline.

Provides TypedDict classes and enums for type-safe data handling
across extraction, translation, and merge phases.
Inspired by teo-lin/renpy-translator's robust data modeling.
"""

from typing import TypedDict, Literal, Optional, List, Dict, Any
from enum import Enum
from dataclasses import dataclass, field


class BlockType(str, Enum):
    """Types of blocks in a Ren'Py translation file."""
    DIALOGUE = "dialogue"
    NARRATOR = "narrator"
    STRING = "string"
    MENU = "menu"


class TranslationStatus(str, Enum):
    """Status of a translation entry."""
    PENDING = "pending"
    TRANSLATED = "translated"
    REVIEWED = "reviewed"
    FAILED = "failed"


@dataclass
class TranslationEntry:
    """A single translation entry with metadata."""
    original: str
    translated: str = ""
    status: TranslationStatus = TranslationStatus.PENDING
    source_file: str = ""
    line_number: int = 0
    block_type: BlockType = BlockType.STRING
    character: Optional[str] = None
    context: List[str] = field(default_factory=list)
    tags: List[Dict[str, Any]] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "original": self.original,
            "translated": self.translated,
            "status": self.status.value,
            "source_file": self.source_file,
            "line_number": self.line_number,
            "block_type": self.block_type.value,
            "character": self.character,
            "context": self.context,
            "tags": self.tags,
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "TranslationEntry":
        return cls(
            original=data.get("original", ""),
            translated=data.get("translated", ""),
            status=TranslationStatus(data.get("status", "pending")),
            source_file=data.get("source_file", ""),
            line_number=data.get("line_number", 0),
            block_type=BlockType(data.get("block_type", "string")),
            character=data.get("character"),
            context=data.get("context", []),
            tags=data.get("tags", []),
        )


@dataclass
class GlossaryEntry:
    """A glossary/dictionary entry for consistent translation."""
    source: str
    target: str
    context: str = ""
    category: str = "general"

    def to_dict(self) -> Dict[str, str]:
        return {
            "src": self.source,
            "dst": self.target,
            "info": self.context,
            "category": self.category,
        }


@dataclass
class TranslationBatch:
    """A batch of texts to translate together."""
    entries: List[TranslationEntry]
    batch_id: int = 0
    total_chars: int = 0

    def __post_init__(self):
        self.total_chars = sum(len(e.original) for e in self.entries)

    @property
    def texts(self) -> List[str]:
        return [e.original for e in self.entries]

    @property
    def size(self) -> int:
        return len(self.entries)


class TagInfo(TypedDict):
    """Information about a tag in text."""
    pos: int
    tag: str
    tag_type: Optional[str]


class ParsedBlock(TypedDict, total=False):
    """A parsed translation block."""
    original: str
    translated: str
    block_type: str
    label: Optional[str]
    location: Optional[str]
    character: Optional[str]
    tags: List[TagInfo]


class TranslationCache(TypedDict):
    """Structure for translation cache file."""
    version: str
    game_path: str
    language: str
    entries: Dict[str, str]
    metadata: Dict[str, Any]


class ProjectConfig(TypedDict, total=False):
    """Project configuration structure."""
    game_root: str
    game_engine: str
    target_language: str
    source_language: str
    translator_type: str
    model: str
    batch_size: int
    max_chars: int
    workers: int
    glossary: List[Dict[str, str]]
