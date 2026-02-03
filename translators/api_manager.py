"""
Enhanced API Manager with intelligent failover, load balancing, and rate limiting.

Inspired by LunaTranslator's multi-API architecture.
"""

from typing import Dict, List, Optional, Callable, Any
from dataclasses import dataclass, field
from threading import Lock
from collections import deque
import time
import random

from translators.base import TranslatorBase, TranslationResult


@dataclass
class APIStats:
    """Statistics for an API endpoint."""
    total_requests: int = 0
    successful_requests: int = 0
    failed_requests: int = 0
    total_latency: float = 0.0
    last_error: Optional[str] = None
    last_error_time: float = 0.0
    consecutive_failures: int = 0

    @property
    def success_rate(self) -> float:
        if self.total_requests == 0:
            return 1.0
        return self.successful_requests / self.total_requests

    @property
    def average_latency(self) -> float:
        if self.successful_requests == 0:
            return 0.0
        return self.total_latency / self.successful_requests

    def record_success(self, latency: float):
        self.total_requests += 1
        self.successful_requests += 1
        self.total_latency += latency
        self.consecutive_failures = 0

    def record_failure(self, error: str):
        self.total_requests += 1
        self.failed_requests += 1
        self.last_error = error
        self.last_error_time = time.time()
        self.consecutive_failures += 1


@dataclass
class TokenPool:
    """Pool of API tokens with rotation support and intelligent cooldown."""
    tokens: List[str] = field(default_factory=list)
    current_index: int = 0
    usage_counts: Dict[str, int] = field(default_factory=dict)
    error_counts: Dict[str, int] = field(default_factory=dict)
    cooldown_until: Dict[str, float] = field(default_factory=dict)
    skip_rounds: Dict[int, int] = field(default_factory=dict)  # LunaTranslator style

    def get_next_token(self) -> Optional[str]:
        """Get next available token, skipping those in cooldown or skip rounds."""
        if not self.tokens:
            return None

        now = time.time()
        attempts = 0
        max_attempts = len(self.tokens) * 2

        while attempts < max_attempts:
            self.current_index = (self.current_index + 1) % len(self.tokens)
            idx = self.current_index
            token = self.tokens[idx]

            # Check skip rounds (LunaTranslator style)
            if self.skip_rounds.get(idx, 0) > 0:
                self.skip_rounds[idx] -= 1
                attempts += 1
                continue

            # Check cooldown
            if self.cooldown_until.get(token, 0) > now:
                attempts += 1
                continue

            return token

        # All tokens in cooldown/skip, return the one with shortest remaining cooldown
        available = [
            t for t in self.tokens
            if self.cooldown_until.get(t, 0) <= now
        ]
        if available:
            return available[0]

        return min(self.tokens, key=lambda t: self.cooldown_until.get(t, 0))

    def record_usage(self, token: str):
        """Record token usage."""
        self.usage_counts[token] = self.usage_counts.get(token, 0) + 1

    def record_error(self, token: str, cooldown_seconds: float = 60.0):
        """Record token error and set cooldown with progressive skip rounds."""
        idx = self.tokens.index(token) if token in self.tokens else -1
        self.error_counts[token] = self.error_counts.get(token, 0) + 1
        self.cooldown_until[token] = time.time() + cooldown_seconds

        # LunaTranslator style: increase skip rounds based on error count
        if idx >= 0:
            error_count = self.error_counts[token]
            self.skip_rounds[idx] = self.skip_rounds.get(idx, 0) + error_count

    def record_success(self, token: str):
        """Record successful usage, reset error count."""
        if token in self.error_counts:
            self.error_counts[token] = max(0, self.error_counts[token] - 1)
        idx = self.tokens.index(token) if token in self.tokens else -1
        if idx >= 0 and idx in self.skip_rounds:
            self.skip_rounds[idx] = max(0, self.skip_rounds[idx] - 1)

    def reset_cooldown(self, token: str):
        """Reset cooldown for a token."""
        if token in self.cooldown_until:
            del self.cooldown_until[token]
        if token in self.error_counts:
            self.error_counts[token] = 0
        idx = self.tokens.index(token) if token in self.tokens else -1
        if idx >= 0 and idx in self.skip_rounds:
            self.skip_rounds[idx] = 0

    def reset_all(self):
        """Reset all cooldowns and error counts."""
        self.cooldown_until.clear()
        self.error_counts.clear()
        self.skip_rounds.clear()
        self.current_index = -1

    def get_stats(self) -> Dict[str, Any]:
        """Get token pool statistics."""
        now = time.time()
        return {
            "total_tokens": len(self.tokens),
            "available_tokens": sum(
                1 for t in self.tokens
                if self.cooldown_until.get(t, 0) <= now
            ),
            "usage_counts": dict(self.usage_counts),
            "error_counts": dict(self.error_counts),
        }


