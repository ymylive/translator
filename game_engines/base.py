from abc import ABC, abstractmethod
from pathlib import Path
from typing import Dict, List


class GameEngineBase(ABC):
    """Base class for game engine handlers"""

    @abstractmethod
    def extract_strings(self, game_root: Path) -> List[str]:
        """Extract translatable strings from game files

        Args:
            game_root: Root directory of the game

        Returns:
            List of unique strings to translate
        """
        pass

    @abstractmethod
    def write_translations(self, game_root: Path, language: str, translations: Dict[str, str]) -> None:
        """Write translations to game files

        Args:
            game_root: Root directory of the game
            language: Target language code
            translations: Dictionary mapping original strings to translations
        """
        pass

    @abstractmethod
    def get_name(self) -> str:
        """Get engine name"""
        pass

    @abstractmethod
    def validate_game_root(self, game_root: Path) -> bool:
        """Check if the game root is valid for this engine

        Args:
            game_root: Root directory to validate

        Returns:
            True if valid, False otherwise
        """
        pass
