"""
Safe configuration utilities with atomic writes.

Inspired by LunaTranslator's config.py - prevents config corruption
from unexpected interruptions.
"""

import os
import json
import time
from pathlib import Path
from typing import Dict, Any, Optional
from threading import Lock


_save_lock = Lock()


def safe_save_json(path: Path, data: Dict[str, Any], indent: int = 2) -> bool:
    """
    Safely save JSON data using atomic write pattern.

    Writes to a temporary file first, then atomically replaces the target.
    This prevents data corruption if the process is interrupted.

    Args:
        path: Target file path
        data: Dictionary to save
        indent: JSON indentation level

    Returns:
        True if successful, False otherwise
    """
    tmp_path = path.with_suffix(path.suffix + ".tmp")

    with _save_lock:
        try:
            # Ensure parent directory exists
            path.parent.mkdir(parents=True, exist_ok=True)

            # Write to temporary file
            with tmp_path.open("w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=indent)

            # Atomic replace
            os.replace(tmp_path, path)
            return True

        except Exception as e:
            # Clean up temp file on failure
            if tmp_path.exists():
                try:
                    tmp_path.unlink()
                except OSError:
                    pass
            raise e


def safe_load_json(path: Path, default: Optional[Dict] = None) -> Dict[str, Any]:
    """
    Safely load JSON data with corruption handling.

    If the file is corrupted, backs it up and returns default.

    Args:
        path: File path to load
        default: Default value if file doesn't exist or is corrupted

    Returns:
        Loaded dictionary or default value
    """
    if not path.exists():
        return default or {}

    try:
        with path.open("r", encoding="utf-8") as f:
            return json.load(f)

    except json.JSONDecodeError:
        # Backup corrupted file
        bad_path = path.with_suffix(f"{path.suffix}.bad-{int(time.time())}")
        try:
            os.replace(path, bad_path)
        except OSError:
            pass
        return default or {}

    except Exception:
        return default or {}


def sync_config(current: Dict[str, Any], defaults: Dict[str, Any]) -> Dict[str, Any]:
    """
    Synchronize configuration with defaults.

    Adds missing keys from defaults while preserving existing values.
    LunaTranslator style config synchronization.

    Args:
        current: Current configuration
        defaults: Default configuration

    Returns:
        Merged configuration
    """
    result = dict(defaults)

    for key, value in current.items():
        if key in result:
            if isinstance(value, dict) and isinstance(result[key], dict):
                # Recursively merge nested dicts
                result[key] = sync_config(value, result[key])
            else:
                result[key] = value
        else:
            result[key] = value

    return result


def backup_config(path: Path, max_backups: int = 5) -> Optional[Path]:
    """
    Create a timestamped backup of configuration file.

    Args:
        path: Config file path
        max_backups: Maximum number of backups to keep

    Returns:
        Backup file path or None if failed
    """
    if not path.exists():
        return None

    try:
        timestamp = int(time.time())
        backup_path = path.with_suffix(f"{path.suffix}.backup-{timestamp}")

        # Copy content
        with path.open("r", encoding="utf-8") as src:
            content = src.read()
        with backup_path.open("w", encoding="utf-8") as dst:
            dst.write(content)

        # Clean old backups
        backup_pattern = f"{path.stem}*.backup-*"
        backups = sorted(
            path.parent.glob(backup_pattern),
            key=lambda p: p.stat().st_mtime,
            reverse=True
        )

        for old_backup in backups[max_backups:]:
            try:
                old_backup.unlink()
            except OSError:
                pass

        return backup_path

    except Exception:
        return None


class ConfigManager:
    """
    Configuration manager with auto-save and validation.

    Features:
    - Atomic saves
    - Auto-backup
    - Default value synchronization
    - Change tracking
    """

    def __init__(self, path: Path, defaults: Optional[Dict] = None):
        self.path = path
        self.defaults = defaults or {}
        self._data: Dict[str, Any] = {}
        self._dirty = False
        self._lock = Lock()

    def load(self) -> Dict[str, Any]:
        """Load configuration from file."""
        with self._lock:
            self._data = safe_load_json(self.path, self.defaults.copy())
            if self.defaults:
                self._data = sync_config(self._data, self.defaults)
            self._dirty = False
            return self._data.copy()

    def save(self, backup: bool = False) -> bool:
        """Save configuration to file."""
        with self._lock:
            if backup:
                backup_config(self.path)
            result = safe_save_json(self.path, self._data)
            if result:
                self._dirty = False
            return result

    def get(self, key: str, default: Any = None) -> Any:
        """Get configuration value."""
        with self._lock:
            return self._data.get(key, default)

    def set(self, key: str, value: Any) -> None:
        """Set configuration value."""
        with self._lock:
            if self._data.get(key) != value:
                self._data[key] = value
                self._dirty = True

    def update(self, data: Dict[str, Any]) -> None:
        """Update multiple configuration values."""
        with self._lock:
            for key, value in data.items():
                if self._data.get(key) != value:
                    self._data[key] = value
                    self._dirty = True

    @property
    def is_dirty(self) -> bool:
        """Check if configuration has unsaved changes."""
        return self._dirty

    @property
    def data(self) -> Dict[str, Any]:
        """Get copy of configuration data."""
        with self._lock:
            return self._data.copy()
