import torch
import time
from qwen_tts import Qwen3TTSModel

def benchmark():
    ckpt = "Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice"
    device = "cuda:0"
    dtype = torch.bfloat16
    
    print(f"Loading model on {device}...")
    tts = Qwen3TTSModel.from_pretrained(
        ckpt,
        device_map=device,
        dtype=dtype,
    )
    
    model = tts.model
    print(f"Model attention implementation: {model.config._attn_implementation}")
    
    # Check if talker and predictor use SDPA
    if hasattr(model, "talker"):
        print(f"Talker attn: {model.talker.config._attn_implementation}")
    if hasattr(model.talker, "code_predictor"):
        print(f"Predictor attn: {model.talker.code_predictor.config._attn_implementation}")

    print("\nWarmup...")
    # Dummy inputs for 12Hz model
    # We need to simulate the inputs expected by the generate method
    # It's easier to just check the config first.

if __name__ == "__main__":
    benchmark()
