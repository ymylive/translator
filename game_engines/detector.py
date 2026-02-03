from pathlib import Path
from typing import Dict, List, Optional, Tuple


class EngineDetector:
    SIGNATURES = {
        'renpy': {
            'files': ['game/script.rpy', 'renpy/', 'game/', 'lib/'],
            'priority': 1
        },
        'rpgmaker_mv': {
            'files': ['www/data/System.json', 'package.json', 'www/js/rpg_core.js'],
            'priority': 2
        },
        'rpgmaker_mz': {
            'files': ['www/js/rmmz_core.js', 'www/data/System.json'],
            'priority': 2
        },
        'unity': {
            'files': ['UnityPlayer.dll', 'globalgamemanagers', 'Data/'],
            'priority': 3
        }
    }

    def detect(self, game_path: Path) -> Optional[str]:
        """Detect game engine from game directory"""
        if not game_path.exists():
            return None

        scores = {}
        for engine, sig in self.SIGNATURES.items():
            score = self.get_confidence_score(game_path, engine)
            if score > 0:
                scores[engine] = score

        if not scores:
            return None

        return max(scores.items(), key=lambda x: (x[1], -self.SIGNATURES[x[0]]['priority']))[0]

    def detect_from_exe(self, exe_path: Path) -> Optional[str]:
        """Detect game engine from executable path"""
        if not exe_path.exists():
            return None
        return self.detect(exe_path.parent)

    def get_confidence_score(self, game_path: Path, engine: str) -> float:
        """Calculate confidence score for an engine"""
        if engine not in self.SIGNATURES:
            return 0.0

        sig = self.SIGNATURES[engine]
        matches = 0
        total = len(sig['files'])

        for file_pattern in sig['files']:
            check_path = game_path / file_pattern
            if check_path.exists():
                matches += 1

        return matches / total if total > 0 else 0.0
