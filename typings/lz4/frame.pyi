from typing import Final

# Compression levels
COMPRESSIONLEVEL_MIN: Final[int]
COMPRESSIONLEVEL_MINHC: Final[int]
COMPRESSIONLEVEL_MAX: Final[int]

# Frame constants
BLOCKSIZE_DEFAULT: Final[int]
BLOCKSIZE_MAX64KB: Final[int]
BLOCKSIZE_MAX256KB: Final[int]
BLOCKSIZE_MAX1MB: Final[int]
BLOCKSIZE_MAX4MB: Final[int]

# Main compression/decompression functions
def compress(
    data: bytes,
    compression_level: int = 0,
    block_size: int = ...,
    block_linked: bool = True,
    content_checksum: bool = False,
    block_checksum: bool = False,
    auto_flush: bool = False,
    store_size: bool = True,
) -> bytes: ...

def decompress(
    data: bytes,
    uncompressed_size: int = -1,
) -> bytes: ...

# Context managers for streaming
class LZ4FrameCompressor:
    def __init__(
        self,
        compression_level: int = 0,
        block_size: int = ...,
        block_linked: bool = True,
        content_checksum: bool = False,
        block_checksum: bool = False,
        auto_flush: bool = False,
    ) -> None: ...
    def begin(self, source_size: int = 0) -> bytes: ...
    def compress(self, data: bytes) -> bytes: ...
    def flush(self) -> bytes: ...
    def reset(self) -> None: ...

class LZ4FrameDecompressor:
    def __init__(self) -> None: ...
    def decompress(self, data: bytes, max_length: int = -1) -> bytes: ...
    def reset(self) -> None: ...

__all__ = [
    "BLOCKSIZE_DEFAULT",
    "BLOCKSIZE_MAX1MB",
    "BLOCKSIZE_MAX4MB",
    "BLOCKSIZE_MAX64KB",
    "BLOCKSIZE_MAX256KB",
    "COMPRESSIONLEVEL_MAX",
    "COMPRESSIONLEVEL_MIN",
    "COMPRESSIONLEVEL_MINHC",
    "LZ4FrameCompressor",
    "LZ4FrameDecompressor",
    "compress",
    "decompress",
]
