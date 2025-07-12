
from aenum import StrEnum

class CacheBackend(StrEnum):
    MEMORY: CacheBackend
    REDIS: CacheBackend
    CACHEBOX: CacheBackend
    DISK: CacheBackend
    @property
    def description(self) -> str: ...
    @property
    def is_default(self) -> bool: ...

class SerializationFormat(StrEnum):
    PICKLE: SerializationFormat
    JSON: SerializationFormat
    @property
    def description(self) -> str: ...
    @property
    def is_default(self) -> bool: ...

class EvictionPolicy(StrEnum):
    LRU: EvictionPolicy
    LFU: EvictionPolicy
    FIFO: EvictionPolicy
    @property
    def description(self) -> str: ...
    @property
    def is_default(self) -> bool: ...

__all__ = [
    "CacheBackend",
    "EvictionPolicy",
    "SerializationFormat",
]
