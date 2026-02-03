from typing import Dict, List, Any


class ConfigSchema:
    TRANSLATOR_SCHEMAS = {
        'openai': {
            'required': ['api_key', 'base_url', 'model'],
            'optional': ['temperature', 'max_tokens']
        },
        'claude': {
            'required': ['api_key', 'model'],
            'optional': ['temperature', 'max_tokens']
        },
        'deepl': {
            'required': ['api_key'],
            'optional': ['formality']
        },
        'google': {
            'required': ['api_key'],
            'optional': ['project_id']
        }
    }

    @staticmethod
    def get_translator_schema(translator_name: str) -> Dict[str, List[str]]:
        """Get configuration schema for a translator"""
        return ConfigSchema.TRANSLATOR_SCHEMAS.get(translator_name, {'required': [], 'optional': []})

    @staticmethod
    def validate_config(translator_name: str, config: Dict[str, Any]) -> bool:
        """Validate translator configuration"""
        schema = ConfigSchema.get_translator_schema(translator_name)
        for field in schema['required']:
            if field not in config or not config[field]:
                return False
        return True
