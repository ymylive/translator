import importlib
import inspect
from pathlib import Path
from typing import Dict, Type

from game_engines.base import GameEngineBase
from translators.base import TranslatorBase


class PluginManager:
    def __init__(self):
        self._engines_cache = None
        self._translators_cache = None

    def discover_engines(self) -> Dict[str, Type[GameEngineBase]]:
        if self._engines_cache is not None:
            return self._engines_cache

        engines = {}
        engines_dir = Path(__file__).parent / "game_engines"

        for py_file in engines_dir.glob("*_engine.py"):
            module_name = f"game_engines.{py_file.stem}"
            try:
                module = importlib.import_module(module_name)
                for name, obj in inspect.getmembers(module, inspect.isclass):
                    if issubclass(obj, GameEngineBase) and obj != GameEngineBase:
                        engine_name = py_file.stem.replace("_engine", "")
                        engines[engine_name] = obj
            except Exception:
                pass

        self._engines_cache = engines
        return engines

    def discover_translators(self) -> Dict[str, Type[TranslatorBase]]:
        if self._translators_cache is not None:
            return self._translators_cache

        translators = {}
        translators_dir = Path(__file__).parent / "translators"

        for py_file in translators_dir.glob("*_translator.py"):
            module_name = f"translators.{py_file.stem}"
            try:
                module = importlib.import_module(module_name)
                for name, obj in inspect.getmembers(module, inspect.isclass):
                    if issubclass(obj, TranslatorBase) and obj != TranslatorBase:
                        translator_name = py_file.stem.replace("_translator", "")
                        translators[translator_name] = obj
            except Exception:
                pass

        self._translators_cache = translators
        return translators

    def get_engine_metadata(self, name: str) -> Dict:
        engines = self.discover_engines()
        if name not in engines:
            return {}
        return {"name": name, "class": engines[name].__name__}

    def get_translator_metadata(self, name: str) -> Dict:
        translators = self.discover_translators()
        if name not in translators:
            return {}
        return {"name": name, "class": translators[name].__name__}
