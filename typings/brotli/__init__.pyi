from typing import Final

# Constants for compression modes
MODE_GENERIC: Final[int]
MODE_TEXT: Final[int]
MODE_FONT: Final[int]

# Main compression/decompression functions
def compress(
    string: bytes,
    mode: int = ...,
    quality: int = 11,
    lgwin: int = 22,
    lgblock: int = 0,
) -> bytes: ...

def decompress(string: bytes) -> bytes: ...

# Version information
__version__: str

__all__ = ["MODE_FONT", "MODE_GENERIC", "MODE_TEXT", "compress", "decompress"]