class APIManager:
    """
    Enhanced API Manager with intelligent failover and load balancing.

    Features:
    - Multiple API support with automatic failover
    - Token pool rotation per API
    - Rate limiting per API
    - Statistics tracking
    - Adaptive retry with exponential backoff
    """

    def __init__(self):
        self.apis: Dict[str, TranslatorBase] = {}
        self.fallback_chain: List[str] = []
        self.token_pools: Dict[str, TokenPool] = {}
        self.stats: Dict[str, APIStats] = {}
        self.rate_limits: Dict[str, float] = {}  # requests per second
        self.last_request_time: Dict[str, float] = {}
        self._lock = Lock()

        # Configuration
        self.max_consecutive_failures = 5
        self.failure_cooldown = 300.0  # 5 minutes
        self.default_rate_limit = 1.0  # 1 request per second

    def add_api(
        self,
        name: str,
        translator: TranslatorBase,
        tokens: List[str],
        rate_limit: Optional[float] = None
    ):
        """Add a translator API with token pool."""
        with self._lock:
            self.apis[name] = translator
            self.token_pools[name] = TokenPool(tokens=tokens)
            self.stats[name] = APIStats()
            self.rate_limits[name] = rate_limit or self.default_rate_limit
            self.last_request_time[name] = 0.0

    def remove_api(self, name: str):
        """Remove an API."""
        with self._lock:
            self.apis.pop(name, None)
            self.token_pools.pop(name, None)
            self.stats.pop(name, None)
            self.rate_limits.pop(name, None)
            self.last_request_time.pop(name, None)
            if name in self.fallback_chain:
                self.fallback_chain.remove(name)

    def translate_with_fallback(
        self,
        texts: List[str],
        source_lang: str,
        target_lang: str,
        on_api_switch: Optional[Callable[[str, str], None]] = None
    ) -> List[str]:
        """
        Translate with automatic fallback on failure.

        Args:
            texts: Texts to translate
            source_lang: Source language
            target_lang: Target language
            on_api_switch: Callback when switching APIs (old_api, new_api)

        Returns:
            List of translated texts
        """
        last_error = None
        previous_api = None

        for api_name in self._get_available_apis():
            if api_name not in self.apis:
                continue

            # Check if API is in cooldown due to consecutive failures
            stats = self.stats.get(api_name)
            if stats and stats.consecutive_failures >= self.max_consecutive_failures:
                if time.time() - stats.last_error_time < self.failure_cooldown:
                    continue

            # Notify API switch
            if previous_api and on_api_switch:
                on_api_switch(previous_api, api_name)
            previous_api = api_name

            try:
                # Wait for rate limit
                self._wait_for_rate_limit(api_name)

                # Get translator and translate
                translator = self.apis[api_name]
                start_time = time.time()
                result = translator.translate(texts, source_lang, target_lang)
                latency = time.time() - start_time

                # Record success
                with self._lock:
                    self.stats[api_name].record_success(latency)

                return result

            except Exception as e:
                last_error = e
                error_msg = str(e)

                # Record failure
                with self._lock:
                    self.stats[api_name].record_failure(error_msg)
                    # Rotate token on failure
                    pool = self.token_pools.get(api_name)
                    if pool:
                        current_token = pool.get_next_token()
                        if current_token:
                            pool.record_error(current_token)

                continue

        if last_error:
            raise last_error
        raise RuntimeError("No translators available")

    def translate_batch_parallel(
        self,
        batches: List[List[str]],
        source_lang: str,
        target_lang: str
    ) -> List[List[str]]:
        """
        Translate multiple batches, distributing across available APIs.

        Uses round-robin distribution for load balancing.
        """
        results: List[List[str]] = [[] for _ in batches]
        available_apis = self._get_available_apis()

        if not available_apis:
            raise RuntimeError("No APIs available")

        for i, batch in enumerate(batches):
            api_name = available_apis[i % len(available_apis)]
            try:
                result = self.translate_with_fallback(batch, source_lang, target_lang)
                results[i] = result
            except Exception:
                results[i] = batch  # Return original on failure

        return results

    def get_next_token(self, api_name: str) -> Optional[str]:
        """Get next token from pool for an API."""
        with self._lock:
            pool = self.token_pools.get(api_name)
            if pool:
                return pool.get_next_token()
            return None

    def rotate_token(self, api_name: str):
        """Rotate to next token in pool."""
        with self._lock:
            pool = self.token_pools.get(api_name)
            if pool:
                pool.current_index += 1

    def get_stats(self, api_name: Optional[str] = None) -> Dict[str, APIStats]:
        """Get statistics for APIs."""
        with self._lock:
            if api_name:
                return {api_name: self.stats.get(api_name, APIStats())}
            return dict(self.stats)

    def get_best_api(self) -> Optional[str]:
        """Get the best performing API based on success rate and latency."""
        with self._lock:
            available = self._get_available_apis()
            if not available:
                return None

            def score(api_name: str) -> float:
                stats = self.stats.get(api_name, APIStats())
                # Higher success rate and lower latency = better score
                success_score = stats.success_rate * 100
                latency_penalty = min(stats.average_latency * 10, 50)
                return success_score - latency_penalty

            return max(available, key=score)

    def reset_stats(self, api_name: Optional[str] = None):
        """Reset statistics for APIs."""
        with self._lock:
            if api_name:
                self.stats[api_name] = APIStats()
            else:
                for name in self.stats:
                    self.stats[name] = APIStats()

    def _get_available_apis(self) -> List[str]:
        """Get list of available APIs in fallback order."""
        if self.fallback_chain:
            return [api for api in self.fallback_chain if api in self.apis]
        return list(self.apis.keys())

    def _wait_for_rate_limit(self, api_name: str):
        """Wait if necessary to respect rate limits."""
        with self._lock:
            rate_limit = self.rate_limits.get(api_name, self.default_rate_limit)
            last_time = self.last_request_time.get(api_name, 0)

        min_interval = 1.0 / rate_limit
        elapsed = time.time() - last_time

        if elapsed < min_interval:
            time.sleep(min_interval - elapsed)

        with self._lock:
            self.last_request_time[api_name] = time.time()

    def set_rate_limit(self, api_name: str, requests_per_second: float):
        """Set rate limit for an API."""
        with self._lock:
            self.rate_limits[api_name] = requests_per_second

    def set_fallback_chain(self, chain: List[str]):
        """Set the fallback chain order."""
        self.fallback_chain = chain

    @property
    def available_apis(self) -> List[str]:
        """Get list of available API names."""
        return list(self.apis.keys())

    def __repr__(self) -> str:
        return f"APIManager(apis={list(self.apis.keys())}, fallback_chain={self.fallback_chain})"
