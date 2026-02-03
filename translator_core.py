import hashlib
import json
import os
import re
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from threading import Lock
from typing import Dict, List, Optional, Tuple

from plugin_manager import PluginManager
from game_engines.detector import EngineDetector
from translators.api_manager import APIManager
from glossary import GlossaryManager
from post_processor import PostProcessor
from prompt_templates import PromptManager
from config_utils import safe_save_json, safe_load_json

PLACEHOLDER_RE = re.compile(r"(\[[^\]]+\]|\{[^{}]*\})")
NL_TOKEN = "<NL>"


def mask_text(text: str) -> Tuple[str, Dict[str, str]]:
    tokens: Dict[str, str] = {}

    def repl(match: re.Match[str]) -> str:
        key = f"<P{len(tokens)}>"
        tokens[key] = match.group(0)
        return key

    masked = PLACEHOLDER_RE.sub(repl, text)
    return masked, tokens


def unmask_text(text: str, tokens: Dict[str, str]) -> str:
    text = text.replace(NL_TOKEN, "\n")
    for key, value in tokens.items():
        text = text.replace(key, value)
    return text


def cache_path_for_game(cache_dir: Path, game_root: Path) -> Path:
    cache_dir.mkdir(parents=True, exist_ok=True)
    key = hashlib.sha1(str(game_root).encode("utf-8")).hexdigest()
    return cache_dir / f"cache_{key}.json"


def load_cache(path: Path) -> Dict[str, str]:
    """Load cache using safe loader with corruption handling."""
    return safe_load_json(path, {})


def save_cache(path: Path, cache: Dict[str, str]) -> None:
    """Save cache using atomic write pattern."""
    safe_save_json(path, cache)


