"""
Enhanced base translator with caching, retry logic, and rate limiting.

Inspired by LunaTranslator's basetranslator.py architecture.
Features:
- Dual-layer cache: short-term memory cache + long-term SQLite persistent cache
- Intelligent retry with exponential backoff
- Rate limiting per translator
"""

from abc import ABC, abstractmethod
from typing import List, Tuple, Dict, Optional, Generator, Any
from threading import Lock
from dataclasses import dataclass
import time
import hashlib
import json
import sqlite3
from pathlib import Path


@dataclass
class TranslationResult:
    """Result of a translation operation."""
    texts: List[str]
    success: bool
    error: Optional[str] = None
    from_cache: bool = False
    api_name: str = ""


class TranslatorBase(ABC):
    """Enhanced base class for all translators with caching and retry support."""

    # Class-level cache for all instances
    _cache_lock = Lock()
    _memory_cache: Dict[str, Dict[str, str]] = {}

    def __init__(self):
        self._last_request_time = 0.0
        self._request_interval = 0.1  # Minimum seconds between requests
        self._max_retries = 3
        self._retry_delay = 1.0
        self._cache_enabled = True
        self._cache_dir: Optional[Path] = None
        # SQLite long-term cache
        self._long_cache_db: Optional[sqlite3.Connection] = None
        self._long_cache_lock = Lock()

    @abstractmethod
    def translate(self, texts: List[str], source_lang: str, target_lang: str) -> List[str]:
        """Translate a list of texts from source language to target language."""
        pass

    @abstractmethod
    def get_supported_languages(self) -> Tuple[List[str], List[str]]:
        """Get supported source and target languages."""
        pass

    @abstractmethod
    def get_name(self) -> str:
        """Get translator name."""
        pass

    def translate_with_cache(
        self,
        texts: List[str],
        source_lang: str,
        target_lang: str
    ) -> TranslationResult:
        """
        Translate with caching support.

        Checks cache first, only translates uncached texts.
        """
        if not self._cache_enabled:
            try:
                result = self.translate(texts, source_lang, target_lang)
                return TranslationResult(texts=result, success=True, api_name=self.get_name())
            except Exception as e:
                return TranslationResult(texts=texts, success=False, error=str(e), api_name=self.get_name())

        cache_key = self._get_cache_key(source_lang, target_lang)
        results: List[str] = []
        uncached_indices: List[int] = []
        uncached_texts: List[str] = []

        # Check cache for each text
        for i, text in enumerate(texts):
            cached = self._get_from_cache(cache_key, text)
            if cached is not None:
                results.append(cached)
            else:
                results.append("")  # Placeholder
                uncached_indices.append(i)
                uncached_texts.append(text)

        # Translate uncached texts
        if uncached_texts:
            try:
                translated = self.translate(uncached_texts, source_lang, target_lang)
                for idx, trans in zip(uncached_indices, translated):
                    results[idx] = trans
                    self._set_cache(cache_key, texts[idx], trans)
                return TranslationResult(texts=results, success=True, api_name=self.get_name())
            except Exception as e:
                return TranslationResult(texts=texts, success=False, error=str(e), api_name=self.get_name())

        return TranslationResult(texts=results, success=True, from_cache=True, api_name=self.get_name())

    def translate_with_retry(
        self,
        texts: List[str],
        source_lang: str,
        target_lang: str,
        max_retries: Optional[int] = None
    ) -> List[str]:
        """
        Translate with automatic retry on failure.

        Uses exponential backoff between retries.
        """
        retries = max_retries if max_retries is not None else self._max_retries
        last_error = None

        for attempt in range(retries + 1):
            try:
                self._wait_for_rate_limit()
                return self.translate(texts, source_lang, target_lang)
            except Exception as e:
                last_error = e
                if attempt < retries:
                    delay = self._retry_delay * (2 ** attempt)
                    time.sleep(delay)

        raise last_error if last_error else RuntimeError("Translation failed")

    def translate_streaming(
        self,
        texts: List[str],
        source_lang: str,
        target_lang: str
    ) -> Generator[Tuple[int, str], None, None]:
        """
        Translate texts one by one, yielding results as they complete.

        Yields:
            Tuple of (index, translated_text)
        """
        for i, text in enumerate(texts):
            try:
                result = self.translate([text], source_lang, target_lang)
                yield i, result[0]
            except Exception as e:
                yield i, text  # Return original on failure

    def _wait_for_rate_limit(self):
        """Wait if necessary to respect rate limits."""
        elapsed = time.time() - self._last_request_time
        if elapsed < self._request_interval:
            time.sleep(self._request_interval - elapsed)
        self._last_request_time = time.time()

    def _get_cache_key(self, source_lang: str, target_lang: str) -> str:
        """Generate cache key for language pair."""
        return f"{self.get_name()}:{source_lang}:{target_lang}"

    def _get_from_cache(self, cache_key: str, text: str) -> Optional[str]:
        """Get translation from cache."""
        with self._cache_lock:
            if cache_key not in self._memory_cache:
                self._memory_cache[cache_key] = {}
            return self._memory_cache[cache_key].get(text)

    def _set_cache(self, cache_key: str, text: str, translation: str):
        """Set translation in cache."""
        with self._cache_lock:
            if cache_key not in self._memory_cache:
                self._memory_cache[cache_key] = {}
            self._memory_cache[cache_key][text] = translation

    def set_cache_dir(self, cache_dir: Path):
        """Set directory for persistent cache."""
        self._cache_dir = cache_dir
        cache_dir.mkdir(parents=True, exist_ok=True)

    def init_long_cache(self, cache_dir: Path, translator_name: str = None):
        """Initialize SQLite long-term cache (LunaTranslator style)."""
        cache_dir.mkdir(parents=True, exist_ok=True)
        name = translator_name or self.get_name()
        db_path = cache_dir / f"{name.lower()}_cache.sqlite"
        with self._long_cache_lock:
            self._long_cache_db = sqlite3.connect(str(db_path), check_same_thread=False)
            self._long_cache_db.execute("""
                CREATE TABLE IF NOT EXISTS cache(
                    srclang TEXT,
                    tgtlang TEXT,
                    source TEXT,
                    trans TEXT,
                    created_at REAL DEFAULT (strftime('%s', 'now')),
                    PRIMARY KEY(srclang, tgtlang, source)
                )
            """)
            self._long_cache_db.execute("""
                CREATE INDEX IF NOT EXISTS idx_cache_lookup
                ON cache(srclang, tgtlang, source)
            """)
            self._long_cache_db.commit()

    def get_from_long_cache(self, text: str, src_lang: str, tgt_lang: str) -> Optional[str]:
        """Get translation from SQLite long-term cache."""
        if not self._long_cache_db:
            return None
        with self._long_cache_lock:
            try:
                row = self._long_cache_db.execute(
                    "SELECT trans FROM cache WHERE srclang=? AND tgtlang=? AND source=?",
                    (src_lang, tgt_lang, text)
                ).fetchone()
                return row[0] if row else None
            except sqlite3.Error:
                return None

    def set_to_long_cache(self, text: str, translation: str, src_lang: str, tgt_lang: str):
        """Set translation to SQLite long-term cache."""
        if not self._long_cache_db:
            return
        with self._long_cache_lock:
            try:
                self._long_cache_db.execute(
                    "INSERT OR REPLACE INTO cache(srclang, tgtlang, source, trans) VALUES(?, ?, ?, ?)",
                    (src_lang, tgt_lang, text, translation)
                )
                self._long_cache_db.commit()
            except sqlite3.Error:
                pass

    def get_cached_dual(self, text: str, src_lang: str, tgt_lang: str) -> Optional[str]:
        """Get translation from dual-layer cache (memory first, then SQLite)."""
        cache_key = self._get_cache_key(src_lang, tgt_lang)
        # Check memory cache first
        with self._cache_lock:
            if cache_key in self._memory_cache:
                cached = self._memory_cache[cache_key].get(text)
                if cached is not None:
                    return cached
        # Check SQLite long-term cache
        long_cached = self.get_from_long_cache(text, src_lang, tgt_lang)
        if long_cached is not None:
            # Backfill to memory cache
            with self._cache_lock:
                if cache_key not in self._memory_cache:
                    self._memory_cache[cache_key] = {}
                self._memory_cache[cache_key][text] = long_cached
            return long_cached
        return None

    def set_cached_dual(self, text: str, translation: str, src_lang: str, tgt_lang: str):
        """Set translation to both memory and SQLite cache."""
        cache_key = self._get_cache_key(src_lang, tgt_lang)
        # Set memory cache
        with self._cache_lock:
            if cache_key not in self._memory_cache:
                self._memory_cache[cache_key] = {}
            self._memory_cache[cache_key][text] = translation
        # Set SQLite cache
        self.set_to_long_cache(text, translation, src_lang, tgt_lang)

    def close_long_cache(self):
        """Close SQLite connection."""
        if self._long_cache_db:
            with self._long_cache_lock:
                try:
                    self._long_cache_db.close()
                except sqlite3.Error:
                    pass
                self._long_cache_db = None

    def get_long_cache_stats(self) -> Dict[str, Any]:
        """Get statistics from SQLite cache."""
        if not self._long_cache_db:
            return {"entries": 0, "size_bytes": 0}
        with self._long_cache_lock:
            try:
                count = self._long_cache_db.execute("SELECT COUNT(*) FROM cache").fetchone()[0]
                return {"entries": count}
            except sqlite3.Error:
                return {"entries": 0}

    def clear_long_cache(self):
        """Clear all entries from SQLite cache."""
        if not self._long_cache_db:
            return
        with self._long_cache_lock:
            try:
                self._long_cache_db.execute("DELETE FROM cache")
                self._long_cache_db.commit()
            except sqlite3.Error:
                pass

    def export_long_cache(self, export_path: Path) -> int:
        """Export SQLite cache to JSON file. Returns number of entries exported."""
        if not self._long_cache_db:
            return 0
        with self._long_cache_lock:
            try:
                rows = self._long_cache_db.execute(
                    "SELECT srclang, tgtlang, source, trans FROM cache"
                ).fetchall()
                data = [
                    {"src_lang": r[0], "tgt_lang": r[1], "source": r[2], "translation": r[3]}
                    for r in rows
                ]
                with export_path.open('w', encoding='utf-8') as f:
                    json.dump(data, f, ensure_ascii=False, indent=2)
                return len(data)
            except (sqlite3.Error, IOError):
                return 0

    def save_cache_to_disk(self):
        """Save memory cache to disk."""
        if not self._cache_dir:
            return

        with self._cache_lock:
            for cache_key, cache_data in self._memory_cache.items():
                safe_key = hashlib.md5(cache_key.encode()).hexdigest()
                cache_file = self._cache_dir / f"{safe_key}.json"
                with cache_file.open('w', encoding='utf-8') as f:
                    json.dump({
                        'key': cache_key,
                        'data': cache_data
                    }, f, ensure_ascii=False, indent=2)

    def load_cache_from_disk(self):
        """Load cache from disk."""
        if not self._cache_dir or not self._cache_dir.exists():
            return

        with self._cache_lock:
            for cache_file in self._cache_dir.glob('*.json'):
                try:
                    with cache_file.open('r', encoding='utf-8') as f:
                        data = json.load(f)
                        cache_key = data.get('key', '')
                        cache_data = data.get('data', {})
                        if cache_key:
                            self._memory_cache[cache_key] = cache_data
                except (json.JSONDecodeError, KeyError):
                    continue

    def clear_cache(self):
        """Clear all cached translations."""
        with self._cache_lock:
            self._memory_cache.clear()

    @property
    def cache_stats(self) -> Dict[str, int]:
        """Get cache statistics."""
        with self._cache_lock:
            total_entries = sum(len(v) for v in self._memory_cache.values())
            return {
                'language_pairs': len(self._memory_cache),
                'total_entries': total_entries,
            }

    def configure(
        self,
        cache_enabled: bool = True,
        request_interval: float = 0.1,
        max_retries: int = 3,
        retry_delay: float = 1.0
    ):
        """Configure translator behavior."""
        self._cache_enabled = cache_enabled
        self._request_interval = request_interval
        self._max_retries = max_retries
        self._retry_delay = retry_delay
