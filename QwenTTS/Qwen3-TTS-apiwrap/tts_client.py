# coding: utf-8
"""
Gradio Client wrapper for Qwen3-TTS server.
Connects to running Gradio TTS demo and provides TTS generation.
"""

import base64
import io
import base64
import io
import logging
import re
from typing import Optional, List

import numpy as np
from gradio_client import Client
from scipy.io import wavfile

logger = logging.getLogger(__name__)


class TTSClient:
    """Client for Qwen3-TTS Gradio server."""

    DEFAULT_SERVER = "http://localhost:23015"
    DEFAULT_SPEAKER = "Sohee"
    DEFAULT_LANGUAGE = "Korean"
    SAMPLE_RATE = 24000
    MAX_CHUNK_LENGTH = 60  # Maximum characters per chunk

    def __init__(self, server_url: str = DEFAULT_SERVER, verbose: bool = False):
        """
        Initialize TTS client.

        Args:
            server_url: URL of the Gradio TTS server
            verbose: Enable verbose logging
        """
        self.server_url = server_url
        self.verbose = verbose
        self._client: Optional[Client] = None

        if verbose:
            logging.getLogger().setLevel(logging.DEBUG)

    def _get_client(self) -> Client:
        """Get or create Gradio client connection."""
        if self._client is None:
            if self.verbose:
                logger.info(f"Connecting to TTS server: {self.server_url}")
            self._client = Client(self.server_url)
        return self._client

    def _split_text(self, text: str) -> List[str]:
        """
        Split text into chunks of maximum size MAX_CHUNK_LENGTH.
        Priority: Sentence endings (.!?) > Clauses (,;:) > Newlines > Spaces > Hard limit
        """
        if len(text) <= self.MAX_CHUNK_LENGTH:
            return [text]

        chunks = []
        current_text = text

        while len(current_text) > self.MAX_CHUNK_LENGTH:
            search_window = current_text[:self.MAX_CHUNK_LENGTH]
            
            # 1. Try splitting by sentence endings (.!?)
            # Look for punctuation followed by space or end of string
            match = re.search(r'([.!?])(?=\s|$)', search_window[::-1])
            split_idx = -1
            
            if match:
                # Found sentence ending (index from end of reversed string)
                # match.start() is index in REVERSED string
                # len - 1 - start is index in ORIGINAL string
                split_idx = len(search_window) - 1 - match.start() + 1
            
            if split_idx == -1:
                # 2. Try splitting by clause separators (,;:)
                match = re.search(r'([,;:])(?=\s|$)', search_window[::-1])
                if match:
                    split_idx = len(search_window) - 1 - match.start() + 1

            if split_idx == -1:
                # 3. Try splitting by newlines
                match = re.search(r'\n', search_window[::-1])
                if match:
                     split_idx = len(search_window) - 1 - match.start() + 1

            if split_idx == -1:
                # 4. Try splitting by spaces
                match = re.search(r'\s', search_window[::-1])
                if match:
                    split_idx = len(search_window) - 1 - match.start() + 1

            if split_idx == -1:
                # 5. No suitable separator found, force split at limit
                split_idx = self.MAX_CHUNK_LENGTH

            # Add chunk and advance
            chunks.append(current_text[:split_idx].strip())
            current_text = current_text[split_idx:].strip()
            
            # Handle case where split consumes everything but leaves empty string or just whitespace
            if not current_text:
                break

        if current_text:
            chunks.append(current_text)

        # Filter out empty chunks
        chunks = [c for c in chunks if c]
        
        if self.verbose:
            logger.debug(f"Split text into {len(chunks)} chunks: {chunks}")
            
        return chunks

    def _generate_raw(
        self,
        text: str,
        language: Optional[str] = None,
        speaker: Optional[str] = None,
        instruct: Optional[str] = None,
    ) -> tuple[int, Optional[np.ndarray]]:
        """
        Internal function to call Gradio API for a single chunk.
        Returns: (sample_rate, numpy_audio_array)
        """
        language = language or self.DEFAULT_LANGUAGE
        speaker = speaker or self.DEFAULT_SPEAKER
        instruct = instruct or ""

        if self.verbose:
            logger.debug(f"TTS chunk request: text='{text[:20]}...', lang={language}, speaker={speaker}")

        client = self._get_client()

        # Call Gradio API - run_instruct function
        result = client.predict(
            text,           # text
            language,       # language dropdown
            speaker,        # speaker dropdown
            instruct,       # instruct textbox
            api_name="/run_instruct"
        )

        if self.verbose:
            logger.debug(f"TTS chunk response received. Type: {type(result)}")

        if not isinstance(result, (list, tuple)) or len(result) < 2:
            raise ValueError(f"Unexpected response format from TTS server: {result}")

        audio_data, status = result[0], result[1]

        if audio_data is None:
            raise ValueError(f"TTS generation failed: {status}")
        
        sample_rate = self.SAMPLE_RATE
        audio_array = None

        if isinstance(audio_data, (list, tuple)) and len(audio_data) == 2:
            sample_rate, audio_array = audio_data
        elif isinstance(audio_data, str):
            if self.verbose:
                logger.debug(f"Loading audio from file: {audio_data}")
            sample_rate, audio_array = wavfile.read(audio_data)
        elif isinstance(audio_data, dict) and 'path' in audio_data:
            path = audio_data['path']
            if self.verbose:
                logger.debug(f"Loading audio from dict path: {path}")
            sample_rate, audio_array = wavfile.read(path)
        else:
            raise ValueError(f"Unsupported audio data format: {type(audio_data)}")
            
        return sample_rate, audio_array

    def generate(
        self,
        text: str,
        language: Optional[str] = None,
        speaker: Optional[str] = None,
        instruct: Optional[str] = None,
    ) -> tuple[bytes, int]:
        """
        Generate speech from text. Handles long text by splitting.

        Args:
            text: Text to synthesize
            language: Language code (default: Korean)
            speaker: Speaker name (default: Sohee)
            instruct: Optional instruction for voice style

        Returns:
            Tuple of (wav_bytes, sample_rate)
        """
        # Split text into manageable chunks
        chunks = self._split_text(text)
        
        if not chunks:
            # Handle empty request appropriately
            raise ValueError("Input text is empty or resulted in empty chunks")

        collected_audio = []
        final_sr = self.SAMPLE_RATE

        for i, chunk in enumerate(chunks):
            try:
                sr, audio = self._generate_raw(chunk, language, speaker, instruct)
                
                # Use SR from first chunk as reference
                if i == 0:
                    final_sr = sr
                elif sr != final_sr:
                    logger.warning(f"Sample rate mismatch in chunk {i}: got {sr}, expected {final_sr}")
                    # In a robust system, we might resample here
                
                if audio is not None and audio.size > 0:
                    collected_audio.append(audio)
                    
            except Exception as e:
                logger.error(f"Failed to generate chunk {i} ('{chunk[:10]}...'): {e}")
                # We continue to try other chunks, or should we fail?
                # For now, let's fail to maintain integrity
                raise e

        if not collected_audio:
            raise ValueError("No audio generated from chunks")

        # Concatenate all audio arrays
        if len(collected_audio) == 1:
            full_audio = collected_audio[0]
        else:
            full_audio = np.concatenate(collected_audio)
            
        if self.verbose:
            logger.debug(f"Merged {len(collected_audio)} chunks. Total samples: {full_audio.shape}")

        # Convert to WAV
        wav_bytes = self._numpy_to_wav(full_audio, final_sr)
        
        return wav_bytes, final_sr

    def generate_base64(
        self,
        text: str,
        language: Optional[str] = None,
        speaker: Optional[str] = None,
        instruct: Optional[str] = None,
    ) -> tuple[str, int]:
        """
        Generate speech and return as base64 encoded WAV.

        Args:
            text: Text to synthesize
            language: Language code
            speaker: Speaker name
            instruct: Optional instruction

        Returns:
            Tuple of (base64_wav_string, sample_rate)
        """
        wav_bytes, sample_rate = self.generate(text, language, speaker, instruct)
        base64_wav = base64.b64encode(wav_bytes).decode("utf-8")
        return base64_wav, sample_rate

    def _numpy_to_wav(self, audio_array: np.ndarray, sample_rate: int) -> bytes:
        """
        Convert numpy audio array to WAV bytes with robust normalization.
        Handles various dtypes (int16, float32, etc.) and ensures mono output.
        """
        x = np.asarray(audio_array)
        
        if self.verbose:
            logger.debug(f"Processing audio: shape={x.shape}, dtype={x.dtype}, sr={sample_rate}")

        # 1. Normalize to float32 in range [-1.0, 1.0]
        if np.issubdtype(x.dtype, np.integer):
            info = np.iinfo(x.dtype)
            if info.min < 0:
                # Signed integer (e.g. int16)
                y = x.astype(np.float32) / max(abs(info.min), info.max)
            else:
                # Unsigned integer (e.g. uint8)
                mid = (info.max + 1) / 2.0
                y = (x.astype(np.float32) - mid) / mid
        elif np.issubdtype(x.dtype, np.floating):
            # Floating point
            y = x.astype(np.float32)
            max_val = np.max(np.abs(y)) if y.size else 0.0
            if max_val > 1.0 + 1e-6:
                y = y / (max_val + 1e-12)
        else:
            raise TypeError(f"Unsupported audio dtype: {x.dtype}")

        # 2. Ensure Mono (collapse channels if multi-channel)
        if y.ndim > 1:
            if self.verbose:
                logger.debug(f"Converting multi-channel ({y.shape}) to mono")
            y = np.mean(y, axis=-1 if y.shape[-1] < y.shape[0] else 0)

        # 3. Final clip to protect against rounding errors
        y = np.clip(y, -1.0, 1.0)

        # 4. Convert to 16-bit PCM integer
        audio_int16 = (y * 32767).astype(np.int16)

        # 5. Write to bytes buffer as standard WAV
        buffer = io.BytesIO()
        wavfile.write(buffer, sample_rate, audio_int16)
        return buffer.getvalue()

    def close(self):
        """Close the client connection."""
        self._client = None


# Module-level client instance for convenience
_default_client: Optional[TTSClient] = None


def get_client(server_url: str = TTSClient.DEFAULT_SERVER, verbose: bool = False) -> TTSClient:
    """Get or create default TTS client."""
    global _default_client
    if _default_client is None:
        _default_client = TTSClient(server_url, verbose)
    return _default_client