class Translator:
    def __init__(
        self,
        game_root: Path,
        language: str,
        base_url: str,
        api_key: str,
        model: str,
        batch_size: int,
        max_chars: int,
        workers: int,
        cache_dir: Path,
        source_root: Optional[Path] = None,
        work_root: Optional[Path] = None,
        force_language: bool = False,
        translator_type: str = "openai",
        game_engine: str = "renpy",
        auto_detect: bool = False,
        api_configs: Optional[Dict] = None,
        fallback_apis: Optional[List[str]] = None,
        # New parameters for enhanced features
        glossary_path: Optional[Path] = None,
        use_glossary: bool = True,
        postprocess_path: Optional[Path] = None,
        use_postprocess: bool = True,
        use_cache: bool = True,
        system_prompt: Optional[str] = None,
        user_prompt: Optional[str] = None,
        api_keys: Optional[List[str]] = None,
        on_log=None,
        on_progress=None,
        stop_event=None,
    ) -> None:
        self.game_root = game_root
        self.language = language
        self.batch_size = batch_size
        self.max_chars = max_chars
        self.workers = max(1, int(workers))
        self.cache_dir = cache_dir
        self.source_root = source_root
        self.work_root = work_root
        self.force_language = force_language
        self.on_log = on_log
        self.on_progress = on_progress
        self.stop_event = stop_event
        self.use_cache = use_cache

        # Initialize plugin system
        self.plugin_manager = PluginManager()
        self.detector = EngineDetector()
        self.api_manager = APIManager()

        # Initialize glossary manager
        self.glossary = GlossaryManager()
        self.use_glossary = use_glossary
        if glossary_path and glossary_path.exists():
            self.glossary.load(glossary_path)
            self.log(f"Loaded glossary: {len(self.glossary)} entries")

        # Initialize post-processor
        self.post_processor = PostProcessor()
        self.post_processor.enabled = use_postprocess
        if postprocess_path and postprocess_path.exists():
            count = self.post_processor.load_from_file(postprocess_path)
            self.log(f"Loaded post-processor: {count} rules")

        # Initialize prompt manager
        self.prompt_manager = PromptManager()
        self.custom_system_prompt = system_prompt
        self.custom_user_prompt = user_prompt

        # Auto-detect engine if requested
        if auto_detect:
            detected_engine = self.detector.detect(game_root)
            if detected_engine:
                game_engine = detected_engine
                self.log(f"Auto-detected engine: {game_engine}")

        # Load engine plugin
        engines = self.plugin_manager.discover_engines()
        if game_engine not in engines:
            raise ValueError(f"Unknown game engine: {game_engine}")
        self.engine = engines[game_engine](on_log=on_log)

        # Load translator plugins
        translators = self.plugin_manager.discover_translators()

        # Setup primary translator
        if translator_type not in translators:
            raise ValueError(f"Unknown translator type: {translator_type}")

        # Use multiple API keys if provided
        tokens = api_keys if api_keys else [api_key]
        tokens = [t for t in tokens if t.strip()]  # Filter empty keys

        if api_configs and translator_type in api_configs:
            config = api_configs[translator_type]
            config_tokens = config.get('tokens', tokens)
            primary_translator = translators[translator_type](
                config.get('base_url', base_url),
                config_tokens[0] if config_tokens else api_key,
                config.get('model', model)
            )
            self.api_manager.add_api(translator_type, primary_translator, config_tokens)
        else:
            primary_translator = translators[translator_type](base_url, tokens[0] if tokens else api_key, model)
            self.api_manager.add_api(translator_type, primary_translator, tokens)

        # Set custom prompts if provided
        if hasattr(primary_translator, 'set_custom_prompts'):
            primary_translator.set_custom_prompts(system_prompt, user_prompt)

        # Initialize SQLite long-term cache
        if use_cache:
            primary_translator.init_long_cache(cache_dir, translator_type)

        # Setup fallback translators
        if fallback_apis and api_configs:
            for fallback_api in fallback_apis:
                if fallback_api in translators and fallback_api in api_configs:
                    config = api_configs[fallback_api]
                    fallback_tokens = config.get('tokens', [])
                    if fallback_tokens:
                        fallback_translator = translators[fallback_api](
                            config.get('base_url', ''),
                            fallback_tokens[0],
                            config.get('model', '')
                        )
                        self.api_manager.add_api(fallback_api, fallback_translator, fallback_tokens)

        self.api_manager.fallback_chain = [translator_type] + (fallback_apis or [])
        self.translator = primary_translator

    def log(self, msg: str) -> None:
        if self.on_log:
            self.on_log(msg)

    def progress(self, done: int, total: int, current: str = "") -> None:
        if self.on_progress:
            self.on_progress(done, total, current)

    def stopped(self) -> bool:
        return bool(self.stop_event and self.stop_event.is_set())

    def run(self) -> None:
        # Validate game root
        if not self.engine.validate_game_root(self.game_root):
            raise RuntimeError(f"Invalid game root for {self.engine.get_name()}: {self.game_root}")

        # Extract strings using game engine
        self.log(f"Extracting strings using {self.engine.get_name()} engine...")
        unique_strings = self.engine.extract_strings(self.game_root)

        # Load cache
        cache_path = cache_path_for_game(self.cache_dir, self.game_root)
        cache = load_cache(cache_path)

        # Filter pending strings
        pending = [s for s in unique_strings if s not in cache]
        total = len(unique_strings)
        done = len(cache)
        self.progress(done, total)

        if not pending:
            self.log("No pending strings. Writing translation file...")
            self.engine.write_translations(self.game_root, self.language, cache)
            self._apply_force_language()
            return

        # Build batches
        batches = self._build_batches(pending)

        def translate_with_fallback(
            b: List[str], m: List[Tuple[str, Dict[str, str]]]
        ) -> List[str]:
            try:
                self.log(f"Translating batch size: {len(b)}")
                translations = self.api_manager.translate_with_fallback(b, "auto", self.language)
                if len(translations) != len(m):
                    raise RuntimeError("Translation count mismatch.")
                return translations
            except Exception as exc:
                if len(b) <= 1:
                    self.log(f"Single item failed, using source text. Error: {exc}")
                    return [m[0][0]]
                self.log(f"Batch failed ({len(b)}). Retrying smaller batches... Error: {exc}")
                mid = len(b) // 2
                left = translate_with_fallback(b[:mid], m[:mid])
                right = translate_with_fallback(b[mid:], m[mid:])
                return left + right

        lock = Lock()

        def process_batch(batch: List[Tuple[str, str, Dict[str, str]]]) -> List[Tuple[str, str]]:
            if self.stopped():
                return []
            masked_list = [item[1] for item in batch]
            meta = [(item[0], item[2]) for item in batch]

            # Apply glossary pre-processing if enabled
            glossary_contexts = []
            if self.use_glossary and len(self.glossary) > 0:
                processed_list = []
                for text in masked_list:
                    processed, context = self.glossary.process_before(text)
                    processed_list.append(processed)
                    glossary_contexts.append(context)
                masked_list = processed_list

            translations = translate_with_fallback(masked_list, meta)
            result: List[Tuple[str, str]] = []
            for i, ((orig, tokens), translated) in enumerate(zip(meta, translations)):
                # Apply glossary post-processing
                if self.use_glossary and glossary_contexts:
                    translated = self.glossary.process_after(translated, glossary_contexts[i])

                # Apply post-processor
                if self.post_processor.enabled:
                    translated = self.post_processor.process(translated)

                translated = unmask_text(translated, tokens).strip()
                result.append((orig, translated))
            return result

        # Process batches
        if self.workers <= 1:
            for batch in batches:
                if self.stopped():
                    break
                result = process_batch(batch)
                for orig, translated in result:
                    cache[orig] = translated
                    done += 1
                    self.progress(done, total, orig)
                save_cache(cache_path, cache)
                time.sleep(0.4)
        else:
            with ThreadPoolExecutor(max_workers=self.workers) as executor:
                futures = []
                for batch in batches:
                    if self.stopped():
                        break
                    futures.append(executor.submit(process_batch, batch))
                for future in as_completed(futures):
                    result = future.result()
                    if not result:
                        continue
                    with lock:
                        for orig, translated in result:
                            cache[orig] = translated
                            done += 1
                            self.progress(done, total, orig)
                        save_cache(cache_path, cache)

        if self.stopped():
            self.log("Stopped by user. Cache saved.")
            return

        # Write translations using game engine
        self.engine.write_translations(self.game_root, self.language, cache)
        self._apply_force_language()

    def _build_batches(self, strings: List[str]) -> List[List[Tuple[str, str, Dict[str, str]]]]:
        batches: List[List[Tuple[str, str, Dict[str, str]]]] = []
        batch: List[Tuple[str, str, Dict[str, str]]] = []
        total_chars = 0
        for s in strings:
            masked, tokens = mask_text(s)
            candidate_len = len(masked)
            if batch and (len(batch) >= self.batch_size or total_chars + candidate_len > self.max_chars):
                batches.append(batch)
                batch = []
                total_chars = 0
            batch.append((s, masked, tokens))
            total_chars += candidate_len
        if batch:
            batches.append(batch)
        return batches

    def _apply_force_language(self) -> None:
        if self.force_language:
            force_path = self.game_root / "game" / "set_default_language_at_startup.rpy"
            force_path.write_text(
                f'init 1000 python:\n    renpy.game.preferences.language = "{self.language}"\n',
                encoding="utf-8",
            )
            self.log(f"Set default language: {self.language}")
